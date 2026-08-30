using FileGateway.Core.Files;

namespace FileGateway.IntegrationTests.Api;

/// <summary>원격 호출 횟수를 세는 위임 IFileAccess. 다운로드가 목록 대비 추가 listing을 하지 않음을 증명한다.</summary>
public sealed class CountingFileAccess(IFileAccess inner) : IFileAccess
{
    private int _listings;
    public int Listings => Volatile.Read(ref _listings);

    public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
    {
        Interlocked.Increment(ref _listings);
        return inner.ListFilesAsync(server, dir, ct);
    }

    public Task<long> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
        => inner.StatFileAsync(server, path, ct);

    public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
        => inner.FileExistsAsync(server, path, ct);

    public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
        => inner.OpenReadAsync(server, path, ct);
}
