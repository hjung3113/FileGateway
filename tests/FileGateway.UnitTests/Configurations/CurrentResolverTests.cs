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

    private sealed class ListingFileAccess(RemoteDirectoryListing listing) : IFileAccess
    {
        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection s, string d, CancellationToken ct)
            => Task.FromResult(listing);

        public Task<long> StatFileAsync(FileServerConnection s, string p, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> FileExistsAsync(FileServerConnection s, string p, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection s, string p, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
