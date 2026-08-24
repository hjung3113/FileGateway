using FileGateway.Configurations.Definitions;
using FileGateway.Configurations.Internal;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Time;
using FileGateway.UnitTests.TestUtils;

namespace FileGateway.UnitTests.Configurations;

public class HistoryResolverTests
{
    private static readonly FileServerConnection Srv = new("SRV1", "ftp1", "ftproot");
    private static ResolvedConfigurationDefinition Def()
        => new(new EquipmentConfigurationDefinition("EQ-001", "PM", "SRV1",
               new CurrentRule("PM/current", "PM*.cfg"),
               new HistoryRule("PM/history/{yyyy}/{MM}/{dd}", "PM*.cfg", "PM/history/{yyyy}/{MM}/{dd}/_DONE")),
               Srv);

    private static EffectiveRange Range(int day)
        => new(new DateTimeOffset(2026, 8, day, 0, 0, 0, TimeSpan.FromHours(9)),
               new DateTimeOffset(2026, 8, day + 1, 0, 0, 0, TimeSpan.FromHours(9)));

    private static void Seed(FakeFileAccess ftp, int day, params string[] files)
    {
        var d = $"PM/history/2026/08/{day:00}";
        foreach (var f in files) ftp.AddFile($"{d}/{f}", new byte[f.Length]);
        if (files.Length > 0) ftp.AddFile($"{d}/_DONE", []); // marker: 존재만
    }

    [Fact]
    public async Task Only_marked_snapshot_sets_are_included()
    {
        var ftp = new FakeFileAccess();
        Seed(ftp, 22, "PM1.cfg", "PM2.cfg"); // marker 있음
        ftp.AddFile("PM/history/2026/08/21/PM1.cfg", "x"u8.ToArray()); // marker 없음
        var files = await new HistoryResolver(ftp).ResolveAsync(
            Def(),
            new(new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.FromHours(9))), CancellationToken.None);
        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.Equal(22, f.SnapshotTimestamp.Day));
    }

    [Fact]
    public async Task Marker_matching_broad_glob_is_excluded_from_results()
    {
        // FilePattern이 marker와도 일치하는 경우 — marker 자체는 결과가 아니다(04b).
        var def = new ResolvedConfigurationDefinition(new EquipmentConfigurationDefinition("EQ-001", "PM", "SRV1",
            new CurrentRule("PM/current", "*"),
            new HistoryRule("PM/history/{yyyy}/{MM}/{dd}", "*", "PM/history/{yyyy}/{MM}/{dd}/_DONE")),
            Srv);
        var ftp = new FakeFileAccess();
        Seed(ftp, 22, "PM1.cfg");
        var files = await new HistoryResolver(ftp).ResolveAsync(def, Range(22), CancellationToken.None);
        var name = Assert.Single(files).Entry.Name;
        Assert.Equal("PM1.cfg", name); // _DONE은 glob 일치에도 제외
    }

    [Fact]
    public async Task Marker_file_itself_is_not_a_result()
    {
        var ftp = new FakeFileAccess();
        Seed(ftp, 22, "PM1.cfg");
        var files = await new HistoryResolver(ftp).ResolveAsync(Def(), Range(22), CancellationToken.None);
        Assert.Equal("PM1.cfg", Assert.Single(files).Entry.Name); // _DONE은 glob 불일치로 제외
    }

    [Fact]
    public async Task Sorts_by_snapshot_desc_then_name()
    {
        var ftp = new FakeFileAccess();
        Seed(ftp, 21, "PM1.cfg");
        Seed(ftp, 22, "pm2.cfg", "PM1.cfg");
        var files = await new HistoryResolver(ftp).ResolveAsync(
            Def(),
            new(new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.FromHours(9))), CancellationToken.None);
        Assert.Equal(
        [
            new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.FromHours(9)),
        ], files.Select(f => f.SnapshotTimestamp));
        Assert.Equal("PM1.cfg", files[0].Entry.Name); // 동일 시각 내 이름 오름차순
    }

    [Fact]
    public async Task Non_midnight_from_excludes_that_days_snapshot()
    {
        var ftp = new FakeFileAccess();
        Seed(ftp, 22, "PM1.cfg");
        Seed(ftp, 23, "PM1.cfg");
        var files = await new HistoryResolver(ftp).ResolveAsync(
            Def(),
            new(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.FromHours(9))), CancellationToken.None);
        var ts = Assert.Single(files).SnapshotTimestamp;
        Assert.Equal(23, ts.Day);
    }

    [Fact]
    public async Task Duplicate_case_insensitive_name_is_conflict()
    {
        // FakeFileAccess는 경로 키가 case-insensitive라 case-only 중복 두 건을 담지 못한다 —
        // listing fake로 동일 디렉터리에 중복이 보이는 경우를 만든다(CurrentResolverTests 패턴).
        var ftp = new ListingFileAccess(new RemoteDirectoryListing(true,
        [
            new RemoteFileEntry("PM1.cfg", 1),
            new RemoteFileEntry("pm1.CFG", 2),
        ]));
        var ex = await Assert.ThrowsAsync<FileGatewayException>(
            () => new HistoryResolver(ftp).ResolveAsync(Def(), Range(22), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    private sealed class ListingFileAccess(RemoteDirectoryListing listing) : IFileAccess
    {
        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection s, string d, CancellationToken ct)
            => Task.FromResult(listing);

        public Task<long> StatFileAsync(FileServerConnection s, string p, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> FileExistsAsync(FileServerConnection s, string p, CancellationToken ct)
            => Task.FromResult(true); // marker 존재로 간주

        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection s, string p, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
