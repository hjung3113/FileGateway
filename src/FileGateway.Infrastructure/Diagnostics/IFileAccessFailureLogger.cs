// src/FileGateway.Infrastructure/Diagnostics/IFileAccessFailureLogger.cs
namespace FileGateway.Infrastructure.Diagnostics;

/// <summary>결정적 파일명 추정(FileNameTemplate) 미스를 운영자 전용 진단 DB에 기록한다.
/// 클라이언트에 노출되지 않는 내부 진단 데이터다 — 물리 경로 비노출 가드레일은 클라이언트 대상이며
/// 이 테이블은 운영자만 조회하는 의도된 예외다(docs/09-security-and-operations.md).</summary>
public interface IFileAccessFailureLogger
{
    Task LogAsync(string equipmentId, string logType, string serverId,
        DateTimeOffset requestedSlot, string computedRelativePath, string failureReason, CancellationToken ct);
}
