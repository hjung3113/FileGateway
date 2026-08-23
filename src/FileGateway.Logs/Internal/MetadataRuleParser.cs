using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FileGateway.Core.Time;
using FileGateway.Logs.Definitions;

namespace FileGateway.Logs.Internal;

public sealed record ParsedMetadata(
    DateTimeOffset? Timestamp, string? Subtype, IReadOnlyDictionary<string, string> Attributes)
{
    public static readonly ParsedMetadata Empty = new(null, null,
        (IReadOnlyDictionary<string, string>)new Dictionary<string, string>());
}

public static partial class MetadataRuleParser
{
    public static ParsedMetadata? Parse(LogMetadataRule rule, GenerationType generation, string relativePath)
        => rule.Mode == MetadataMode.Template ? ParseTemplate(rule.Pattern, generation, relativePath)
                                              : ParseRegex(rule, generation, relativePath);

    private static ParsedMetadata? ParseTemplate(string pattern, GenerationType generation, string path)
    {
        var regex = TemplateToRegex(pattern);
        var m = regex.Match(path);
        if (!m.Success) return null;

        var attrs = new Dictionary<string, string>();
        string? subtype = null;
        DateTimeOffset? date = null, hour = null;

        foreach (var name in regex.GetGroupNames().Where(g => regex.GroupNumberFromName(g) >= 0 && !int.TryParse(g, out _)))
        {
            var v = m.Groups[name].Value;
            if (v.Length == 0) return null;
            switch (name)
            {
                case "fg_ts_yyyy": date = DateTimeOffset.TryParseExact(
                        $"{v}-{G(m, "fg_ts_MM")}-{G(m, "fg_ts_dd")}", "yyyy-M-d",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                        ? new DateTimeOffset(d.Date, TimeSpan.FromHours(9)) : null; break;
                case "fg_ts_MM": case "fg_ts_dd": break;
                case "fg_ts_HH": hour = DateTimeOffset.TryParseExact(v, "HH", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var h) ? h : null; break;
                case "fg_subtype": subtype = v; break;
                default:
                    if (name.StartsWith("fg_attr_", StringComparison.Ordinal))
                        attrs[name["fg_attr_".Length..]] = v;
                    break;
            }
        }

        if (date is not null)
        {
            var localDate = TimeZoneInfo.ConvertTime(date.Value, SiteTime.Local);
            var midnight = new DateTimeOffset(localDate.Date, TimeSpan.FromHours(9));
            if (generation == GenerationType.Daily) return new(midnight, subtype, attrs);
            if (generation == GenerationType.Hourly)
            {
                if (hour is null) return null;
                return new(midnight.AddHours(hour.Value.Hour), subtype, attrs);
            }
            // Continuous: 추출된 시각이 있으면 사용(없어도 null 허용은 아래에서 처리)
            return new(midnight.AddHours(hour?.Hour ?? 0), subtype, attrs);
        }
        if (generation is GenerationType.Hourly or GenerationType.Daily) return null; // 날짜 토큰 미추출
        return new ParsedMetadata(null, subtype, attrs); // Continuous, timestamp 없음
    }

    private static string G(Match m, string name) => m.Groups[name].Value;

    [GeneratedRegex(@"\{(?<tok>yyyy|MM|dd|HH|mm|subtype|attribute\.[^}]+)\}", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    private static Regex TemplateToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        var last = 0;
        foreach (Match tm in TokenRegex().Matches(pattern))
        {
            sb.Append(Regex.Escape(pattern[last..tm.Index]));
            var tok = tm.Groups["tok"].Value;
            sb.Append(tok switch
            {
                "yyyy" => "(?<fg_ts_yyyy>\\d{4})",
                "MM" => "(?<fg_ts_MM>\\d{2})",
                "dd" => "(?<fg_ts_dd>\\d{2})",
                "HH" => "(?<fg_ts_HH>\\d{2})",
                "mm" => "(?<fg_ts_mm>\\d{2})",
                "subtype" => "(?<fg_subtype>[^/]+?)",
                var a when a.StartsWith("attribute.", StringComparison.Ordinal)
                    => $"(?<fg_attr_{a["attribute.".Length..]}>[^/]+?)",
                _ => throw new ArgumentException($"unknown token {tok}")
            });
            last = tm.Index + tm.Length;
        }
        sb.Append(Regex.Escape(pattern[last..]));
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.ExplicitCapture);
    }

    private static ParsedMetadata? ParseRegex(LogMetadataRule rule, GenerationType generation, string path)
    {
        var regex = new Regex(rule.Pattern, RegexOptions.Compiled | RegexOptions.ExplicitCapture);
        var m = regex.Match(path);
        if (!m.Success) return null;

        DateTimeOffset? timestamp = null; string? subtype = null;
        var attrs = new Dictionary<string, string>();
        foreach (var map in rule.Mappings)
        {
            if (!m.Groups[map.Group].Success) return null;
            var value = m.Groups[map.Group].Value;
            if (map.Target == "timestamp")
            {
                if (!DateTimeOffset.TryParseExact(value, map.Format!, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt)) return null;
                var unspecified = new DateTimeOffset(dt.DateTime, TimeSpan.FromHours(9)); // Site local 해석
                timestamp = generation == GenerationType.Daily
                    ? new DateTimeOffset(unspecified.Date, TimeSpan.FromHours(9))
                    : unspecified;
            }
            else if (map.Target == "subtype") subtype = value;
            else if (map.Target.StartsWith("attribute.", StringComparison.Ordinal))
                attrs[map.Target["attribute.".Length..]] = value;
            else return null;
        }
        if (generation is GenerationType.Hourly or GenerationType.Daily && timestamp is null) return null;
        return new(timestamp, subtype, attrs);
    }
}
