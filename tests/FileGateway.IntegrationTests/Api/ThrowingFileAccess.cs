using FileGateway.Core.Files;

namespace FileGateway.IntegrationTests.Api;

/// <summary>어떤 메서드든 호출되면 즉시 실패 — catalog 조회가 IFileAccess를 건드리지 않음을 구조적으로 증명.</summary>
public sealed class ThrowingFileAccess : IFileAccess
{
    public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string relativeDirectory, CancellationToken ct)
        => throw new InvalidOperationException("IFileAccess must not be called for catalog queries.");

    public Task<RemoteDirectoryNames> ListDirectoriesAsync(
        FileServerConnection server, string relativeDirectory, CancellationToken ct)
        => throw new InvalidOperationException("IFileAccess must not be called for catalog queries.");

    public Task<FileStat> StatFileAsync(FileServerConnection server, string relativePath, CancellationToken ct)
        => throw new InvalidOperationException("IFileAccess must not be called for catalog queries.");

    public Task<bool> FileExistsAsync(FileServerConnection server, string relativePath, CancellationToken ct)
        => throw new InvalidOperationException("IFileAccess must not be called for catalog queries.");

    public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string relativePath, CancellationToken ct)
        => throw new InvalidOperationException("IFileAccess must not be called for catalog queries.");
}
