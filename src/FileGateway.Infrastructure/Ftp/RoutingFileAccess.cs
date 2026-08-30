using FileGateway.Core.Files;

namespace FileGateway.Infrastructure.Ftp;

/// <summary>Host=="localhost"면 로컬 파일시스템, 그 외엔 FTP/FTPS로 위임하는 IFileAccess composite.
/// 라우팅 조건은 이곳에만 존재하고 상위 계층(Logs/Configurations/Api)은 이 분기를 모른다.</summary>
public sealed class RoutingFileAccess(IFileAccess local, IFileAccess ftp) : IFileAccess
{
    public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
        => Select(server).ListFilesAsync(server, dir, ct);

    public Task<long> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
        => Select(server).StatFileAsync(server, path, ct);

    public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
        => Select(server).FileExistsAsync(server, path, ct);

    public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
        => Select(server).OpenReadAsync(server, path, ct);

    private IFileAccess Select(FileServerConnection server)
        => IsLocalHost(server.Host) ? local : ftp;

    private static bool IsLocalHost(string? host)
        => string.Equals(host?.Trim(), "localhost", StringComparison.OrdinalIgnoreCase);
}
