// src/FileGateway.Infrastructure/Diagnostics/SpFileAccessFailureLogger.cs
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace FileGateway.Infrastructure.Diagnostics;

/// <summary>SP dbo.FileGateway_LogFileAccessFailure 호출로 실패 진단 로그를 남긴다.
/// 이 기능은 진단 편의를 위한 부가 기능이지 핵심 요청 경로가 아니다 — connection string 미설정이나
/// DB 장애로도 원래 요청의 응답을 막지 않도록 모든 예외를 내부에서 삼키고 경고만 남긴다.</summary>
public sealed class SpFileAccessFailureLogger(
    string? connectionString, ILogger<SpFileAccessFailureLogger> logger,
    string spName = "dbo.FileGateway_LogFileAccessFailure") : IFileAccessFailureLogger
{
    public async Task LogAsync(string equipmentId, string logType, string serverId,
        DateTimeOffset requestedSlot, string computedRelativePath, string failureReason, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            logger.LogWarning("ReferenceData connection string not configured; skipping file access failure log for {EquipmentId}/{LogType}",
                equipmentId, logType);
            return;
        }
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = spName;
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@EquipmentId", equipmentId);
            cmd.Parameters.AddWithValue("@LogType", logType);
            cmd.Parameters.AddWithValue("@ServerId", serverId);
            cmd.Parameters.AddWithValue("@RequestedSlotUtc", requestedSlot.UtcDateTime);
            cmd.Parameters.AddWithValue("@ComputedRelativePath", computedRelativePath);
            cmd.Parameters.AddWithValue("@FailureReason", failureReason);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 진단 로그 실패는 원래 요청 흐름을 막지 않는다 — 경고만 남긴다.
            logger.LogWarning(ex, "failed to record file access failure log for {EquipmentId}/{LogType}",
                equipmentId, logType);
        }
    }
}
