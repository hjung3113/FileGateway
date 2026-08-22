using System.Buffers.Text;
using System.Security.Cryptography;
using FileGateway.Core.Tokens;
using FileGateway.Infrastructure.Tokens;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FileGateway.UnitTests.Tokens;

public class TokenCodecTests
{
    private static ITokenCodec CreateCodec(string? keyDir = null)
    {
        var services = new ServiceCollection();
        var b = services.AddDataProtection();
        if (keyDir != null) b.PersistKeysToFileSystem(new DirectoryInfo(keyDir));
        return new DataProtectionTokenCodec(services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>());
    }

    private static TokenPayload Sample(DateTimeOffset? issued = null) => new(
        "fg.fileid.log",
        new Dictionary<string, string> { ["equipmentId"] = "EQ-001", ["fileName"] = "Event_A.zip" },
        issued ?? DateTimeOffset.UtcNow,
        TimeSpan.FromHours(24));

    [Fact]
    public void Round_trips_claims()
    {
        var codec = CreateCodec();
        var result = codec.Unprotect(codec.Protect(Sample()));
        Assert.Equal(TokenValidity.Valid, result.Validity);
        Assert.Equal("EQ-001", result.Payload!.Claims["equipmentId"]);
        Assert.Equal("fg.fileid.log", result.Payload.Purpose);
    }

    [Fact]
    public void Token_does_not_expose_payload_plaintext()
    {
        var codec = CreateCodec();
        var token = codec.Protect(Sample());
        Assert.DoesNotContain("EQ-001", token, StringComparison.Ordinal);
        Assert.DoesNotContain("Event_A.zip", token, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("AAAA-zzz")]
    public void Tampered_or_malformed_token_is_invalid(string token)
        => Assert.Equal(TokenValidity.Invalid, CreateCodec().Unprotect(token).Validity);

    [Fact]
    public void Modified_ciphertext_is_invalid()
    {
        var codec = CreateCodec();
        var token = codec.Protect(Sample());
        var bytes = Base64Url.DecodeFromChars(token.ToCharArray());
        bytes[10] ^= 0xFF;
        var tampered = Base64Url.EncodeToString(bytes);
        Assert.Equal(TokenValidity.Invalid, codec.Unprotect(tampered).Validity);
    }

    [Fact]
    public void Expired_token_reports_expired_not_invalid()
    {
        var codec = CreateCodec();
        var token = codec.Protect(Sample(DateTimeOffset.UtcNow.AddHours(-25)));
        Assert.Equal(TokenValidity.Expired, codec.Unprotect(token).Validity);
    }

    [Fact]
    public void New_codec_instance_with_same_key_directory_validates_prior_tokens()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fg-keys-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var token = CreateCodec(dir).Protect(Sample());
        // "재시작/rotation 후 동일 key ring" 시뮬레이션: 새 provider 인스턴스
        Assert.Equal(TokenValidity.Valid, CreateCodec(dir).Unprotect(token).Validity);
    }
}
