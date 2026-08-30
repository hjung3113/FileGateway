namespace FileGateway.Core.Files;

/// <summary>
/// <see cref="IFileAccess.ListDirectoriesAsync"/> 결과. <see cref="RemoteDirectoryListing"/> 계약을 미러한다.
/// Exists=false = 디렉터리 부재(정상 — 호출자는 해당 branch를 prune). 존재하지만 비어 있는 디렉터리는
/// Exists=true, Names=[] — no-match와 missing을 구분한다. Names는 '.'·'..'가 제거된 직계 자식 디렉터리 이름.
/// 전송/인증/프로토콜 장애는 빈 결과가 아니라 FileAccessException으로 throw된다.
/// </summary>
public sealed record RemoteDirectoryNames(bool Exists, IReadOnlyList<string> Names)
{
    public static RemoteDirectoryNames Missing { get; } = new(false, []);
}
