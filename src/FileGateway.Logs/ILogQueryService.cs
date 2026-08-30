using FileGateway.Core.Files;
using FileGateway.Core.Queries;
using FileGateway.Core.Tokens;

namespace FileGateway.Logs;

/// <summary>로그 조회 서비스 계약: 목록(페이지네이션), 단일 식별, fileId 재해석.</summary>
public interface ILogQueryService
{
    Task<PagedResult<LogFileDescriptor>> ListAsync(LogListQuery query, CancellationToken ct);

    /// <summary>ListAsync와 동일한 매치 집합을 물리 위치까지 포함해 한 번에 반환한다(파일별 재탐색 없음).</summary>
    Task<IReadOnlyList<LocatedLogFile>> ListLocatedAsync(LogListQuery query, CancellationToken ct);

    Task<SingleFileMatch> ResolveSingleAsync(LogListQuery query, CancellationToken ct);

    Task<LocatedFile> LocateByFileIdAsync(TokenPayload fileId, CancellationToken ct);
}
