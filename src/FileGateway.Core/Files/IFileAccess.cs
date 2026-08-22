namespace FileGateway.Core.Files;

/// <summary>
/// Protocol-agnostic remote file access. Absent files: <see cref="StatFileAsync"/> throws
/// <see cref="FileAccessException"/> with <see cref="FileAccessError.FileNotFound"/>;
/// <see cref="FileExistsAsync"/> returns false. Transport errors always throw.
/// </summary>
public interface IFileAccess
{
    Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string relativeDirectory, CancellationToken ct);

    /// <exception cref="FileAccessException">FileNotFound when the file is absent.</exception>
    Task<long> StatFileAsync(FileServerConnection server, string relativePath, CancellationToken ct);

    /// <summary>Returns false when the file is absent; throws on transport errors.</summary>
    Task<bool> FileExistsAsync(FileServerConnection server, string relativePath, CancellationToken ct);

    Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string relativePath, CancellationToken ct);
}
