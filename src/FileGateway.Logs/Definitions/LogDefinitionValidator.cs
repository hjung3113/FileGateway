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
            try
            {
                // ExplicitCapture: 이름 없는 그룹 캡처 방지 — mapping은 named group만 허용
                var regex = new Regex(meta.Pattern, RegexOptions.Compiled | RegexOptions.ExplicitCapture);
                if (def.GenerationType != GenerationType.Continuous &&
                    meta.Mappings.All(m => m.Target != "timestamp"))
                    errors.Add($"{def.GenerationType} requires a timestamp mapping");
                foreach (var m in meta.Mappings)
                {
                    if (m.Target is "timestamp")
                    {
                        if (string.IsNullOrEmpty(m.Format)) errors.Add("timestamp mapping requires format");
                        else
                        {
                            if (def.GenerationType == GenerationType.Daily &&
                                (m.Format!.Contains('H') || m.Format.Contains('m') || m.Format.Contains('s')))
                                errors.Add("Daily timestamp format must be date-only");
                            if (!regex.GetGroupNames().Contains(m.Group))
                                errors.Add($"mapping group not in regex: {m.Group}");
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
