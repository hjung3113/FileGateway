using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using FileGateway.Core.Tokens;
using Microsoft.AspNetCore.DataProtection;

namespace FileGateway.Infrastructure.Tokens;

public sealed class DataProtectionTokenCodec(IDataProtectionProvider provider) : ITokenCodec
{
    private const string ProtectorPurpose = "filegateway.tokens.v1";

    private sealed record EncodedToken(
        string Purpose, IReadOnlyDictionary<string, string> Claims,
        DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

    public string Protect(TokenPayload payload)
    {
        var inner = new EncodedToken(
            payload.Purpose, payload.Claims, payload.IssuedAt, payload.IssuedAt.Add(payload.Ttl));
        var json = JsonSerializer.SerializeToUtf8Bytes(inner);
        var protectedBytes = provider.CreateProtector(ProtectorPurpose).Protect(json);
        return Base64Url.EncodeToString(protectedBytes);
    }

    public TokenDecodeResult Unprotect(string token)
    {
        try
        {
            byte[] bytes;
            try { bytes = Base64Url.DecodeFromChars(token.ToCharArray()); }
            catch (FormatException) { return Invalid(); }
            var json = provider.CreateProtector(ProtectorPurpose).Unprotect(bytes);
            var inner = JsonSerializer.Deserialize<EncodedToken>(json);
            if (inner is null) return Invalid();
            if (inner.ExpiresAt <= DateTimeOffset.UtcNow) return new(TokenValidity.Expired, null);
            return new(TokenValidity.Valid,
                new TokenPayload(inner.Purpose, inner.Claims, inner.IssuedAt, inner.ExpiresAt - inner.IssuedAt));
        }
        catch (CryptographicException) { return Invalid(); }
        catch (JsonException) { return Invalid(); }
    }

    private static TokenDecodeResult Invalid() => new(TokenValidity.Invalid, null);
}
