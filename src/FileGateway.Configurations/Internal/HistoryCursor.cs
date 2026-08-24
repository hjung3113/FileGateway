using System.Globalization;
using FileGateway.Configurations.Tokens;
using FileGateway.Core.Errors;
using FileGateway.Core.Tokens;

namespace FileGateway.Configurations.Internal;

/// <summary>
/// Configuration History 무상태 pagination 커서(LogCursor와 동일 구조). 토큰에 원본 조회조건
/// (canonical 바인딩)과 마지막 위치(snapshotTimestamp + fileName)를 넣어 서버 세션 없이 다음
/// 페이지를 재해석한다. limit은 바인딩에서 제외(페이지 크기 변경 허용).
/// </summary>
public static class HistoryCursor
{
    public static string Canonical(ConfigurationHistoryQuery q)
        => string.Join("|",
            Esc(q.EquipmentId), Esc(q.ConfigurationType),
            q.From.ToString("O", CultureInfo.InvariantCulture),
            q.To.ToString("O", CultureInfo.InvariantCulture));

    private static string Esc(string s)
        => s.Replace("\\", "\\\\").Replace("|", "\\|");

    public static string Encode(ITokenCodec codec, TimeProvider clock, ConfigurationHistoryQuery q,
        DateTimeOffset lastTimestamp, string lastFileName, TimeSpan ttl)
    {
        var claims = new Dictionary<string, string>
        {
            ["bind"] = Canonical(q),
            ["lastTs"] = lastTimestamp.ToString("O", CultureInfo.InvariantCulture),
            ["lastName"] = lastFileName,
        };
        return codec.Protect(new TokenPayload(ConfigurationTokenKinds.ContinuationPurpose, claims,
            clock.GetUtcNow(), ttl));
    }

    public static (DateTimeOffset LastTs, string? LastName) Decode(ITokenCodec codec, string token)
    {
        var result = codec.Unprotect(token, ConfigurationTokenKinds.ContinuationPurpose);
        if (result.Validity != TokenValidity.Valid)
            throw new FileGatewayException("InvalidRequest", "invalid continuation token");
        var p = result.Payload!;
        if (p.Purpose != ConfigurationTokenKinds.ContinuationPurpose)
            throw new FileGatewayException("InvalidRequest", "invalid continuation token");
        var ts = DateTimeOffset.Parse(p.Claims["lastTs"], CultureInfo.InvariantCulture);
        return (ts, p.Claims.TryGetValue("lastName", out var n) && n.Length > 0 ? n : null);
    }

    public static void AssertBinding(ITokenCodec codec, string token, ConfigurationHistoryQuery current)
    {
        var result = codec.Unprotect(token, ConfigurationTokenKinds.ContinuationPurpose);
        if (result.Validity != TokenValidity.Valid ||
            result.Payload!.Purpose != ConfigurationTokenKinds.ContinuationPurpose ||
            result.Payload.Claims.GetValueOrDefault("bind") != Canonical(current))
            throw new FileGatewayException("InvalidRequest", "continuation token does not match query conditions");
    }
}
