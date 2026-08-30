// src/FileGateway.Configurations/Definitions/ConfigurationDefinitionValidator.cs
using System.Text.RegularExpressions;
using FileGateway.Configurations.Internal;
using FileGateway.Core.Files;
using FileGateway.Core.Paths;

namespace FileGateway.Configurations.Definitions;

public static class ConfigurationDefinitionValidator
{
    private static readonly string[] PathTokens = ["{yyyy}", "{MM}", "{dd}", "{HH}"];
    private static readonly string[] MetadataTokens = ["{yyyy}", "{MM}", "{dd}", "{HH}", "{mm}"];

    public static IReadOnlyList<string> Validate(EquipmentConfigurationDefinition def)
    {
        var errors = new List<string>();

        ValidatePath(def.CurrentRule.PathTemplate, "currentRule pathTemplate", allowRegex: true, errors);
        ValidateFileRule(def.CurrentRule.FileMatchMode, def.CurrentRule.FilePattern, "currentRule", errors);
        ValidatePath(def.HistoryRule.PathTemplate, "historyRule pathTemplate", allowRegex: true, errors);
        ValidateFileRule(def.HistoryRule.FileMatchMode, def.HistoryRule.FilePattern, "historyRule", errors);
        ValidatePath(def.HistoryRule.MarkerPathTemplate, "historyRule markerPathTemplate", allowRegex: false, errors);

        RequireDateTokens(def.HistoryRule.PathTemplate, "historyRule pathTemplate", errors);
        RequireDateTokens(def.HistoryRule.MarkerPathTemplate, "historyRule markerPathTemplate", errors);

        if (def.HistoryRule.Metadata is { } metadata)
            ValidateMetadata(metadata, "historyRule metadata", errors);

        return errors;
    }

    // 검증 경계(P1-1): 파싱이 검증에 선행한다. raw 문자열 전체에 safe-path/token 검사를 적용하면
    // `regex:` 접두사의 ':'가 IsRooted에 걸리고, 수량자 `[0-9]{2}`의 `{2}`가 미지원 token으로 오인된다.
    // 세그먼트를 분리해 비-regex 세그먼트에만 기존 검증을 적용하고, regex pattern에는 별도 검사를 한다.
    // 빈 세그먼트는 기존 동작(IsSafeDefinitionPath/Normalize가 RemoveEmptyEntries로 제거)을 보존해
    // 제거한 뒤 파싱한다(P2-N3 판정).
    private static void ValidatePath(string pathTemplate, string field, bool allowRegex, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(pathTemplate)
            || pathTemplate.Trim().StartsWith('/') || pathTemplate.Trim().StartsWith('\\'))
        {
            errors.Add($"{field} unsafe: {pathTemplate}");
            return;
        }

        var segments = ConfigurationRuleParser.ParsePath(pathTemplate);
        var nonRegex = segments.Where(s => s.Kind != PathSegmentKind.Regex).Select(s => s.Value).ToList();
        var regexSegments = segments.Where(s => s.Kind == PathSegmentKind.Regex).Select(s => s.Value).ToList();

        // ':' 판정은 비-regex 세그먼트에만 — regex: 접두사 자체는 예약어다(설계 §1.1).
        // non-regex 세그먼트가 하나도 없으면(전체 regex 경로) safe-path 검사 대상 자체가 없다.
        var recombined = string.Join("/", nonRegex);
        if (nonRegex.Count > 0
            && (nonRegex.Any(s => s.Contains(':')) || !RemotePath.IsSafeDefinitionPath(recombined)))
        {
            errors.Add($"{field} unsafe: {pathTemplate}");
            return; // unsafe면 token 검사까지 돌려 같은 필드에 오류가 중복된다(기존 동작 보존)
        }
        if (nonRegex.Any(s => s.Contains("..")))
            errors.Add($"{field} contains '..'");
        // LogDefinitionValidator와 동일한 PathTokens 화이트리스트 — 미지원 token은 거부.
        foreach (var token in nonRegex.SelectMany(ExtractTokens))
            if (!PathTokens.Contains(token))
                errors.Add($"{field} unknown token: {token}");

        if (!allowRegex && regexSegments.Count > 0)
            errors.Add($"{field} must not contain regex: segments"); // marker는 확정 1개 경로(설계 §1.4)

