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
    /// <summary>ActualName은 서버가 응답한 실제 파일명(원본 casing) — 응답 모델은 이 이름을 그대로 보존한다.</summary>
    Task<FileStat> StatFileAsync(FileServerConnection server, string relativePath, CancellationToken ct);

    /// <summary>Returns false when the file is absent; throws on transport errors.</summary>
    Task<bool> FileExistsAsync(FileServerConnection server, string relativePath, CancellationToken ct);

    Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string relativePath, CancellationToken ct);

    /// <summary>
    /// 직계 자식 디렉터리 이름 열거. <see cref="RemoteDirectoryNames.Exists"/>=false는 디렉터리 부재(정상),
    /// 존재하지만 비어 있으면 Exists=true, Names=[]. 전송/인증/프로토콜 장애는 FileAccessException으로 throw,
    /// 빈 결과와 엄격히 구분한다. '.'·'..'는 제거한 자식 이름만 반환한다.
    /// </summary>
    Task<RemoteDirectoryNames> ListDirectoriesAsync(FileServerConnection server, string relativeDirectory, CancellationToken ct);
}
