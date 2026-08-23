using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using FileGateway.Core.Tokens;
using Microsoft.AspNetCore.DataProtection;

namespace FileGateway.Infrastructure.Tokens;

public sealed class DataProtectionTokenCodec(IDataProtectionProvider provider) : ITokenCodec
{
    private const string ProtectorPurposePrefix = "filegateway.tokens.v1";

    private sealed record EncodedToken(
        string Purpose, IReadOnlyDictionary<string, string> Claims,
        DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

    public string Protect(TokenPayload payload)
    {
        var inner = new EncodedToken(
            payload.Purpose, payload.Claims, payload.IssuedAt, payload.IssuedAt.Add(payload.Ttl));
        var json = JsonSerializer.SerializeToUtf8Bytes(inner);
        var protectedBytes = provider.CreateProtector(ProtectorPurpose(payload.Purpose)).Protect(json);
        return Base64Url.EncodeToString(protectedBytes);
    }

    public TokenDecodeResult Unprotect(string token, string expectedPurpose)
    {
        if (string.IsNullOrEmpty(expectedPurpose)) throw new ArgumentException("expected purpose is required", nameof(expectedPurpose));
        try
        {
            byte[] bytes;
            try { bytes = Base64Url.DecodeFromChars(token.ToCharArray()); }
            catch (FormatException) { return Invalid(); }
            var json = provider.CreateProtector(ProtectorPurpose(expectedPurpose)).Unprotect(bytes);
            var inner = JsonSerializer.Deserialize<EncodedToken>(json);
            if (inner is null) return Invalid();
            if (!string.Equals(inner.Purpose, expectedPurpose, StringComparison.Ordinal)) return Invalid();
            if (inner.ExpiresAt <= DateTimeOffset.UtcNow) return new(TokenValidity.Expired, null);
            return new(TokenValidity.Valid,
                new TokenPayload(inner.Purpose, inner.Claims, inner.IssuedAt, inner.ExpiresAt - inner.IssuedAt));
        }
        catch (CryptographicException) { return Invalid(); }
        catch (JsonException) { return Invalid(); }
    }

    // 종류별 purpose(fg.fileid.log 등)를 protector purpose에 반영해 cross-kind 토큰이 복호화 자체에 실패한다.
    private static string ProtectorPurpose(string purpose) => $"{ProtectorPurposePrefix}:{purpose}";

    private static TokenDecodeResult Invalid() => new(TokenValidity.Invalid, null);
}
