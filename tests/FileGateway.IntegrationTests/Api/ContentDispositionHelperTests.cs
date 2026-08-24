using FileGateway.Api.Downloading;

namespace FileGateway.IntegrationTests.Api;

public class ContentDispositionHelperTests
{
    [Theory]
    [InlineData("report.zip", "report.zip")]
    [InlineData("a\"b\\c.zip", "abc.zip")] // ASCII fallback은 "와 \를 모두 제거 — quoted-string 이탈 방지
    [InlineData("한글\".zip", ".zip")]     // 비ASCII 제거 후에도 " 제거 유지
    public void Attachment_ascii_fallback_strips_quote_and_backslash(string fileName, string expectedFallback)
    {
        var value = ContentDispositionHelper.Attachment(fileName);
        var fallback = System.Text.RegularExpressions.Regex.Match(value, @"filename=""([^""]*)""").Groups[1].Value;
        Assert.Equal(expectedFallback, fallback);
        Assert.DoesNotContain('\\', fallback);
        Assert.Matches(@"filename\*=UTF-8''", value); // 원본은 RFC 5987로 전달
    }
}
