using System.Net.Sockets;
using FileGateway.Core.Files;
using FileGateway.Core.Paths;
using FluentFTP;
using FluentFTP.Exceptions;

namespace FileGateway.Infrastructure.Ftp;

/// <summary>
/// FluentFTP 기반 IFileAccess. 연결과 연결 이후 명령 오류를 같은 규칙으로 FileAccessError로 변환하며,
/// OpenReadAsync 반환 스트림이 pool checkout을 소유해 다운로드 중에도 한도가 유지된다.
/// </summary>
public sealed class FtpFileAccess(FtpClientPool pool) : IFileAccess
{
    public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
        => WrapAsync(() => pool.RunAsync(server, async (client, token) =>
        {
            // FubarDev류 서버는 LIST 550을 빈 결과로 돌릴 수 있어, MLST 부재 판정을 먼저 한다.
            if (await GetObjectInfoOrNullAsync(client, server, dir, token) is null)
                return RemoteDirectoryListing.Missing;
            var items = await client.GetListing(
                AbsolutePath(server, dir), FtpListOption.Modify | FtpListOption.Size, token);
            return new RemoteDirectoryListing(true,
                items.Where(i => i.Type == FtpObjectType.File)
                     .Select(i => new RemoteFileEntry(i.Name, i.Size)).ToList());
        }, ct));

    public Task<RemoteDirectoryNames> ListDirectoriesAsync(FileServerConnection server, string dir, CancellationToken ct)
        => WrapAsync(() => pool.RunAsync(server, async (client, token) =>
        {
            var info = await GetObjectInfoOrNullAsync(client, server, dir, token);
            if (info?.Type != FtpObjectType.Directory)
                return RemoteDirectoryNames.Missing;

            var items = await client.GetListing(
                AbsolutePath(server, dir), FtpListOption.Modify | FtpListOption.Size, token);
            return new RemoteDirectoryNames(true,
                items.Where(i => i.Type == FtpObjectType.Directory && IsSafeDirectoryName(i.Name))
                     .Select(i => i.Name).ToList());
        }, ct));

    public Task<long> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
        => WrapAsync(() => pool.RunAsync(server, async (client, token) =>
        {
            var info = await GetObjectInfoOrNullAsync(client, server, path, token);
            if (info?.Type != FtpObjectType.File)
                throw new FileAccessException(FileAccessError.FileNotFound, "file not found");
            return info.Size;
        }, ct));

    public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
        => WrapAsync(() => pool.RunAsync(server, async (client, token) =>
        {
            var info = await GetObjectInfoOrNullAsync(client, server, path, token);
            return info?.Type == FtpObjectType.File;
        }, ct));

    public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
        => WrapAsync(() => OpenReadCoreAsync(server, path, ct));

