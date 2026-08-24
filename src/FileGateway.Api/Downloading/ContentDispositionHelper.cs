using System.Globalization;
using System.Text;

namespace FileGateway.Api.Downloading;

/// <summary>Content-Disposition 헤더에 안전하게 넣을 수 있는 attachment 값 생성(CR/LF·비ASCII 제거 ASCII fallback + RFC 5987 filename*).</summary>
public static class ContentDispositionHelper
{
    public static string Attachment(string fileName)
    {
        var fallback = new string(fileName.Where(c => c > 0x20 && c < 0x7f).ToArray());
        return $"attachment; filename=\"{fallback.Replace("\"", string.Empty, StringComparison.Ordinal)}\"; filename*=UTF-8''{Encode(fileName)}";
    }

    private static string Encode(string value)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            // RFC 5987 attr-char: ALPHA / DIGIT / !#$&+-.^_`|~
            if (b is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9'
                or (byte)'!' or (byte)'#' or (byte)'$' or (byte)'&' or (byte)'+' or (byte)'-' or (byte)'.'
                or (byte)'^' or (byte)'_' or (byte)'`' or (byte)'|' or (byte)'~')
                sb.Append((char)b);
            else
                sb.Append(CultureInfo.InvariantCulture, $"%{b:X2}");
        }
        return sb.ToString();
    }
}
