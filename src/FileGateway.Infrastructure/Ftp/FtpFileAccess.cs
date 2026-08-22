using System.Net.Sockets;
using FileGateway.Core.Files;
using FileGateway.Core.Paths;
using FluentFTP;
using FluentFTP.Exceptions;

namespace FileGateway.Infrastructure.Ftp;

/// <summary>
/// FluentFTP 기반 IFileAccess. 연결과 연결 이후 명령 오류를 같은 규칙으로 FileAccessError로 변환하며,
/// OpenReadAsync 반환 스트림이 client와 동시성 lease를 소유해 다운로드 중에도 한도가 유지된다.
/// </summary>
public sealed class FtpFileAccess(FtpOptions options, FtpConcurrencyLimiter limiter) : IFileAccess
{
    public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
        => limiter.RunAsync(server, token => WrapAsync(async () =>
        {
            using var client = await ConnectAsync(server, token);
            // FubarDev류 서버는 LIST 550을 빈 결과로 돌릴 수 있어, MLST 부재 판정을 먼저 한다.
            if (await GetObjectInfoOrNullAsync(client, server, dir, token) is null)
                return RemoteDirectoryListing.Missing;
            var items = await client.GetListing(
                AbsolutePath(server, dir), FtpListOption.Modify | FtpListOption.Size, token);
            return new RemoteDirectoryListing(true,
                items.Where(i => i.Type == FtpObjectType.File)
                     .Select(i => new RemoteFileEntry(i.Name, i.Size)).ToList());
        }), ct);

    public Task<long> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
        => limiter.RunAsync(server, token => WrapAsync(async () =>
        {
            using var client = await ConnectAsync(server, token);
            var info = await GetObjectInfoOrNullAsync(client, server, path, token);
            if (info?.Type != FtpObjectType.File)
                throw new FileAccessException(FileAccessError.FileNotFound, "file not found");
            return info.Size;
        }), ct);

    public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
        => limiter.RunAsync(server, token => WrapAsync(async () =>
        {
            using var client = await ConnectAsync(server, token);
            var info = await GetObjectInfoOrNullAsync(client, server, path, token);
            return info?.Type == FtpObjectType.File;
        }), ct);

    public async Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
    {
        // lease와 client를 반환 스트림이 소유: 다운로드가 끝나야 permit이 해제된다.
        var lease = await limiter.AcquireAsync(server, ct);
        AsyncFtpClient? client = null;
        try
        {
            client = await ConnectAsync(server, ct);
            var full = AbsolutePath(server, path);
            var info = await GetObjectInfoOrNullAsync(client, server, path, token: ct); // 시작 직전 크기 관측
            if (info is null) throw new FileAccessException(FileAccessError.FileNotFound, "file not found");
            var stream = await client.OpenRead(full, FtpDataType.Binary, 0, checkIfFileExists: false, ct);
            return new RemoteOpenRead(new OwnedFtpStream(stream, client, lease), info.Size);
        }
        catch (Exception ex)
        {
            if (client is not null) { try { await client.DisposeAsync(); } catch { /* 원본 오류 유지 */ } }
            await lease.DisposeAsync();
            if (ex is FileAccessException or OperationCanceledException) throw;
            throw Classify(ex); // 연결/명령 구분 없이 동일 매핑
        }
    }

    private static async Task<FtpListItem?> GetObjectInfoOrNullAsync(
        AsyncFtpClient client, FileServerConnection server, string path, CancellationToken token)
    {
        try { return await client.GetObjectInfo(AbsolutePath(server, path), dateModified: false, token); }
        catch (FtpException ex) when (IsFileNotFoundReply(ex)) { return null; } // MLST 550 → 부재
    }

    /// <summary>연결·명령 구분 없이 모든 FTP 오류를 FileAccessError로 변환한다.</summary>
    private static async Task<T> WrapAsync<T>(Func<Task<T>> op)
    {
        try { return await op(); }
        catch (Exception ex) when (ex is not FileAccessException and not OperationCanceledException)
        {
            throw Classify(ex);
        }
    }

    private static FileAccessException Classify(Exception ex)
    {
        // 연결 거부가 FtpException(SocketException inner) 또는 IOException으로 감싸져 나올 수 있어
        // 전송 계열 오류를 I/O 폴백보다 먼저 전체 예외 체인에서 찾는다.
        for (var e = (Exception?)ex; e is not null; e = e.InnerException)
        {
            if (e is FtpAuthenticationException) return new(FileAccessError.AuthenticationFailed, "ftp auth failed", ex);
            if (e is SocketException) return new(FileAccessError.ConnectionFailed, "ftp connection failed", ex);
            if (e is TimeoutException) return new(FileAccessError.Timeout, "ftp timeout", ex);
        }

        for (var e = (Exception?)ex; e is not null; e = e.InnerException)
        {
            if (e is IOException) return new(FileAccessError.IoFailure, "ftp I/O failure", ex);
        }
        return ex is FtpException
            ? new(FileAccessError.ProtocolError, "ftp protocol error", ex)
            : new(FileAccessError.ProtocolError, "ftp failure", ex);
    }

    private async Task<AsyncFtpClient> ConnectAsync(FileServerConnection server, CancellationToken ct)
    {
        // FluentFTP는 빈 UserName을 ctor에서 거부한다. 미설정 credential은 anonymous로 접속한다.
        var client = new AsyncFtpClient(server.Host, options.UserName ?? "anonymous", options.Password ?? "",
            FtpOptions.ResolveHostPort(options), FtpOptions.ToFtpConfig(options));
        try { await client.Connect(ct); return client; }
        catch
        {
            // 연결 실패 경로의 dispose 오류가 원본 예외를 덮어쓰지 않게 한다.
            try { await client.DisposeAsync(); } catch { /* 원본 연결 오류 유지 */ }
            throw;
        }
    }

    /// <summary>서버 루트 기준 절대 경로. 상대 경로는 서버 CWD에 의존해 침투 위험이 있으므로 항상 루팅한다.</summary>
    private static string AbsolutePath(FileServerConnection server, string relative)
        => "/" + RemotePath.Combine(server.RootPath, relative);

    private static bool IsFileNotFoundReply(FtpException ex)
        => ex is FtpMissingObjectException
           || (ex as FtpCommandException)?.CompletionCode == "550";

    /// <summary>OpenRead 반환용 스트림. DisposeAsync에서 데이터 스트림, client, lease를 함께 해제한다.</summary>
    private sealed class OwnedFtpStream(Stream inner, AsyncFtpClient client, FtpConcurrencyLimiter.FtpLease lease) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override long Length => inner.Length;
        public override int Read(byte[] buffer, int offset, int count)
        {
            try { return inner.Read(buffer, offset, count); }
            catch (Exception ex) when (ex is not FileAccessException and not OperationCanceledException)
            {
                throw Classify(ex);
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
        {
            try { return await inner.ReadAsync(buffer, ct); }
            catch (Exception ex) when (ex is not FileAccessException and not OperationCanceledException)
            {
                throw Classify(ex);
            }
        }
        public override async ValueTask DisposeAsync()
        {
            // 해제 실패가 permit 누수로 이어지지 않게 각 단계를 best-effort로 처리한다.
            try { await inner.DisposeAsync(); } catch { /* best-effort teardown */ }
            try { await client.DisposeAsync(); } catch { /* best-effort teardown */ }
            await lease.DisposeAsync();
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
