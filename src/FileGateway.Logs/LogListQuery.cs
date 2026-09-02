using FileGateway.Core.Errors;
using FileGateway.Core.Time;
using FileGateway.Logs.Definitions;

namespace FileGateway.Logs;

/// <summary>로그 목록 조회 조건. From/To는 절대 시각(offset 포함)이며 없으면 기본 range가 적용된다.</summary>
public sealed record LogListQuery(
    string EquipmentId,
    string LogType,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Subtype,
    IReadOnlyDictionary<string, string> Attributes,
    int? Limit,
    string? ContinuationToken);

/// <summary>조회 조건을 물리 탐색 range로 정규화한다. 규칙 위반은 <see cref="FileGatewayException"/>("InvalidRequest").</summary>
public static class EffectiveRangePlanner
{
    public static EffectiveRange Normalize(LogListQuery q, GenerationType type, TimeSpan maxRange, TimeProvider clock)
    {
        if (type == GenerationType.Continuous)
        {
            if (q.From is not null || q.To is not null)
                throw new FileGatewayException("InvalidRequest", "Continuous log does not accept from/to");
            return new(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        }
        if (q.To is not null && q.From is null)
            throw new FileGatewayException("InvalidRequest", "to without from is not supported");
        if (q.To is not null && q.From >= q.To)
            throw new FileGatewayException("InvalidRequest", "from must be before to");

        var now = clock.GetUtcNow(); // 시계는 1회만 읽는다(두 번 읽으면 기본 2일이 정확히 떨어지지 않는다)
        var from = q.From ?? now.AddDays(-2);
        var to = q.To ?? (q.From is not null ? q.From.Value.AddDays(2) : now);
        if (to - from > maxRange)
            throw new FileGatewayException("InvalidRequest", $"query range exceeds limit ({maxRange})");
        return new(from, to);
    }
}
