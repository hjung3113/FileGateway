// src/FileGateway.Configurations/Definitions/ConfigurationDefinitionValidator.cs
using FileGateway.Core.Files;
using FileGateway.Core.Paths;

namespace FileGateway.Configurations.Definitions;

public static class ConfigurationDefinitionValidator
{
    public static IReadOnlyList<string> Validate(EquipmentConfigurationDefinition def)
    {
        var errors = new List<string>();

        ValidatePath(def.CurrentRule.PathTemplate, "currentRule pathTemplate", errors);
        ValidatePattern(def.CurrentRule.FilePattern, "currentRule filePattern", errors);
        ValidatePath(def.HistoryRule.PathTemplate, "historyRule pathTemplate", errors);
        ValidatePattern(def.HistoryRule.FilePattern, "historyRule filePattern", errors);
        ValidatePath(def.HistoryRule.MarkerPathTemplate, "historyRule markerPathTemplate", errors);

        RequireDateTokens(def.HistoryRule.PathTemplate, "historyRule pathTemplate", errors);
        RequireDateTokens(def.HistoryRule.MarkerPathTemplate, "historyRule markerPathTemplate", errors);

        return errors;
    }

    private static void ValidatePath(string pathTemplate, string field, List<string> errors)
    {
        if (!RemotePath.IsSafeDefinitionPath(pathTemplate))
            errors.Add($"{field} unsafe: {pathTemplate}");
        else if (pathTemplate.Split('/').Any(s => s.Contains("..")))
            errors.Add($"{field} contains '..'");
    }

    private static void ValidatePattern(string filePattern, string field, List<string> errors)
    {
        try { GlobPattern.Validate(filePattern); }
        catch (ArgumentException ex) { errors.Add($"{field} invalid: {ex.Message}"); }
    }

    private static void RequireDateTokens(string pathTemplate, string field, List<string> errors)
    {
        if (!pathTemplate.Contains("{yyyy}", StringComparison.Ordinal) ||
            !pathTemplate.Contains("{MM}", StringComparison.Ordinal) ||
            !pathTemplate.Contains("{dd}", StringComparison.Ordinal))
            errors.Add($"{field} must contain {{yyyy}}{{MM}}{{dd}} tokens");
        if (pathTemplate.Contains("{HH}", StringComparison.Ordinal))
            errors.Add($"{field} must not contain {{HH}} token");
    }
}
