// src/FileGateway.Configurations/Internal/ConfigurationRuleParser.cs
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FileGateway.Configurations.Definitions;
using FileGateway.Core.Files;
using FileGateway.Core.Time;

namespace FileGateway.Configurations.Internal;

/// <summary>파일명 매처 — Literal(ci 전체 동등) / Glob(기존 GlobPattern) / Regex(DefinitionRegex, ci).
/// 파싱 시점 1회 생성해 정의 단위로 재사용한다(설계 §2, §5.1).</summary>
public abstract class FileMatcher
{
    public abstract bool Matches(string fileName);

    public static FileMatcher Create(FileMatchMode mode, string pattern) => mode switch
    {
        FileMatchMode.Literal => new LiteralMatcher(pattern),
        FileMatchMode.Glob => new GlobMatcher(pattern),
        FileMatchMode.Regex => new RegexMatcher(pattern),
        _ => throw new ArgumentException($"unknown file match mode: {mode}"),
    };

    private sealed class LiteralMatcher(string pattern) : FileMatcher
    {
        public override bool Matches(string fileName) => FileNameComparison.Same(pattern, fileName);
    }

    private sealed class GlobMatcher(string pattern) : FileMatcher
    {
        private readonly GlobPattern _glob = new(pattern);
        public override bool Matches(string fileName) => _glob.Matches(fileName);
    }

    private sealed class RegexMatcher(string pattern) : FileMatcher
    {
        // 전체 일치는 DefinitionRegex의 \A(?:...)\z wrap이 강제한다(설계 §2.1, §5.2).
        private readonly Regex _regex = DefinitionRegex.Compile(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        public override bool Matches(string fileName) => SafeIsMatch(_regex, fileName);
    }

    /// <summary>regex runtime timeout은 정의 품질 문제다 — FileDefinitionConflict로 변환한다(설계 §5.2, §8).
        /// dir/file/metadata 세 regex 종류가 같은 오류 계약을 공유한다.</summary>
    internal static bool SafeIsMatch(System.Text.RegularExpressions.Regex regex, string input)
    {
        try { return regex.IsMatch(input); }
        catch (RegexMatchTimeoutException ex)
        {
            throw new FileGateway.Core.Errors.FileGatewayException("FileDefinitionConflict",
                $"regex match timeout: {ex.Message}", ex);
        }
    }
}

/// <summary>컴파일된 metadata rule. 후보 파일(FilePattern 통과)의 fileName에서 timestamp를 추출한다.
/// 매칭/추출 실패는 null 반환 — 호출자(HistoryResolver)가 FileDefinitionConflict로 처리한다(설계 §3.2, §8).</summary>
public sealed class ParsedMetadataRule
{
    private readonly ConfigurationMetadataMode _mode;
    private readonly Regex _regex;
    private readonly string? _group;
    private readonly string? _format;

    private ParsedMetadataRule(ConfigurationMetadataMode mode, Regex regex, string? group, string? format)
    {
        _mode = mode; _regex = regex; _group = group; _format = format;
    }

    /// <summary>컴파일된 pattern의 named group 이름(validator의 mapping group 존재 검사용).</summary>
    internal IReadOnlyList<string> GroupNames => _regex.GetGroupNames();

    /// <summary>fileName에서 snapshotTimestamp를 추출. 실패(비매칭·해석 불가)는 false.</summary>
    public bool TryGetTimestamp(string fileName, out DateTimeOffset timestamp)
    {
        timestamp = default;
        try
        {
            return _mode == ConfigurationMetadataMode.Template
                ? TryTemplate(fileName, out timestamp)
                : TryRegex(fileName, out timestamp);
        }
        catch (RegexMatchTimeoutException)
        {
            return false; // 병리 pattern — 호출자가 conflict로 분류(설계 §5.2)
        }
    }

