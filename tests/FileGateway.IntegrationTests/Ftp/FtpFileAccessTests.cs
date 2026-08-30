using FileGateway.Core.Files;
using FileGateway.Infrastructure.Ftp;
using FluentFTP;

namespace FileGateway.IntegrationTests.Ftp;

public class FtpFileAccessTests(FtpAdapterFixture ftp) : IClassFixture<FtpAdapterFixture>
{
    private static FileServerConnection Server(int port) => new("S1", "127.0.0.1", "ftproot");

    private static (FtpFileAccess Access, FtpOptions Opt) Create(FtpAdapterFixture f)
    {
        var opt = new FtpOptions { UserName = FtpAdapterFixture.UserName, Password = FtpAdapterFixture.Password };
        return (new FtpFileAccess(opt, new FtpConcurrencyLimiter(opt)), opt);
    }

    private static async Task Seed(FtpAdapterFixture f, string path, byte[] content)
    {
        using var client = new AsyncFtpClient("127.0.0.1", FtpAdapterFixture.UserName, FtpAdapterFixture.Password, f.Port);
        await client.Connect();
        await client.UploadStream(new MemoryStream(content), path, createRemoteDir: true);
    }

    private static FtpOptions WithPort(FtpAdapterFixture f, FtpOptions o) { o.HostPortOverride = f.Port; return o; }

    [Fact]
    public async Task ListFiles_returns_entries_when_directory_exists()
    {
        await Seed(ftp, "ftproot/Logs/2026/08/22/18/Event_A.zip", "abc"u8.ToArray());
        var (access, opt) = Create(ftp); WithPort(ftp, opt);
        var listing = await access.ListFilesAsync(Server(ftp.Port), "Logs/2026/08/22/18", CancellationToken.None);
        Assert.True(listing.Exists);
        var file = Assert.Single(listing.Files);
        Assert.Equal("Event_A.zip", file.Name);
        Assert.Equal(3, file.Size);
    }

    [Fact]
    public async Task ListFiles_reports_missing_directory_as_not_exists()
    {
        var (access, opt) = Create(ftp); WithPort(ftp, opt);
        var listing = await access.ListFilesAsync(Server(ftp.Port), "Logs/nope", CancellationToken.None);
        Assert.False(listing.Exists);
        Assert.Empty(listing.Files);
    }

