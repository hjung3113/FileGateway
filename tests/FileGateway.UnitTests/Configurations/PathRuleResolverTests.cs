using FileGateway.Core.Errors;
using FileGateway.Configurations.Internal;
using FileGateway.Core.Files;

namespace FileGateway.UnitTests.Configurations;

public class PathRuleResolverTests
{
    private static readonly FileServerConnection Srv = new("SRV1", "ftp1", "ftproot");
    private static readonly DateTimeOffset Slot = new(2026, 8, 29, 0, 0, 0, TimeSpan.FromHours(9));

    private static IReadOnlyList<CompiledPathSegment> Segments(string raw) => ConfigurationRuleParser.CompilePath(raw);
    private static PathRuleResolver Resolver(IFileAccess ftp) => new(ftp);


    [Fact]
    public async Task Template_only_path_returns_single_expanded_path_without_directory_enumeration()
    {
        var ftp = new ThrowingListDirsAccess(); // 열거 호출 시 예외 — regex가 없으면 호출되면 안 된다
        var dirs = await Resolver(ftp).ResolveAsync(Srv, Segments("PM/history/{yyyy}/{MM}/{dd}"), Slot, CancellationToken.None);
        Assert.Equal(["PM/history/2026/08/29"], dirs);
    }

    [Fact]
    public async Task Single_regex_level_fans_out_all_matches()
    {
        var ftp = new InMemoryFileAccess();
        foreach (var d in new[] { "cfg/PM1", "cfg/PM2", "cfg/pm3", "cfg/PMX", "cfg/Port3" })
            ftp.AddDirectory(d);
        var dirs = await Resolver(ftp).ResolveAsync(Srv, Segments("cfg/regex:^PM[0-9]$"), Slot, CancellationToken.None);
        Assert.Equal(["cfg/PM1", "cfg/PM2", "cfg/pm3"], dirs); // ci 정렬, 비매칭 제외
    }

    [Fact]
    public async Task Multiple_regex_levels_combine()
    {
        var ftp = new InMemoryFileAccess();
        foreach (var d in new[] { "cfg/PM1/UI", "cfg/PM1/TransferChamber", "cfg/PM2/UI", "cfg/PM2/Nope" })
            ftp.AddDirectory(d);
        var dirs = await Resolver(ftp).ResolveAsync(Srv,
            Segments("cfg/regex:^PM[0-9]$/regex:^(UI|TransferChamber)$"), Slot, CancellationToken.None);
        Assert.Equal(["cfg/PM1/TransferChamber", "cfg/PM1/UI", "cfg/PM2/UI"], dirs);
    }

    [Fact]
    public async Task Missing_parent_directory_prunes_branch_as_empty_result()
    {
        var ftp = new InMemoryFileAccess();
        ftp.AddDirectory("cfg/PM1");
        var dirs = await Resolver(ftp).ResolveAsync(Srv, Segments("cfg/regex:^PM[0-9]$"), Slot, CancellationToken.None);
        Assert.Equal(["cfg/PM1"], dirs);
        Assert.Empty(await Resolver(ftp).ResolveAsync(Srv, Segments("nope/regex:^PM[0-9]$"), Slot, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_directory_is_exists_true_and_yields_no_branches()
    {
        var ftp = new InMemoryFileAccess();
        ftp.AddDirectory("cfg");
        var dirs = await Resolver(ftp).ResolveAsync(Srv, Segments("cfg/regex:^PM[0-9]$"), Slot, CancellationToken.None);
        Assert.Empty(dirs);
    }

    [Fact]
    public async Task Unsafe_enumerated_child_names_are_skipped()
    {
        var ftp = new CustomNamesAccess("PM1", "bad:name", "..", ".");
        var dirs = await Resolver(ftp).ResolveAsync(Srv, Segments("regex:^.*$"), Slot, CancellationToken.None);
        Assert.Equal(["PM1"], dirs); // ':'·'..'·'.' 포함 이름은 결합 불가 — 매칭에서 제외(§6.2)
    }

    [Fact]
    public async Task Root_relative_first_regex_segment_enumerates_empty_path()
    {
        var ftp = new InMemoryFileAccess();
        ftp.AddDirectory("PM1");
        var dirs = await Resolver(ftp).ResolveAsync(Srv, Segments("regex:^PM[0-9]$"), Slot, CancellationToken.None);
        Assert.Equal(["PM1"], dirs);
    }
    private sealed class ThrowingListDirsAccess : IFileAccess
    {
        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection s, string d, CancellationToken ct)
            => Task.FromResult(RemoteDirectoryListing.Missing);
        public Task<RemoteDirectoryNames> ListDirectoriesAsync(FileServerConnection s, string d, CancellationToken ct)
            => throw new FileAccessException(FileAccessError.ProtocolError, "must not be called");
        public Task<long> StatFileAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> FileExistsAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
    }


    [Fact]
    public async Task Directory_regex_timeout_is_conflict()
    {
        // P1-S1: 디렉터리 매처 timeout도 FileDefinitionConflict로 분류된다(세 regex 종류 계약 통일).
        var ftp = new CustomNamesAccess(new string('a', 60) + "b");
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() => Resolver(ftp).ResolveAsync(
            Srv, Segments("regex:^(a+)+$"), Slot, CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task Regex_segment_with_backslash_escape_matches_directories()
    {
        // P1-S2: `regex:^PM\d$` 같은 escape 포함 pattern이 변형 없이 매칭에 쓰인다.
        var ftp = new InMemoryFileAccess();
        ftp.AddDirectory("cfg/PM1");
        ftp.AddDirectory("cfg/PMX");
        var dirs = await Resolver(ftp).ResolveAsync(Srv, Segments("cfg/regex:^PM\\d$"), Slot, CancellationToken.None);
        Assert.Equal(["cfg/PM1"], dirs);
    }

    private sealed class CustomNamesAccess(params string[] names) : IFileAccess
    {
        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection s, string d, CancellationToken ct)
            => Task.FromResult(RemoteDirectoryListing.Missing);
        public Task<RemoteDirectoryNames> ListDirectoriesAsync(FileServerConnection s, string d, CancellationToken ct)
            => Task.FromResult(new RemoteDirectoryNames(true, names));
        public Task<long> StatFileAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> FileExistsAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
    }
}