    // Template: fileName의 첫 '.' 앞 stem에 매칭해 확장자를 독립화한다(설계 §3.1).
    private bool TryTemplate(string fileName, out DateTimeOffset ts)
    {
        ts = default;
        var dot = fileName.IndexOf('.');
        var stem = dot < 0 ? fileName : fileName[..dot];
        var m = _regex.Match(stem);
        if (!m.Success) return false;

        if (!DateTime.TryParseExact($"{m.Groups["fg_yyyy"].Value}-{m.Groups["fg_ts_MM"].Value}-{m.Groups["fg_ts_dd"].Value}",
                "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;
        var hour = 0; var minute = 0;
        // 범위 밖 HH(>23)/mm(>59)는 보정 없이 실패 → FileDefinitionConflict(잘못된 시각의
        // logical identity/fileId 발급 방지 — PR24 리뷰 P1-S3).
        if (m.Groups["fg_ts_HH"].Success
            && (!int.TryParse(m.Groups["fg_ts_HH"].Value, out hour) || hour is < 0 or > 23)) return false;
        if (m.Groups["fg_ts_mm"].Success
            && (!int.TryParse(m.Groups["fg_ts_mm"].Value, out minute) || minute is < 0 or > 59)) return false;
        var local = date.AddHours(hour).AddMinutes(minute);
        ts = new DateTimeOffset(local, SiteTime.Local.GetUtcOffset(local));
        return true;
    }

    // Regex: 단일 named group 값 전체를 format으로 해석한다(구성 group 조립 금지 — 설계 §3.2, P1-4).
    private bool TryRegex(string fileName, out DateTimeOffset ts)
    {
        ts = default;
        var m = _regex.Match(fileName);
        if (!m.Success || !m.Groups[_group!].Success) return false;
        if (!DateTime.TryParseExact(m.Groups[_group!].Value, _format!, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return false;
        // format에 offset 지정자는 허용하지 않는다(validator) — 값은 site-local로 해석한다.
        var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        ts = new DateTimeOffset(local, SiteTime.Local.GetUtcOffset(local));
        return true;
    }

    public static ParsedMetadataRule Compile(ConfigurationMetadataRule rule)
    {
        if (rule.Mode == ConfigurationMetadataMode.Template)
        {
            // mappings는 빈 목록 계약 — 토큰명이 곧 mapping(04a). yyyy-MM-dd 필수는 validator가 검사한다.
            var regex = DefinitionRegex.Compile(TemplateToRegex(rule.Pattern), RegexOptions.IgnoreCase | RegexOptions.Compiled);
            return new(rule.Mode, regex, null, null);
        }
        var compiled = DefinitionRegex.Compile(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        var map = rule.Mappings[0];
        return new(rule.Mode, compiled, map.Group, map.Format);
    }

    // Logs의 TemplateToRegex 변환을 미러링: 허용 token {yyyy}{MM}{dd}{HH}{mm} → named group, 그 외 escape.
    private static string TemplateToRegex(string pattern)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '{')
            {
                var close = pattern.IndexOf('}', i);
                if (close > i)
                {
                    var token = pattern[(i + 1)..close];
                    var group = token switch
                    {
                        "yyyy" => @"(?<fg_yyyy>\d{4})",
                        "MM" => @"(?<fg_ts_MM>\d{2})",
                        "dd" => @"(?<fg_ts_dd>\d{2})",
                        "HH" => @"(?<fg_ts_HH>\d{2})",
                        "mm" => @"(?<fg_ts_mm>\d{2})",
                        _ => throw new ArgumentException($"unknown metadata token: {{{token}}}"),
                    };
                    sb.Append(group);
                    i = close;
                    continue;
                }
            }
            sb.Append(Regex.Escape(pattern[i].ToString()));
        }
        return sb.ToString();
    }
}

/// <summary>경로 세그먼트 + Regex 세그먼트의 사전 컴파일. 컴파일 수명은 정의(스냅샷) 인스턴스에 바인딩된다
/// — 프로세스 전역 pattern-key 캐시가 아니다(P2-S1 최소 반영).</summary>
public sealed record CompiledPathSegment(PathSegment Segment, Regex? Regex);

public sealed record ParsedCurrentRule(IReadOnlyList<CompiledPathSegment> Path, FileMatcher File);

public sealed record ParsedHistoryRule(
    IReadOnlyList<CompiledPathSegment> Path,
    FileMatcher File,
    IReadOnlyList<PathSegment> MarkerPath,
    ParsedMetadataRule? Metadata);

/// <summary>persisted 정의(문자열)를 구조화·컴파일한다. 검증(Validator)이 파싱에 선행 분리하는 규칙
/// 때문에 파서는 유효한 정의만 다루며, 무효한 mode/regex는 ArgumentException을 던진다(설계 §1.2, §5.1).</summary>
public static class ConfigurationRuleParser
{
    public const string RegexPrefix = "regex:";

    /// <summary>경로 구분자는 '/' 뿐이다. backslash를 치환하지 않는다 — regex 세그먼트의
    /// 정상 .NET escape(\d, \. 등)를 보존한다(P1-S2). 비-regex 세그먼트의 backslash는
    /// validator가 unsafe로 거부한다.</summary>
    public static IReadOnlyList<PathSegment> ParsePath(string pathTemplate)
        => pathTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Classify)
            .ToList();

    private static PathSegment Classify(string segment)
    {
        if (segment.StartsWith(RegexPrefix, StringComparison.Ordinal))
        {
            var pattern = segment[RegexPrefix.Length..];
            return new PathSegment(PathSegmentKind.Regex, pattern);
        }
        var hasDateToken = ContainsToken(segment, "{yyyy}") || ContainsToken(segment, "{MM}")
            || ContainsToken(segment, "{dd}") || ContainsToken(segment, "{HH}");
        return new PathSegment(hasDateToken ? PathSegmentKind.DateFormat : PathSegmentKind.Literal, segment);
    }

    private static bool ContainsToken(string s, string token) => s.Contains(token, StringComparison.Ordinal);

    /// <summary>Template 세그먼트(Literal/DateFormat)를 슬롯 날짜(site-local)로 확장한다.</summary>
    public static string ExpandSegment(PathSegment segment, DateTimeOffset slot)
    {
        if (segment.Kind == PathSegmentKind.Regex)
            throw new ArgumentException("cannot expand a regex segment", nameof(segment));
        return segment.Value
            .Replace("{yyyy}", slot.ToString("yyyy", CultureInfo.InvariantCulture))
            .Replace("{MM}", slot.ToString("MM", CultureInfo.InvariantCulture))
            .Replace("{dd}", slot.ToString("dd", CultureInfo.InvariantCulture))
            .Replace("{HH}", slot.ToString("HH", CultureInfo.InvariantCulture));
    }

    public static FileMatchMode ParseFileMatchMode(string mode)
    {
        if (string.IsNullOrEmpty(mode)) return FileMatchMode.Glob; // 미지정 → Glob 기본(설계 §2.1)
        if (Enum.TryParse(mode, out FileMatchMode parsed) && Enum.IsDefined(parsed)) return parsed;
        throw new ArgumentException($"invalid file match mode: {mode}");
    }
    // 컴파일은 파싱 시점 1회만(설계 §5.1). 정의 객체는 기준정보 스냅샷마다 새 인스턴스로 교체되므로
    // ConditionalWeakTable(참조 수명 연동)로 캐싱한다 — 스냅샷이 GC되면 파싱 결과도 함께 회수된다.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CurrentRule, ParsedCurrentRule> CurrentCache = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HistoryRule, ParsedHistoryRule> HistoryCache = new();


    /// <summary>Regex 세그먼트를 컴파일한다(IgnoreCase|Compiled, 전체 일치 wrap + timeout은 DefinitionRegex).</summary>
    public static IReadOnlyList<CompiledPathSegment> CompilePath(string pathTemplate)
        => ParsePath(pathTemplate).Select(s => s.Kind == PathSegmentKind.Regex
            ? new CompiledPathSegment(s,
                DefinitionRegex.Compile(s.Value, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            : new CompiledPathSegment(s, null)).ToList();

    public static ParsedCurrentRule ParseCurrent(CurrentRule rule)
        => CurrentCache.GetValue(rule, r =>
            new ParsedCurrentRule(CompilePath(r.PathTemplate),
                FileMatcher.Create(ParseFileMatchMode(r.FileMatchMode), r.FilePattern)));

    public static ParsedHistoryRule ParseHistory(HistoryRule rule)
        => HistoryCache.GetValue(rule, r =>
            new ParsedHistoryRule(CompilePath(r.PathTemplate),
                FileMatcher.Create(ParseFileMatchMode(r.FileMatchMode), r.FilePattern),
                ParsePath(r.MarkerPathTemplate),
                r.Metadata is null ? null : ParsedMetadataRule.Compile(r.Metadata)));
}
