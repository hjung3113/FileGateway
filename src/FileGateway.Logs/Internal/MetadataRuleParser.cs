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
    {
        try
        {
            return rule.Mode == MetadataMode.Template
                ? ParseTemplate(rule.Pattern, generation, relativePath)
                : ParseRegex(rule, generation, relativePath);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static ParsedMetadata? ParseTemplate(string pattern, GenerationType generation, string path)
    {
        var regex = TemplateToRegex(pattern);
        var m = regex.Regex.Match(path);
        if (!m.Success) return null;

        var attrs = new Dictionary<string, string>();
        string? subtype = null;
        DateTimeOffset? date = null, hour = null, minute = null;

        foreach (var name in regex.Regex.GetGroupNames().Where(g => regex.Regex.GroupNumberFromName(g) >= 0 && !int.TryParse(g, out _)))
        {
            var v = m.Groups[name].Value;
            if (v.Length == 0) return null;
            switch (name)
            {
                case "fg_ts_yyyy": date = DateTimeOffset.TryParseExact(
                        $"{v}-{G(m, "fg_ts_MM")}-{G(m, "fg_ts_dd")}", "yyyy-M-d",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                        ? SiteLocalMidnight(d.Date) : null; break;
                case "fg_ts_MM": case "fg_ts_dd": break;
                case "fg_ts_HH": hour = DateTimeOffset.TryParseExact(v, "HH", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var h) ? h : null; break;
                case "fg_ts_mm": minute = DateTimeOffset.TryParseExact(v, "mm", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var min) ? min : null; break;
                case "fg_subtype": subtype = v; break;
                default: break;
            }
        }

        foreach (var (group, key) in regex.AttributeGroups)
            attrs[key] = m.Groups[group].Value;

        if (date is not null)
        {
            var midnight = SiteLocalMidnight(date.Value.Date);
            if (generation == GenerationType.Daily) return new(midnight, subtype, attrs);
            if (generation == GenerationType.Hourly)
            {
                if (hour is null) return null;
                return new(midnight.AddHours(hour.Value.Hour).AddMinutes(minute?.Minute ?? 0), subtype, attrs);
            }
            // Continuous: 추출된 시각이 있으면 사용(없어도 null 허용은 아래에서 처리)
            return new(midnight.AddHours(hour?.Hour ?? 0).AddMinutes(minute?.Minute ?? 0), subtype, attrs);
        }
        if (generation is GenerationType.Hourly or GenerationType.Daily) return null; // 날짜 토큰 미추출
        return new ParsedMetadata(null, subtype, attrs); // Continuous, timestamp 없음
    }

    private static string G(Match m, string name) => m.Groups[name].Value;

    [GeneratedRegex(@"\{(?<tok>yyyy|MM|dd|HH|mm|subtype|attribute\.[^}]+)\}", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    private static (Regex Regex, IReadOnlyList<(string Group, string Key)> AttributeGroups) TemplateToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        var last = 0;
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        var attributeGroups = new List<(string Group, string Key)>();
        foreach (Match tm in TokenRegex().Matches(pattern))
        {
            sb.Append(Regex.Escape(pattern[last..tm.Index]));
            var tok = tm.Groups["tok"].Value;
            if (!seenTokens.Add(tok))
                throw new ArgumentException($"duplicate metadata token: {tok}");
            sb.Append(tok switch
            {
                "yyyy" => "(?<fg_ts_yyyy>\\d{4})",
                "MM" => "(?<fg_ts_MM>\\d{2})",
                "dd" => "(?<fg_ts_dd>\\d{2})",
                "HH" => "(?<fg_ts_HH>\\d{2})",
                "mm" => "(?<fg_ts_mm>\\d{2})",
                "subtype" => "(?<fg_subtype>[^/]+?)",
                var a when a.StartsWith("attribute.", StringComparison.Ordinal)
                    => AttributeGroup(a["attribute.".Length..], attributeGroups),
                _ => throw new ArgumentException($"unknown token {tok}")
            });
            last = tm.Index + tm.Length;
        }
        sb.Append(Regex.Escape(pattern[last..]));
        sb.Append('$');
        return (new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.ExplicitCapture), attributeGroups);
    }

    private static string AttributeGroup(string key, List<(string Group, string Key)> attributeGroups)
    {
        var group = $"fg_attr_{attributeGroups.Count}";
        attributeGroups.Add((group, key));
        return $"(?<{group}>[^/]+?)";
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
                timestamp = ParseTimestamp(value, map.Format!, generation);
                if (timestamp is null) return null;
            }
            else if (map.Target == "subtype") subtype = value;
            else if (map.Target.StartsWith("attribute.", StringComparison.Ordinal))
                attrs[map.Target["attribute.".Length..]] = value;
            else return null;
        }
        if (generation is GenerationType.Hourly or GenerationType.Daily && timestamp is null) return null;
        return new(timestamp, subtype, attrs);
    }

    private static DateTimeOffset SiteLocalMidnight(DateTime date)
    {
        var localDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        return new DateTimeOffset(localDate, SiteTime.Local.GetUtcOffset(localDate));
    }

    private static DateTimeOffset? ParseTimestamp(string value, string format, GenerationType generation)
    {
        if (!DateTimeOffset.TryParseExact(value, format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)) return null;

        if (!HasOffsetSpecifier(format))
        {
            var localDateTime = DateTime.SpecifyKind(parsed.DateTime, DateTimeKind.Unspecified);
            parsed = new DateTimeOffset(localDateTime, SiteTime.Local.GetUtcOffset(localDateTime));
        }

        return generation == GenerationType.Daily ? SiteTime.SiteLocalMidnight(parsed) : parsed;
    }

    private static bool HasOffsetSpecifier(string format)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '\\')
            {
                i++;
                continue;
            }
            if (c == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && i + 1 < format.Length && format[i + 1] == '\'')
                {
                    i++;
                    continue;
                }
                inSingleQuote = !inSingleQuote;
                continue;
            }
            if (c == '"' && !inSingleQuote)
            {
                if (inDoubleQuote && i + 1 < format.Length && format[i + 1] == '"')
                {
                    i++;
                    continue;
                }
                inDoubleQuote = !inDoubleQuote;
                continue;
            }
            if (!inSingleQuote && !inDoubleQuote && c is 'K' or 'z' or 'Z')
                return true;
        }
        return false;
    }
}
