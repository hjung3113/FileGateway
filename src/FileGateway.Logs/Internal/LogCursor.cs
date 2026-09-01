using System.Globalization;
using FileGateway.Core.Errors;
using FileGateway.Core.Time;
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
    {
        // subtype ""→null 정규화(LogQueryService 진입부와 같은 규칙): 빈 subtype는 미지정과
        // 동일 바인딩이어야 한다. 바인딩과 필터가 같은 정규화된 query를 보게 된다.
        var subtype = q.Subtype is { Length: > 0 } ? q.Subtype : null;
        return string.Join("|",
            Esc(q.EquipmentId), Esc(q.LogType),
            q.From?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            q.To?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            Esc(subtype ?? ""),
            string.Join("&", q.Attributes.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{Esc(kv.Key)}={Esc(kv.Value)}")));
    }

    private static string Esc(string s)
        => s.Replace("\\", "\\\\").Replace("|", "\\|").Replace("&", "\\&").Replace("=", "\\=");

    public static string Encode(ITokenCodec codec, TimeProvider clock, LogListQuery q,
        DateTimeOffset? lastTimestamp, string? lastFileName, EffectiveRange effectiveRange, TimeSpan ttl)
    {
        var claims = new Dictionary<string, string>
        {
            ["bind"] = Canonical(q),
            ["lastTs"] = lastTimestamp?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            ["lastName"] = lastFileName ?? "",
            // 첫 페이지의 effective range를 고정: from/to==null 기본 2일이 이후 페이지에서
            // 진행된 시계로 재계산되어 하한이 파일을 지나쳐 사라지는 일을 막는다.
            ["efFrom"] = effectiveRange.From.ToString("O", CultureInfo.InvariantCulture),
            ["efTo"] = effectiveRange.To.ToString("O", CultureInfo.InvariantCulture),
        };
        return codec.Protect(new TokenPayload(LogTokenKinds.ContinuationPurpose, claims,
            clock.GetUtcNow(), ttl));
    }

    public static (DateTimeOffset? LastTs, string? LastName, EffectiveRange EffectiveRange) Decode(
        ITokenCodec codec, string token)
    {
        var result = codec.Unprotect(token, LogTokenKinds.ContinuationPurpose);
        if (result.Validity != TokenValidity.Valid)
            throw new FileGatewayException("InvalidRequest", "invalid continuation token");
        var p = result.Payload!;
        if (p.Purpose != LogTokenKinds.ContinuationPurpose)
            throw new FileGatewayException("InvalidRequest", "invalid continuation token");
        var ts = p.Claims.TryGetValue("lastTs", out var t) && t.Length > 0
            ? DateTimeOffset.Parse(t, CultureInfo.InvariantCulture) : (DateTimeOffset?)null;
        var range = new EffectiveRange(
            DateTimeOffset.Parse(p.Claims["efFrom"], CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(p.Claims["efTo"], CultureInfo.InvariantCulture));
        return (ts, p.Claims.TryGetValue("lastName", out var n) && n.Length > 0 ? n : null, range);
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
