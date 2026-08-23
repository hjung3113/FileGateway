// src/FileGateway.Logs/Definitions/LogDefinitionValidator.cs
using System.Text.RegularExpressions;
using FileGateway.Core.Files;
using FileGateway.Core.Paths;

namespace FileGateway.Logs.Definitions;

public static class LogDefinitionValidator
{
    private static readonly string[] PathTokens = ["{yyyy}", "{MM}", "{dd}", "{HH}"];
    private static readonly string[] DateTokens = ["{yyyy}", "{MM}", "{dd}", "{HH}", "{mm}"];

    public static IReadOnlyList<string> Validate(EquipmentLogDefinition def)
    {
        var errors = new List<string>();
        var rule = def.DiscoveryRule;

        if (!RemotePath.IsSafeDefinitionPath(rule.PathTemplate))
            errors.Add($"pathTemplate unsafe: {rule.PathTemplate}");
        else if (rule.PathTemplate.Split('/').Any(s => s.Contains("..")))
            errors.Add("pathTemplate contains '..'");
        foreach (var token in ExtractTokens(rule.PathTemplate))
            if (!PathTokens.Contains(token))
                errors.Add($"unknown pathTemplate token: {token}");

        try { GlobPattern.Validate(rule.FilePattern); }
        catch (ArgumentException ex) { errors.Add($"filePattern invalid: {ex.Message}"); }

        ValidateMetadata(def, errors);
        return errors;
    }

    private static bool HasToken(string s, string t) => s.Contains(t, StringComparison.Ordinal);

    private static void ValidateMetadata(EquipmentLogDefinition def, List<string> errors)
    {
        var meta = def.MetadataRule;
        if (string.IsNullOrWhiteSpace(meta.Pattern)) { errors.Add("metadata pattern empty"); return; }

        if (meta.Mode == MetadataMode.Template)
        {
            foreach (var token in ExtractTokens(meta.Pattern))
                if (!DateTokens.Contains(token) && token != "{subtype}" && !token.StartsWith("{attribute."))
                    errors.Add($"unknown metadata token: {token}");
            var hasDate = HasToken(meta.Pattern, "{yyyy}") && HasToken(meta.Pattern, "{MM}") && HasToken(meta.Pattern, "{dd}");
            var hasHour = HasToken(meta.Pattern, "{HH}");
            if (def.GenerationType == GenerationType.Hourly && !(hasDate && hasHour))
                errors.Add("Hourly metadata pattern must contain yyyy/MM/dd/HH tokens");
            if (def.GenerationType == GenerationType.Daily && (hasHour || HasToken(meta.Pattern, "{mm}")))
                errors.Add("Daily metadata pattern must not contain time tokens");
            if (def.GenerationType == GenerationType.Daily && !hasDate)
                errors.Add("Daily metadata pattern must contain yyyy/MM/dd tokens");
        }
        else
        {
            // metadata regex는 정규화된 전체 상대경로 매칭 계약 — ^...$ anchor 강제.
            // 부분 매칭 pattern은 잘못된 파일을 다른 경로로 오인시킬 수 있다.
            if (!meta.Pattern.StartsWith('^') || !meta.Pattern.EndsWith('$'))
                errors.Add("metadata regex must be anchored to the full path (^...$)");
            try
            {
                // ExplicitCapture: 이름 없는 그룹 캡처 방지 — mapping은 named group만 허용
                var regex = new Regex(meta.Pattern, RegexOptions.Compiled | RegexOptions.ExplicitCapture);
                if (def.GenerationType != GenerationType.Continuous &&
                    meta.Mappings.All(m => m.Target != "timestamp"))
                    errors.Add($"{def.GenerationType} requires a timestamp mapping");
                foreach (var m in meta.Mappings)
                {
                    // group 존재 검증은 timestamp뿐 아니라 모든 mapping(subtype/attribute.*)에 적용한다.
                    if (!regex.GetGroupNames().Contains(m.Group))
                        errors.Add($"mapping group not in regex: {m.Group}");
                    if (m.Target is "timestamp")
                    {
                        if (string.IsNullOrEmpty(m.Format)) errors.Add("timestamp mapping requires format");
                        else
                        {
                            if (def.GenerationType == GenerationType.Daily &&
                                (m.Format!.Contains('H') || m.Format.Contains('m') || m.Format.Contains('s')))
                                errors.Add("Daily timestamp format must be date-only");
                            // Hourly는 년/월/일/시를 모두 포함해야 한다 — 부분 포맷(예: yyyyMMdd) 거부.
                            if (def.GenerationType == GenerationType.Hourly &&
                                !(m.Format.Contains('y') && m.Format.Contains('M') &&
                                  m.Format.Contains('d') && m.Format.Contains('H')))
                                errors.Add("Hourly timestamp format must contain yyyy/MM/dd/HH");
                        }
                    }
                    else if (m.Target is not "subtype" && !m.Target.StartsWith("attribute."))
                        errors.Add($"unsupported mapping target: {m.Target}");
                }
            }
            catch (ArgumentException ex) { errors.Add($"metadata regex invalid: {ex.Message}"); }
        }
    }

    private static IEnumerable<string> ExtractTokens(string pattern)
        => Regex.Matches(pattern, @"\{[^}]+\}").Select(m => m.Value);
}
