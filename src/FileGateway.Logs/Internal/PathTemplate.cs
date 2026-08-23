using System.Globalization;
using System.Text.RegularExpressions;

namespace FileGateway.Logs.Internal;

internal static class PathTemplate
{
    private static readonly string[] AllowedTokens = ["{yyyy}", "{MM}", "{dd}", "{HH}"];

    public static void ValidateTokens(string template)
    {
        foreach (var m in Regex.Matches(template, @"\{[^}]+\}").Select(m => m.Value))
            if (!AllowedTokens.Contains(m))
                throw new ArgumentException($"unsupported path token: {m}");
    }

    public static string Expand(string template, DateTimeOffset siteLocalSlot)
        => template.Replace("{yyyy}", siteLocalSlot.ToString("yyyy", CultureInfo.InvariantCulture))
                   .Replace("{MM}", siteLocalSlot.ToString("MM", CultureInfo.InvariantCulture))
                   .Replace("{dd}", siteLocalSlot.ToString("dd", CultureInfo.InvariantCulture))
                   .Replace("{HH}", siteLocalSlot.ToString("HH", CultureInfo.InvariantCulture));
}