        foreach (var pattern in regexSegments)
            ValidateRegexPattern(pattern, field, errors);
    }

    // regex pattern 검사(설계 §5.1): 비어있지 않음, '/' 미포함, ^...$ anchor, 컴파일 성공.
    // 전체 일치 보장은 DefinitionRegex의 \A(?:...)\z wrap이 담당하고 anchor는 작성 규칙으로 유지한다.
    private static void ValidateRegexPattern(string pattern, string field, List<string> errors)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            errors.Add($"{field} empty regex pattern");
            return;
        }
        if (pattern.Contains('/'))
        {
            errors.Add($"{field} regex pattern must not contain '/': {pattern}");
            return;
        }
        if (!pattern.StartsWith('^') || !pattern.EndsWith('$'))
            errors.Add($"{field} regex pattern must be anchored (^...$): {pattern}");
        try { DefinitionRegex.Compile(pattern, RegexOptions.IgnoreCase); }
        catch (ArgumentException ex) { errors.Add($"{field} invalid regex: {ex.Message}"); }
    }

    private static void ValidateFileRule(string mode, string filePattern, string field, List<string> errors)
    {
        FileMatchMode parsed;
        try { parsed = ConfigurationRuleParser.ParseFileMatchMode(mode); }
        catch (ArgumentException ex) { errors.Add($"{field} {ex.Message}"); return; }

        if (parsed == FileMatchMode.Glob)
        {
            try { GlobPattern.Validate(filePattern); }
            catch (ArgumentException ex) { errors.Add($"{field} filePattern invalid: {ex.Message}"); }
            return;
        }
        if (parsed == FileMatchMode.Literal)
        {
            if (string.IsNullOrWhiteSpace(filePattern))
                errors.Add($"{field} filePattern must not be empty");
            if (filePattern.Contains('/'))
                errors.Add($"{field} filePattern must not contain '/': {filePattern}");
            return;
        }
        ValidateRegexPattern(filePattern, $"{field} filePattern", errors);
    }

    private static void ValidateMetadata(ConfigurationMetadataRule rule, string field, List<string> errors)
    {
        if (rule.Mode == ConfigurationMetadataMode.Template)
        {
            if (rule.Mappings.Count > 0)
                errors.Add($"{field} Template mode must not have mappings");
            foreach (var token in ExtractTokens(rule.Pattern))
                if (!MetadataTokens.Contains(token))
                    errors.Add($"{field} unknown token: {token}");
            if (!rule.Pattern.Contains("{yyyy}", StringComparison.Ordinal)
                || !rule.Pattern.Contains("{MM}", StringComparison.Ordinal)
                || !rule.Pattern.Contains("{dd}", StringComparison.Ordinal))
                errors.Add($"{field} Template pattern must contain {{yyyy}}{{MM}}{{dd}} tokens");
            try { ParsedMetadataRule.Compile(rule); }
            catch (ArgumentException ex) { errors.Add($"{field} invalid: {ex.Message}"); }
            return;
        }

        ValidateRegexPattern(rule.Pattern, field, errors);
        if (rule.Mappings.Count != 1)
        {
            errors.Add($"{field} Regex mode must have exactly one mapping");
            return;
        }
        var map = rule.Mappings[0];
        if (map.Target != "timestamp")
            errors.Add($"{field} mapping target must be 'timestamp': {map.Target}");
        if (string.IsNullOrEmpty(map.Format))
            errors.Add($"{field} mapping format is required");
        else
            ValidateTimestampFormat(map.Format, field, errors);
        try
        {
            var compiled = ParsedMetadataRule.Compile(rule);
            if (!compiled.GroupNames.Contains(map.Group))
                errors.Add($"{field} mapping group not found in pattern: {map.Group}");
        }
        catch (ArgumentException ex) { errors.Add($"{field} invalid: {ex.Message}"); }
    }

    // format은 y,M,d 필수 + H,m 선택 — 그 외 지정자(offset·fraction·ampm 등)는 거부해 해석 모호성을 없앤다.
    private static void ValidateTimestampFormat(string format, string field, List<string> errors)
    {
        var letters = format.Where(char.IsLetter).ToList();
        if (!letters.Contains('y') || !letters.Contains('M') || !letters.Contains('d'))
            errors.Add($"{field} format must contain y, M, d: {format}");
        if (letters.Any(c => c is not ('y' or 'M' or 'd' or 'H' or 'm')))
            errors.Add($"{field} format allows only y, M, d, H, m: {format}");
    }

    private static void RequireDateTokens(string pathTemplate, string field, List<string> errors)
    {
        // regex 세그먼트의 수량자({2})를 token으로 오인하지 않도록 비-regex 세그먼트만 검사한다.
        var nonRegex = string.Join("/",
            ConfigurationRuleParser.ParsePath(pathTemplate)
                .Where(s => s.Kind != PathSegmentKind.Regex)
                .Select(s => s.Value));
        if (!nonRegex.Contains("{yyyy}", StringComparison.Ordinal) ||
            !nonRegex.Contains("{MM}", StringComparison.Ordinal) ||
            !nonRegex.Contains("{dd}", StringComparison.Ordinal))
            errors.Add($"{field} must contain {{yyyy}}{{MM}}{{dd}} tokens");
        if (nonRegex.Contains("{HH}", StringComparison.Ordinal))
            errors.Add($"{field} must not contain {{HH}} token");
    }

    private static IEnumerable<string> ExtractTokens(string pattern)
        => Regex.Matches(pattern, @"\{[^}]+\}").Select(m => m.Value);
}
