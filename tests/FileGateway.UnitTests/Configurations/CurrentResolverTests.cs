using FileGateway.Configurations.Definitions;
using FileGateway.Configurations.Internal;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.UnitTests.TestUtils;

namespace FileGateway.UnitTests.Configurations;

public class CurrentResolverTests
{
    private static readonly FileServerConnection Srv = new("SRV1", "ftp1", "ftproot");
    private static ResolvedConfigurationDefinition Def()
        => new(new EquipmentConfigurationDefinition("EQ-001", "PM", "SRV1",
               new CurrentRule("PM/current", "PM*.cfg"),
               new HistoryRule("PM/history/{yyyy}/{MM}/{dd}", "PM*.cfg", "PM/history/{yyyy}/{MM}/{dd}/_DONE")),
               Srv);

    [Fact]
    public async Task Returns_all_current_files_sorted_case_insensitive()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("PM/current/pm2.cfg", "22"u8.ToArray());
        ftp.AddFile("PM/current/PM1.cfg", "11"u8.ToArray());
        var files = await new CurrentResolver(ftp).ResolveAsync(Def(), CancellationToken.None);
        Assert.Equal(["PM1.cfg", "pm2.cfg"], files.Select(f => f.Entry.Name));
    }

    [Fact]
    public async Task Missing_directory_returns_empty_list()
        => Assert.Empty(await new CurrentResolver(new FakeFileAccess()).ResolveAsync(Def(), CancellationToken.None));

    [Fact]
    public async Task Case_insensitive_duplicate_is_conflict()
    {
        var ftp = new ListingFileAccess(new RemoteDirectoryListing(true,
        [
            new RemoteFileEntry("PM1.cfg", 1),
            new RemoteFileEntry("pm1.CFG", 2),
        ]));
        var ex = await Assert.ThrowsAsync<FileGatewayException>(
            () => new CurrentResolver(ftp).ResolveAsync(Def(), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task Non_matching_files_excluded()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("PM/current/PM1.cfg", "1"u8.ToArray());
        ftp.AddFile("PM/current/readme.txt", "2"u8.ToArray());
        var files = await new CurrentResolver(ftp).ResolveAsync(Def(), CancellationToken.None);
        Assert.Single(files);
    }

    [Fact]
    public async Task Regex_directory_segments_fan_out_and_combine()
    {
        var ftp = new InMemoryFileAccess();
        ftp.AddFile("PM/current/PM1/a.cfg", "1"u8.ToArray());
        ftp.AddFile("PM/current/PM2/b.cfg", "2"u8.ToArray());
        ftp.AddFile("PM/current/PMX/c.cfg", "3"u8.ToArray());
        var def = new ResolvedConfigurationDefinition(new EquipmentConfigurationDefinition("EQ-001", "PM", "SRV1",
            new CurrentRule("PM/current/regex:^PM[0-9]$", @"^\w+\.cfg$", "Regex"),
            new HistoryRule("PM/history/{yyyy}/{MM}/{dd}", "PM*.cfg", "PM/history/{yyyy}/{MM}/{dd}/_DONE")), Srv);
        var files = await new CurrentResolver(ftp).ResolveAsync(def, CancellationToken.None);
        Assert.Equal(["a.cfg", "b.cfg"], files.Select(f => f.Entry.Name));
        Assert.Equal("PM/current/PM1/a.cfg", files[0].RelativePath);
    }

    [Fact]
    public async Task Date_tokens_expand_once_from_captured_slot()
    {
        var ftp = new InMemoryFileAccess();
        ftp.AddFile("PM/current/2026-08-30/a.cfg", "2"u8.ToArray());
        var def = new ResolvedConfigurationDefinition(new EquipmentConfigurationDefinition("EQ-001", "PM", "SRV1",
            new CurrentRule("PM/current/{yyyy}-{MM}-{dd}", "*.cfg"),
            new HistoryRule("PM/history/{yyyy}/{MM}/{dd}", "PM*.cfg", "PM/history/{yyyy}/{MM}/{dd}/_DONE")), Srv);
        // 고정 시각(2026-08-30 04:30 UTC = 13:30 KST) — token 없는 Current path의 무변경은
        // token 있는 path가 문서화된 계약(확장)을 따르는지 검증한다(P2-2).
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 30, 4, 30, 0, TimeSpan.Zero));
        var files = await new CurrentResolver(ftp, clock).ResolveAsync(def, CancellationToken.None);
        var f = Assert.Single(files);
        Assert.Equal("PM/current/2026-08-30/a.cfg", f.RelativePath); // slot 날짜(2026-08-30 KST)만 방문
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
            => throw new NotSupportedException();

        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection s, string p, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
