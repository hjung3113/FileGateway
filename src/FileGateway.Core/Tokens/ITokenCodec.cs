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
    TokenDecodeResult Unprotect(string token);
}
