using System.Collections.Concurrent;
using FileGateway.Core.Files;

namespace FileGateway.Infrastructure.Ftp;

/// <summary>전체/서버별 FTP 동시성 permit. 단기 명령은 RunAsync, 스트리밍은 lease를 스트림에 소유시킨다.</summary>
public sealed class FtpConcurrencyLimiter(FtpOptions options)
{
    private readonly SemaphoreSlim _global = new(options.MaxConcurrentGlobal, options.MaxConcurrentGlobal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perServer = new(StringComparer.OrdinalIgnoreCase);

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
        var perServer = _perServer.GetOrAdd(server.Host,
            _ => new SemaphoreSlim(options.MaxConcurrentPerServer, options.MaxConcurrentPerServer));
        await _global.WaitAsync(ct);
        try { await perServer.WaitAsync(ct); }
        catch { _global.Release(); throw; }
        return new FtpLease(_global, perServer);
    }

    public async Task<T> RunAsync<T>(FileServerConnection server, Func<CancellationToken, Task<T>> op, CancellationToken ct)
    {
        await using var lease = await AcquireAsync(server, ct);
        return await op(ct);
    }
}