    private async Task<RemoteOpenRead> OpenReadCoreAsync(
        FileServerConnection server, string path, CancellationToken ct)
    {
        // checkout을 반환 스트림이 소유: 다운로드가 끝나야 client가 반납/폐기되고 permit이 해제된다.
        var checkout = await pool.CheckoutAsync(server, ct);
        try
        {
            return await OpenCheckedOutAsync(checkout, server, path, ct);
        }
        catch (Exception ex) when (checkout.Recycled
                                   && !ct.IsCancellationRequested
                                   && FtpClientPool.IsTransportFailure(ex))
        {
            await pool.DiscardAsync(checkout.Client);
            AsyncFtpClient? fresh = null;
            try
            {
                fresh = await pool.ConnectNewAsync(server, ct);
                var replacement = checkout with { Client = fresh, Recycled = false };
                return await OpenCheckedOutAsync(replacement, server, path, ct);
            }
            catch
            {
                if (fresh is not null) await pool.DiscardAsync(fresh);
                await checkout.Lease.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await pool.DiscardAsync(checkout.Client);
            await checkout.Lease.DisposeAsync();
            throw;
        }
    }

    private async Task<RemoteOpenRead> OpenCheckedOutAsync(FtpClientPool.Checkout checkout,
        FileServerConnection server, string path, CancellationToken ct)
    {
        var full = AbsolutePath(server, path);
        var info = await GetObjectInfoOrNullAsync(checkout.Client, server, path, ct); // 시작 직전 크기 관측
        if (info is null) throw new FileAccessException(FileAccessError.FileNotFound, "file not found");
        var stream = await checkout.Client.OpenRead(full, FtpDataType.Binary, 0, checkIfFileExists: false, ct);
        return new RemoteOpenRead(new OwnedFtpStream(stream, pool, checkout, info.Size), info.Size);
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

    private static bool IsSafeDirectoryName(string? name)
        => !string.IsNullOrEmpty(name)
           && name is not "." and not ".."
           && name.IndexOfAny(['/', '\\', ':']) < 0;

    /// <summary>서버 루트 기준 절대 경로. 상대 경로는 서버 CWD에 의존해 침투 위험이 있으므로 항상 루팅한다.</summary>
    private static string AbsolutePath(FileServerConnection server, string relative)
        => "/" + RemotePath.Combine(server.RootPath, relative);

    private static bool IsFileNotFoundReply(FtpException ex)
        => ex is FtpMissingObjectException
           || (ex as FtpCommandException)?.CompletionCode == "550";

    /// <summary>OpenRead 반환용 스트림. Dispose/DisposeAsync에서 data stream, checkout, permit을 정리한다.</summary>
    private sealed class OwnedFtpStream(
        Stream inner,
        FtpClientPool pool,
        FtpClientPool.Checkout checkout,
        long declaredLength) : Stream
    {
        private int _disposed; // sync/async 경로 간 이중 해제 방지
        private long _delivered;
        private bool _innerEof;
        private bool _readFailed;

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
            try
            {
                var read = inner.Read(buffer, offset, count);
                if (read > 0) _delivered += read;
                else if (count > 0) _innerEof = true;
                return read;
            }
            catch (Exception ex)
            {
                _readFailed = true;
                if (ex is FileAccessException or OperationCanceledException) throw;
                throw Classify(ex);
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
        {
            try
            {
                var read = await inner.ReadAsync(buffer, ct);
                if (read > 0) _delivered += read;
                else if (buffer.Length > 0) _innerEof = true;
                return read;
            }
            catch (Exception ex)
            {
                _readFailed = true;
                if (ex is FileAccessException or OperationCanceledException) throw;
                throw Classify(ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            base.Dispose(disposing);
            if (!disposing) return;
            // 동기 경로에서는 완료 응답을 검증할 수 없으므로 client를 항상 폐기한다.
            try { inner.Dispose(); } catch { /* best-effort teardown */ }
            try { checkout.Client.Dispose(); } catch { /* best-effort teardown */ }
            try { checkout.Lease.DisposeAsync().GetAwaiter().GetResult(); } catch { /* best-effort teardown */ }
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            try { await TeardownAsync(); } catch { /* best-effort teardown */ }
        }

        private async ValueTask TeardownAsync()
        {
            var closable = inner as FtpSocketStream;
            var eligible = !_readFailed
                           && (_delivered == declaredLength || _innerEof)
                           && closable is not null;
            if (eligible)
            {
                try
                {
                    await closable!.CloseAsync(CancellationToken.None);
                    if (checkout.Client.IsConnected)
                    {
                        pool.Return(checkout.Slot, checkout.Client);
                        await checkout.Lease.DisposeAsync();
                        return;
                    }
                }
                catch
                {
                    // 완료 응답 검증 실패는 안전한 폐기로 진행한다.
                }
            }

            try { await inner.DisposeAsync(); } catch { /* best-effort teardown */ }
            try { await pool.DiscardAsync(checkout.Client); } catch { /* best-effort teardown */ }
            try { await checkout.Lease.DisposeAsync(); } catch { /* best-effort teardown */ }
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
