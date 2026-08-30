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

    private static ResolvedConfigurationDefinition RegexDef(string? mode = null, string? pattern = null,
        ConfigurationMetadataMapping[]? mappings = null, string? filePattern = null)
        => new(new EquipmentConfigurationDefinition("EQ-001", "PM", "SRV1",
            new CurrentRule("PM/current", "PM*.cfg"),
            new HistoryRule("PM/history/{yyyy}/{MM}/{dd}/regex:^PM[0-9]$",
                filePattern ?? (mode == "Regex" ? @"^\d{10}\.(zip|txt\.gz)$" : "*"),
                "PM/history/{yyyy}/{MM}/{dd}/_DONE",
                mode ?? "",
                mappings is null ? null : new ConfigurationMetadataRule(
                    mode == "Regex" ? ConfigurationMetadataMode.Regex : ConfigurationMetadataMode.Template,
                    pattern ?? "{yyyy}{MM}{dd}{HH}", mappings))), Srv);

    [Fact]
    public async Task Regex_segments_and_metadata_extract_snapshot_timestamp()
    {
        var ftp = new InMemoryFileAccess();
        ftp.AddFile("PM/history/2026/08/22/PM1/2026082220.zip", "1"u8.ToArray());
        ftp.AddFile("PM/history/2026/08/22/PM1/2026082220.txt.gz", "2"u8.ToArray());
        ftp.AddFile("PM/history/2026/08/22/PM2/2026082221.zip", "3"u8.ToArray());
        ftp.AddFile("PM/history/2026/08/22/_DONE", []);
        var files = await new HistoryResolver(ftp).ResolveAsync(
            RegexDef("Glob", mappings: []), Range(22), CancellationToken.None);
        // Template stem 매칭 — 같은 stem(.zip/.txt.gz)이 동일 ts 20:00 Set을 이룬다.
        Assert.Equal(3, files.Count); // PM1 20:00 Set(zip+txt.gz) + PM2 21:00
        Assert.Equal(21, files[0].SnapshotTimestamp.Hour); // ts DESC 정렬
        Assert.All(files, f => Assert.Equal(22, f.SnapshotTimestamp.Day));
        Assert.Equal("PM/history/2026/08/22/PM2/2026082221.zip", files[0].RelativePath);
        Assert.Equal(20, files[1].SnapshotTimestamp.Hour);
    }

    [Fact]
    public async Task Slot_and_extracted_timestamp_date_mismatch_is_conflict()
    {
        var ftp = new InMemoryFileAccess();
        // 물리 슬롯 08-22, 추출 ts 2026-08-23 20:00 — round-trip 불변식 위반(P1-N2).
        ftp.AddFile("PM/history/2026/08/22/PM1/2026082320.zip", "1"u8.ToArray());
        ftp.AddFile("PM/history/2026/08/22/_DONE", []);
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() => new HistoryResolver(ftp).ResolveAsync(
            RegexDef("Glob", mappings: []),
            new(new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.FromHours(9))), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task Metadata_failure_on_matching_candidate_is_conflict()
    {
        var ftp = new InMemoryFileAccess();
        ftp.AddFile("PM/history/2026/08/22/PM1/readme.zip", "1"u8.ToArray()); // FilePattern 통과, ts 추출 실패
        ftp.AddFile("PM/history/2026/08/22/_DONE", []);
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() => new HistoryResolver(ftp).ResolveAsync(
            new ResolvedConfigurationDefinition(new EquipmentConfigurationDefinition("EQ-001", "PM", "SRV1",
                new CurrentRule("PM/current", "PM*.cfg"),
                new HistoryRule("PM/history/{yyyy}/{MM}/{dd}/regex:^PM[0-9]$", "*", "PM/history/{yyyy}/{MM}/{dd}/_DONE",
                    "Glob", new ConfigurationMetadataRule(ConfigurationMetadataMode.Regex,
                        @"^(?<ts>\d{10})\.(zip|txt\.gz)$",
                        [new ConfigurationMetadataMapping("ts", "timestamp", "yyyyMMddHH")]))), Srv),
            Range(22), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task Out_of_range_extracted_time_is_conflict_not_normalized()
    {
        // P1-S3: mm=60 후보는 보정 없이 FileDefinitionConflict — 잘못된 ts의 identity 발급을 막는다.
        var ftp = new InMemoryFileAccess();
        ftp.AddFile("PM/history/2026/08/22/PM1/202608221260.zip", "1"u8.ToArray());
        ftp.AddFile("PM/history/2026/08/22/_DONE", []);
        var def = new ResolvedConfigurationDefinition(new EquipmentConfigurationDefinition("EQ-001", "PM", "SRV1",
            new CurrentRule("PM/current", "PM*.cfg"),
            new HistoryRule("PM/history/{yyyy}/{MM}/{dd}/regex:^PM[0-9]$", "*",
                "PM/history/{yyyy}/{MM}/{dd}/_DONE", "Glob",
                new ConfigurationMetadataRule(ConfigurationMetadataMode.Template, "{yyyy}{MM}{dd}{HH}{mm}", []))), Srv);
        var ex = await Assert.ThrowsAsync<FileGatewayException>(
            () => new HistoryResolver(ftp).ResolveAsync(def, Range(22), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task From_to_filter_uses_extracted_timestamp_when_metadata_present()
    {
        var ftp = new InMemoryFileAccess();
        // from=08-22 12:00(KST) — 추출 ts 20:00은 포함, 슬롯 자정 아님에도 결과가 나온다.
        ftp.AddFile("PM/history/2026/08/22/PM1/2026082220.zip", "1"u8.ToArray());
        ftp.AddFile("PM/history/2026/08/22/_DONE", []);
        var files = await new HistoryResolver(ftp).ResolveAsync(
            RegexDef("Glob", mappings: []),
            new(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.FromHours(9))), CancellationToken.None);
        var f = Assert.Single(files);
        Assert.Equal(20, f.SnapshotTimestamp.Hour);
    }

    [Fact]
    public async Task Same_name_different_extracted_ts_are_distinct()
    {
        var ftp = new InMemoryFileAccess();
        // 서로 다른 폴더에서 동일 fileName이지만 추출 ts가 달라 dedupe 키 (ts, ci-name)가 다르다.
        ftp.AddFile("PM/history/2026/08/22/PM1/2026082220.zip", "1"u8.ToArray());
        ftp.AddFile("PM/history/2026/08/23/PM1/2026082320.zip", "2"u8.ToArray());
        ftp.AddFile("PM/history/2026/08/22/_DONE", []);
        ftp.AddFile("PM/history/2026/08/23/_DONE", []);
        var files = await new HistoryResolver(ftp).ResolveAsync(
            RegexDef("Glob", mappings: []),
            new(new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.FromHours(9))), CancellationToken.None);
        Assert.Equal(2, files.Count);
    }

    private sealed class ListingFileAccess(RemoteDirectoryListing listing) : IFileAccess
    {
        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection s, string d, CancellationToken ct)
            => Task.FromResult(listing);

        public Task<RemoteDirectoryNames> ListDirectoriesAsync(
            FileServerConnection s, string d, CancellationToken ct)
            => Task.FromResult(RemoteDirectoryNames.Missing);

        public Task<long> StatFileAsync(FileServerConnection s, string p, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> FileExistsAsync(FileServerConnection s, string p, CancellationToken ct)
            => Task.FromResult(true); // marker 존재로 간주

        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection s, string p, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
