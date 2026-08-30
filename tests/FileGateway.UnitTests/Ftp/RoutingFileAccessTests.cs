using System.Reflection;
using FileGateway.Core.Files;
using FileGateway.Infrastructure.Ftp;

namespace FileGateway.UnitTests.Infrastructure.Ftp;

public sealed class RoutingFileAccessTests
{
    [Fact] // R1 — AC-22-1
    public async Task AC_22_1_LocalhostHostRoutesAllFourMethodsToLocal()
    {
        var local = new SpyFileAccess();
        var ftp = new SpyFileAccess();
        var routing = new RoutingFileAccess(local, ftp);
        var server = new FileServerConnection("S1", "localhost", "/logs");

        await routing.ListFilesAsync(server, "dir", CancellationToken.None);
        await routing.StatFileAsync(server, "file.log", CancellationToken.None);
        await routing.FileExistsAsync(server, "file.log", CancellationToken.None);
        await routing.OpenReadAsync(server, "file.log", CancellationToken.None);

        Assert.Equal(4, local.Calls);
        Assert.Equal(0, ftp.Calls);
        Assert.All(local.Servers, s => Assert.Same(server, s));
        Assert.Equal(["dir", "file.log", "file.log", "file.log"], local.Args);
    }

    [Fact] // R2 — AC-22-2
    public async Task AC_22_2_NonLocalhostHostRoutesAllFourMethodsToFtp()
    {
        var local = new SpyFileAccess();
        var ftp = new SpyFileAccess();
        var routing = new RoutingFileAccess(local, ftp);
        var server = new FileServerConnection("S1", "ftp01", "/logs");

        await routing.ListFilesAsync(server, "dir", CancellationToken.None);
        await routing.StatFileAsync(server, "file.log", CancellationToken.None);
        await routing.FileExistsAsync(server, "file.log", CancellationToken.None);
        await routing.OpenReadAsync(server, "file.log", CancellationToken.None);

        Assert.Equal(4, ftp.Calls);
        Assert.Equal(0, local.Calls);
    }

    [Fact]
    public async Task Directory_listing_routes_to_matching_access()
    {
        var local = new SpyFileAccess();
        var ftp = new SpyFileAccess();
        var routing = new RoutingFileAccess(local, ftp);

        await routing.ListDirectoriesAsync(
            new FileServerConnection("S1", "localhost", "/logs"), "local", CancellationToken.None);
        await routing.ListDirectoriesAsync(
            new FileServerConnection("S2", "ftp01", "/logs"), "remote", CancellationToken.None);

        Assert.Equal(1, local.Calls);
        Assert.Equal(1, ftp.Calls);
        Assert.Equal(["local"], local.Args);
        Assert.Equal(["remote"], ftp.Args);
    }

    [Theory] // R3, R4 — AC-22-6
    [InlineData("LOCALHOST")]
    [InlineData("LocalHost")]
    [InlineData(" localhost ")]
    public async Task AC_22_6_HostComparisonIgnoresCaseAndTrimsWhitespace(string host)
    {
        var local = new SpyFileAccess();
        var ftp = new SpyFileAccess();
        var routing = new RoutingFileAccess(local, ftp);

        await routing.StatFileAsync(new FileServerConnection("S1", host, "/logs"), "f", CancellationToken.None);

        Assert.Equal(1, local.Calls);
        Assert.Equal(0, ftp.Calls);
    }

    [Theory] // R5 — AC-22-6, AC-22-2
    [InlineData("127.0.0.1")]
    [InlineData("localhost.example.com")]
    [InlineData("localhost.")]
    [InlineData("local host")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AC_22_6_NonExactMatchHostsRouteToFtp(string? host)
    {
        var local = new SpyFileAccess();
        var ftp = new SpyFileAccess();
        var routing = new RoutingFileAccess(local, ftp);

        await routing.StatFileAsync(new FileServerConnection("S1", host!, "/logs"), "f", CancellationToken.None);

        Assert.Equal(1, ftp.Calls);
        Assert.Equal(0, local.Calls);
    }

    [Fact] // R6 — AC-22-3
    public async Task AC_22_3_DelegateExceptionsPassThroughUnwrapped()
    {
        var local = new SpyFileAccess { Throw = new FileAccessException(FileAccessError.IoFailure, "boom") };
        var ftp = new SpyFileAccess { Throw = new OperationCanceledException() };
        var routing = new RoutingFileAccess(local, ftp);
        var server = new FileServerConnection("S1", "localhost", "/logs");
        var other = new FileServerConnection("S2", "ftp01", "/logs");

        var fae = await Assert.ThrowsAsync<FileAccessException>(
            () => routing.StatFileAsync(server, "f", CancellationToken.None));
        Assert.Equal(FileAccessError.IoFailure, fae.Error);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => routing.StatFileAsync(other, "f", CancellationToken.None));
    }

    [Theory] // IsLocalHost null-safety — AC-22-6
    [InlineData("localhost", true)]
    [InlineData(" LOCALHOST ", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("127.0.0.1", false)]
    public void AC_22_6_IsLocalHostIsNullSafeAndExactMatch(string? host, bool expected)
    {
        var method = typeof(RoutingFileAccess).GetMethod(
            "IsLocalHost", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, [host]));
    }

    private sealed class SpyFileAccess : IFileAccess
    {
        public int Calls;
        public List<FileServerConnection> Servers = [];
        public List<string> Args = [];
        public Exception? Throw;

        private Task<T> Record<T>(FileServerConnection server, string arg, Func<T> result)
        {
            Calls++;
            Servers.Add(server);
            Args.Add(arg);
            if (Throw is not null) return Task.FromException<T>(Throw);
            return Task.FromResult(result());
        }

        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
            => Record(server, dir, () => RemoteDirectoryListing.Missing);
        public Task<long> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
            => Record(server, path, () => 0L);
        public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
            => Record(server, path, () => false);
        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
            => Record(server, path, () => new RemoteOpenRead(Stream.Null, 0));
        public Task<RemoteDirectoryNames> ListDirectoriesAsync(
            FileServerConnection server, string dir, CancellationToken ct)
            => Record(server, dir, () => RemoteDirectoryNames.Missing);
    }
}
