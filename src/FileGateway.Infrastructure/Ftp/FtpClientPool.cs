using System.Collections.Concurrent;
using System.Net.Sockets;
using FileGateway.Core.Files;
using FluentFTP;

namespace FileGateway.Infrastructure.Ftp;

/// <summary>전체/서버별 FTP 동시성 permit과 서버별 재사용 가능한 연결을 함께 관리한다.</summary>
public sealed class FtpClientPool(FtpOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _global = new(options.MaxConcurrentGlobal, options.MaxConcurrentGlobal);
    private readonly ConcurrentDictionary<string, ServerPool> _perServer = new(StringComparer.OrdinalIgnoreCase);

    public sealed class ServerPool
    {
        public required SemaphoreSlim Gate;
        public required ConcurrentQueue<AsyncFtpClient> Idle;
    }

    public sealed record Checkout(AsyncFtpClient Client, ServerPool Slot, FtpLease Lease)
    {
        internal bool Recycled { get; init; }
    }

    public sealed class FtpLease(SemaphoreSlim global, SemaphoreSlim perServer) : IAsyncDisposable
    {
        private int _released;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1) return;
            perServer.Release();
            global.Release();
            await ValueTask.CompletedTask;
        }
    }

    public async Task<FtpLease> AcquireAsync(FileServerConnection server, CancellationToken ct)
    {
        var (lease, _) = await AcquireInternalAsync(server, ct);
        return lease;
    }

    public async Task<AsyncFtpClient> ConnectNewAsync(FileServerConnection server, CancellationToken ct)
    {
        // FluentFTP는 빈 UserName을 ctor에서 거부한다. 미설정 credential은 anonymous로 접속한다.
        var client = new AsyncFtpClient(server.Host, options.UserName ?? "anonymous", options.Password ?? "",
            FtpOptions.ResolveHostPort(options), FtpOptions.ToFtpConfig(options));
        try
        {
            await client.Connect(ct);
            return client;
        }
        catch
        {
            // 연결 실패 경로의 dispose 오류가 원본 예외를 덮어쓰지 않게 한다.
            try { await client.DisposeAsync(); } catch { /* 원본 연결 오류 유지 */ }
            throw;
        }
    }

    public async Task<T> RunAsync<T>(FileServerConnection server,
        Func<AsyncFtpClient, CancellationToken, Task<T>> op, CancellationToken ct)
    {
        var (lease, slot) = await AcquireInternalAsync(server, ct);
        await using var _ = lease;
        var (client, recycled) = await TakeAsync(slot, server, ct);

        try
        {
            var result = await op(client, ct);
            slot.Idle.Enqueue(client);
            return result;
        }
        catch (Exception ex) when (recycled && !ct.IsCancellationRequested && IsTransportFailure(ex))
        {
            await DestroyAsync(client);
            var fresh = await ConnectNewAsync(server, ct);
            try
            {
                var result = await op(fresh, ct);
                slot.Idle.Enqueue(fresh);
                return result;
            }
            catch
            {
                await DestroyAsync(fresh);
                throw;
            }
        }
        catch
        {
            await DestroyAsync(client);
            throw;
        }
    }

    public async Task<Checkout> CheckoutAsync(FileServerConnection server, CancellationToken ct)
    {
        var (lease, slot) = await AcquireInternalAsync(server, ct);
        try
        {
            var (client, recycled) = await TakeAsync(slot, server, ct);
            return new Checkout(client, slot, lease) { Recycled = recycled };
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    public void Return(ServerPool slot, AsyncFtpClient client) => slot.Idle.Enqueue(client);

    public ValueTask DiscardAsync(AsyncFtpClient client) => DestroyAsync(client);

    internal static bool IsTransportFailure(Exception ex)
    {
        for (var e = (Exception?)ex; e is not null; e = e.InnerException)
        {
            if (e is SocketException or IOException or TimeoutException) return true;
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var slot in _perServer.Values)
        {
            while (slot.Idle.TryDequeue(out var client))
                await DestroyAsync(client);
            slot.Gate.Dispose();
        }

        _global.Dispose();
    }

    private async Task<(FtpLease Lease, ServerPool Slot)> AcquireInternalAsync(
        FileServerConnection server, CancellationToken ct)
    {
        var slot = _perServer.GetOrAdd(server.Host, _ => new ServerPool
        {
            Gate = new SemaphoreSlim(options.MaxConcurrentPerServer, options.MaxConcurrentPerServer),
            Idle = new ConcurrentQueue<AsyncFtpClient>(),
        });
        await _global.WaitAsync(ct);
        try
        {
            await slot.Gate.WaitAsync(ct);
        }
        catch
        {
            _global.Release();
            throw;
        }

        return (new FtpLease(_global, slot.Gate), slot);
    }

    private async Task<(AsyncFtpClient Client, bool Recycled)> TakeAsync(
        ServerPool slot, FileServerConnection server, CancellationToken ct)
    {
        if (slot.Idle.TryDequeue(out var client))
        {
            if (client.IsConnected) return (client, true);
            await DestroyAsync(client);
        }

        return (await ConnectNewAsync(server, ct), false);
    }

    private static async ValueTask DestroyAsync(AsyncFtpClient client)
    {
        try { await client.DisposeAsync(); } catch { /* best-effort teardown */ }
    }
}