    [Fact]
    public async Task ListDirectories_returns_immediate_child_directories()
    {
        await Seed(ftp, "ftproot/DirectoryListing/PM1/file.cfg", "1"u8.ToArray());
        await Seed(ftp, "ftproot/DirectoryListing/PM2/file.cfg", "2"u8.ToArray());
        await Seed(ftp, "ftproot/DirectoryListing/PM2/Nested/file.cfg", "3"u8.ToArray());

        var (access, opt) = Create(ftp); WithPort(ftp, opt);
        var listing = await access.ListDirectoriesAsync(
            Server(ftp.Port), "DirectoryListing", CancellationToken.None);

        Assert.True(listing.Exists);
        Assert.Equal(["PM1", "PM2"], listing.Names.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ListDirectories_reports_missing_directory_as_not_exists()
    {
        var (access, opt) = Create(ftp); WithPort(ftp, opt);
        var listing = await access.ListDirectoriesAsync(
            Server(ftp.Port), "DirectoryListingMissing", CancellationToken.None);

        Assert.False(listing.Exists);
        Assert.Empty(listing.Names);
    }

    [Fact]
    public async Task ListDirectories_returns_empty_for_existing_directory_without_child_directories()
    {
        await Seed(ftp, "ftproot/DirectoryListingEmpty/file.cfg", "1"u8.ToArray());
        var (access, opt) = Create(ftp); WithPort(ftp, opt);

        var listing = await access.ListDirectoriesAsync(
            Server(ftp.Port), "DirectoryListingEmpty", CancellationToken.None);

        Assert.True(listing.Exists);
        Assert.Empty(listing.Names);
    }

    [Fact]
    public async Task StatFile_throws_FileNotFound_for_missing_file()
    {
        var (access, opt) = Create(ftp); WithPort(ftp, opt);
        var ex = await Assert.ThrowsAsync<FileAccessException>(
            () => access.StatFileAsync(Server(ftp.Port), "ftproot/missing.bin", CancellationToken.None));
        Assert.Equal(FileAccessError.FileNotFound, ex.Error);
    }

    [Fact]
    public async Task FileExists_distinguishes_missing_and_present()
    {
        await Seed(ftp, "ftproot/Logs/present.bin", "x"u8.ToArray());
        var (access, opt) = Create(ftp); WithPort(ftp, opt);
        Assert.True(await access.FileExistsAsync(Server(ftp.Port), "Logs/present.bin", CancellationToken.None));
        Assert.False(await access.FileExistsAsync(Server(ftp.Port), "Logs/absent.bin", CancellationToken.None));
    }

    [Fact]
    public async Task StatFile_rejects_directory_as_file()
    {
        await Seed(ftp, "ftproot/Logs/stat-directory-child.bin", "x"u8.ToArray());
        var (access, opt) = Create(ftp); WithPort(ftp, opt);

        var ex = await Assert.ThrowsAsync<FileAccessException>(
            () => access.StatFileAsync(Server(ftp.Port), "Logs", CancellationToken.None));

        Assert.Equal(FileAccessError.FileNotFound, ex.Error);
    }

    [Fact]
    public async Task FileExists_returns_false_for_directory()
    {
        await Seed(ftp, "ftproot/Logs/exists-directory-child.bin", "x"u8.ToArray());
        var (access, opt) = Create(ftp); WithPort(ftp, opt);

        Assert.False(await access.FileExistsAsync(Server(ftp.Port), "Logs", CancellationToken.None));
    }

    [Fact]
    public async Task OpenRead_returns_stream_and_length()
    {
        await Seed(ftp, "ftproot/Logs/data.bin", "0123456789"u8.ToArray());
        var (access, opt) = Create(ftp); WithPort(ftp, opt);
        var open = await access.OpenReadAsync(Server(ftp.Port), "Logs/data.bin", CancellationToken.None);
        await using var s = open.Stream;
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        Assert.Equal(10, ms.Length);
        Assert.Equal(10, open.Length);
    }

    [Fact]
    public async Task Wrong_credentials_map_to_AuthenticationFailed()
    {
        var opt = new FtpOptions { UserName = "nobody", Password = "bad" };
        WithPort(ftp, opt);
        var access = new FtpFileAccess(opt, new FtpConcurrencyLimiter(opt));
        var ex = await Assert.ThrowsAsync<FileAccessException>(
            () => access.ListFilesAsync(Server(ftp.Port), "Logs", CancellationToken.None));
        Assert.Equal(FileAccessError.AuthenticationFailed, ex.Error);
    }

    [Fact]
    public async Task Unreachable_host_maps_to_ConnectionFailed()
    {
        var opt = new FtpOptions { ConnectTimeoutSeconds = 2 };
        var access = new FtpFileAccess(opt, new FtpConcurrencyLimiter(opt));
        var ex = await Assert.ThrowsAsync<FileAccessException>(() => access.ListFilesAsync(
            new FileServerConnection("S1", "127.0.0.1", "ftproot"), "Logs", CancellationToken.None));
        // 127.0.0.1:21 거부 → ConnectionFailed. 옵션의 포트 오버라이드 없이 기본 21 사용.
        Assert.Equal(FileAccessError.ConnectionFailed, ex.Error);
    }

    [Fact]
    public void FtpConfig_maps_security_and_certificate_policy()
    {
        var plain = FtpOptions.ToFtpConfig(new FtpOptions());
        Assert.Equal(FtpEncryptionMode.None, plain.EncryptionMode);
        Assert.False(plain.ValidateAnyCertificate);
        Assert.Equal(15_000, plain.ConnectTimeout);
        Assert.Equal(60_000, plain.ReadTimeout);
        Assert.Equal(15_000, plain.DataConnectionConnectTimeout);
        Assert.Equal(60_000, plain.DataConnectionReadTimeout);

        var ftps = FtpOptions.ToFtpConfig(new FtpOptions
            { Security = FtpSecurity.ExplicitTls, AcceptUntrustedCertificates = true });
        Assert.Equal(FtpEncryptionMode.Explicit, ftps.EncryptionMode);
        Assert.True(ftps.ValidateAnyCertificate);

        var implicitFtps = FtpOptions.ToFtpConfig(new FtpOptions { Security = FtpSecurity.ImplicitTls });
        Assert.Equal(FtpEncryptionMode.Implicit, implicitFtps.EncryptionMode);
    }

    [Fact]
    public async Task Open_stream_holds_concurrency_lease_until_disposed()
    {
        await Seed(ftp, "ftproot/Logs/a.bin", "12345"u8.ToArray());
        await Seed(ftp, "ftproot/Logs/b.bin", "67890"u8.ToArray());
        var opt = new FtpOptions { UserName = FtpAdapterFixture.UserName, Password = FtpAdapterFixture.Password,
                                   MaxConcurrentPerServer = 1 };
        WithPort(ftp, opt);
        var access = new FtpFileAccess(opt, new FtpConcurrencyLimiter(opt));

        var first = await access.OpenReadAsync(Server(ftp.Port), "Logs/a.bin", CancellationToken.None);
        // 첫 스트림이 살아있는 동안 같은 서버의 두 번째 open은 permit 대기로 timeout/fail해야 한다
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => access.OpenReadAsync(Server(ftp.Port), "Logs/b.bin", cts.Token));

        await first.Stream.DisposeAsync(); // lease 해제
        var second = await access.OpenReadAsync(Server(ftp.Port), "Logs/b.bin", CancellationToken.None);
        await second.Stream.DisposeAsync();
    }

    [Fact]
    public async Task Sync_dispose_releases_permit_for_immediate_reacquisition()
    {
        await Seed(ftp, "ftproot/Logs/a.bin", "12345"u8.ToArray());
        await Seed(ftp, "ftproot/Logs/b.bin", "67890"u8.ToArray());
        var opt = new FtpOptions { UserName = FtpAdapterFixture.UserName, Password = FtpAdapterFixture.Password,
                                   MaxConcurrentPerServer = 1 };
        WithPort(ftp, opt);
        var access = new FtpFileAccess(opt, new FtpConcurrencyLimiter(opt));

        var first = await access.OpenReadAsync(Server(ftp.Port), "Logs/a.bin", CancellationToken.None);
        // 선행 확인: permit 상한 1에서 첫 스트림이 살아있으면 두 번째 open은 대기하다 취소된다
        using var blocked = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => access.OpenReadAsync(Server(ftp.Port), "Logs/b.bin", blocked.Token));

        first.Stream.Dispose(); // sync dispose — inner/client/lease 모두 해제돼야 한다
        using var reopened = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // 해제 누수면 즉시 실패
        var second = await access.OpenReadAsync(Server(ftp.Port), "Logs/b.bin", reopened.Token);
        await second.Stream.DisposeAsync();
    }

    [Fact]
    public async Task Double_dispose_sync_then_async_is_harmless()
    {
        await Seed(ftp, "ftproot/Logs/a.bin", "12345"u8.ToArray());
        var opt = new FtpOptions { UserName = FtpAdapterFixture.UserName, Password = FtpAdapterFixture.Password,
                                   MaxConcurrentPerServer = 1 };
        WithPort(ftp, opt);
        var access = new FtpFileAccess(opt, new FtpConcurrencyLimiter(opt));

        var opened = await access.OpenReadAsync(Server(ftp.Port), "Logs/a.bin", CancellationToken.None);
        opened.Stream.Dispose();
        await opened.Stream.DisposeAsync(); // sync 후 async 이중 해제 — 예외 없이 정확히 1회만 반환

        using var reacquired = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // permit이 정상 상태인지 재획득으로 증명
        var second = await access.OpenReadAsync(Server(ftp.Port), "Logs/a.bin", reacquired.Token);
        await second.Stream.DisposeAsync();
    }
}
