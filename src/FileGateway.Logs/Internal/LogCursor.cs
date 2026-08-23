using System.Globalization;
using FileGateway.Core.Errors;
using FileGateway.Core.Tokens;
using FileGateway.Logs.Tokens;

namespace FileGateway.Logs.Internal;

/// <summary>
/// 무상태 pagination 커서. 토큰에 원본 조회조건(canonical 바인딩)과 마지막 위치를 함께 넣어
/// 서버 세션 없이 다음 페이지를 재해석한다. limit은 바인딩에서 제외(페이지 크기 변경 허용).
/// </summary>
public static class LogCursor
{
    public static string Canonical(LogListQuery q)
        => string.Join("|",
            q.EquipmentId, q.LogType,
            q.From?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            q.To?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            q.Subtype ?? "",
            string.Join("&", q.Attributes.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}")));

    public static string Encode(ITokenCodec codec, LogListQuery q,
        DateTimeOffset? lastTimestamp, string? lastFileName, TimeSpan ttl)
    {
        var claims = new Dictionary<string, string>
        {
            ["bind"] = Canonical(q),
            ["lastTs"] = lastTimestamp?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            ["lastName"] = lastFileName ?? "",
        };
        return codec.Protect(new TokenPayload(LogTokenKinds.ContinuationPurpose, claims,
            DateTimeOffset.UtcNow, ttl));
    }

    public static (DateTimeOffset? LastTs, string? LastName) Decode(ITokenCodec codec, string token)
    {
        var result = codec.Unprotect(token, LogTokenKinds.ContinuationPurpose);
        if (result.Validity != TokenValidity.Valid)
            throw new FileGatewayException("InvalidRequest", "invalid continuation token");
        var p = result.Payload!;
        if (p.Purpose != LogTokenKinds.ContinuationPurpose)
            throw new FileGatewayException("InvalidRequest", "invalid continuation token");
        var ts = p.Claims.TryGetValue("lastTs", out var t) && t.Length > 0
            ? DateTimeOffset.Parse(t, CultureInfo.InvariantCulture) : (DateTimeOffset?)null;
        return (ts, p.Claims.TryGetValue("lastName", out var n) && n.Length > 0 ? n : null);
    }

    public static void AssertBinding(ITokenCodec codec, string token, LogListQuery current)
    {
        var result = codec.Unprotect(token, LogTokenKinds.ContinuationPurpose);
        if (result.Validity != TokenValidity.Valid ||
            result.Payload!.Purpose != LogTokenKinds.ContinuationPurpose ||
            result.Payload.Claims.GetValueOrDefault("bind") != Canonical(current))
            throw new FileGatewayException("InvalidRequest", "continuation token does not match query conditions");
    }
}
