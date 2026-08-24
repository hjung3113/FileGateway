using FileGateway.Core.Files;

namespace FileGateway.Core.Queries;

/// <summary>페이지 결과. ContinuationToken은 원본 조회조건에 바인딩된 불투명 커서다(같은 조건에서만 사용 가능).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? ContinuationToken);

public enum MatchCount { Zero, One, Many }

/// <summary>단일 논리 파일 식별 결과. File/FileId는 Count가 One일 때만 채워진다(FileId는 ToDescriptor와 동일 규칙으로 발급한 보호 토큰).</summary>
public sealed record SingleFileMatch(LocatedFile? File, MatchCount Count, string? FileId = null);
