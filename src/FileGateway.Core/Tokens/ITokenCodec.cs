namespace FileGateway.Core.Tokens;

public sealed record TokenPayload(
    string Purpose,
    IReadOnlyDictionary<string, string> Claims,
    DateTimeOffset IssuedAt,
    TimeSpan Ttl);

public enum TokenValidity { Valid, Invalid, Expired }

public sealed record TokenDecodeResult(TokenValidity Validity, TokenPayload? Payload);

public interface ITokenCodec
{
    string Protect(TokenPayload payload);

    /// <summary>expectedPurpose는 종류별 purpose 상수(fg.fileid.log 등). 불일치 토큰은 Invalid로 거부된다.</summary>
    TokenDecodeResult Unprotect(string token, string expectedPurpose);
}
