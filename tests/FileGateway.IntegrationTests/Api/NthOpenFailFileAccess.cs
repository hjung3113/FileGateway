using FileGateway.Core.Files;

namespace FileGateway.IntegrationTests.Api;

/// <summary>N번째(1-based) OpenReadAsync 호출만 전송 실패를 흉내내는 위임 IFileAccess.
/// ThrowingFileAccess는 ListFilesAsync까지 실패시키므로 "스트리밍 도중 일부 파일 실패" 시나리오 전용.</summary>
public sealed class NthOpenFailFileAccess(IFileAccess inner, int failOnNthOpen) : IFileAccess
{
    private int _opens;

    public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
        => inner.ListFilesAsync(server, dir, ct);

    public Task<long> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
        => inner.StatFileAsync(server, path, ct);

    public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
        => inner.FileExistsAsync(server, path, ct);

    public async Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref _opens);
        if (n == failOnNthOpen)
            throw new FileAccessException(FileAccessError.ConnectionFailed, "forced failure on nth open");
        return await inner.OpenReadAsync(server, path, ct);
    }
}
