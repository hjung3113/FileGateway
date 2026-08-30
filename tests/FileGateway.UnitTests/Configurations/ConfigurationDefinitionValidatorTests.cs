using System.Globalization;
using FileGateway.Configurations.Definitions;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;

namespace FileGateway.UnitTests.Configurations;

public class ConfigurationDefinitionValidatorTests
{
    private static ConfigurationMetadataMapping TsMap(string group = "ts", string format = "yyyyMMddHH")
        => new(group, "timestamp", format);

    private static EquipmentConfigurationDefinition Def(
        CurrentRule? current = null, HistoryRule? history = null)
        => new("E", "PM", "S",
            current ?? new CurrentRule("PM/current", "PM*.cfg"),
            history ?? new HistoryRule("PM/history/{yyyy}/{MM}/{dd}", "PM*.cfg", "PM/history/{yyyy}/{MM}/{dd}/_DONE"));

    [Fact]
    public void Accepts_regex_path_segments()
        => Assert.Empty(ConfigurationDefinitionValidator.Validate(Def(
            history: new HistoryRule("PM/history/{yyyy}/{MM}/{dd}/regex:^PM[0-9]$", "PM*.cfg",
                "PM/history/{yyyy}/{MM}/{dd}/_DONE"))));

    [Fact]
    public void Acceptes_regex_file_modes_and_metadata()
        => Assert.Empty(ConfigurationDefinitionValidator.Validate(Def(
            current: new CurrentRule("PM/current", @"^\d{10}\.cfg$", "Regex"),
            history: new HistoryRule("PM/history/{yyyy}/{MM}/{dd}", @"^\d{10}\.(zip|txt\.gz)$",
                "PM/history/{yyyy}/{MM}/{dd}/_DONE", "Regex",
                new ConfigurationMetadataRule(ConfigurationMetadataMode.Regex,
                    @"^(?<ts>\d{10})\.(zip|gz|txt\.gz)$", [TsMap()])))));

    [Fact]
    public void Rejects_unanchored_or_slash_containing_regex_segment()
    {
        Assert.Contains(ConfigurationDefinitionValidator.Validate(Def(
                history: new HistoryRule("PM/h/{yyyy}/{MM}/{dd}/regex:^PM[0-9]", "PM*.cfg", "PM/h/{yyyy}/{MM}/{dd}/_DONE"))),
            e => e.Contains("anchored"));
        // '/'가 pattern 안에 있으면 세그먼트 분리로 잘려 나가 뒤가 template이 되므로 별도 케이스:
        var split = ConfigurationDefinitionValidator.Validate(Def(
            history: new HistoryRule("PM/h/{yyyy}/{MM}/{dd}/regex:a{2}/x", "PM*.cfg", "PM/h/{yyyy}/{MM}/{dd}/_DONE")));
        Assert.Contains(split, e => e.Contains("unknown token") || e.Contains("anchored") || e.Contains("unsafe"));
    }

    [Fact]
    public void Rejects_regex_segment_in_marker_template()
        => Assert.Contains(ConfigurationDefinitionValidator.Validate(Def(
                history: new HistoryRule("PM/h/{yyyy}/{MM}/{dd}", "PM*.cfg", "PM/h/regex:^x$"))),
            e => e.Contains("marker") && e.Contains("regex"));

    [Theory]
    [InlineData("Wildcard")]
    [InlineData("glob")]
    public void Rejects_unknown_file_match_mode(string mode)
        => Assert.Contains(ConfigurationDefinitionValidator.Validate(Def(
                current: new CurrentRule("PM/current", "*.cfg", mode))),
            e => e.Contains("file match mode"));

    [Fact]
    public void Rejects_unanchored_file_regex()
        => Assert.Contains(ConfigurationDefinitionValidator.Validate(Def(
                current: new CurrentRule("PM/current", @"\d{4}\.cfg", "Regex"))),
            e => e.Contains("anchored"));

    [Fact]
    public void Rejects_template_metadata_with_mappings_or_missing_date_tokens()
    {
        var withMappings = Def(history: new HistoryRule("PM/h/{yyyy}/{MM}/{dd}", "*.cfg", "PM/h/{yyyy}/{MM}/{dd}/_DONE",
            Metadata: new ConfigurationMetadataRule(ConfigurationMetadataMode.Template, "{yyyy}{MM}{dd}{HH}",
                [TsMap()])));
        Assert.Contains(ConfigurationDefinitionValidator.Validate(withMappings), e => e.Contains("must not have mappings"));

        var noDate = Def(history: new HistoryRule("PM/h/{yyyy}/{MM}/{dd}", "*.cfg", "PM/h/{yyyy}/{MM}/{dd}/_DONE",
            Metadata: new ConfigurationMetadataRule(ConfigurationMetadataMode.Template, "{yyyy}{MM}", [])));
        Assert.Contains(ConfigurationDefinitionValidator.Validate(noDate), e => e.Contains("{yyyy}{MM}{dd}"));
    }

    [Fact]
    public void Rejects_regex_metadata_contract_violations()
    {
        var two = Def(history: new HistoryRule("PM/h/{yyyy}/{MM}/{dd}", "*.cfg", "PM/h/{yyyy}/{MM}/{dd}/_DONE",
            "Glob", new ConfigurationMetadataRule(ConfigurationMetadataMode.Regex,
                @"^(?<ts>\d{10})\.zip$", [TsMap(), TsMap("ts2")])));
        Assert.Contains(ConfigurationDefinitionValidator.Validate(two), e => e.Contains("exactly one mapping"));

        var badTarget = Def(history: new HistoryRule("PM/h/{yyyy}/{MM}/{dd}", "*.cfg", "PM/h/{yyyy}/{MM}/{dd}/_DONE",
            "Glob", new ConfigurationMetadataRule(ConfigurationMetadataMode.Regex,
                @"^(?<x>\d{10})\.zip$", [new ConfigurationMetadataMapping("x", "subtype", null)])));
        Assert.Contains(ConfigurationDefinitionValidator.Validate(badTarget), e => e.Contains("timestamp"));

        var noFormat = Def(history: new HistoryRule("PM/h/{yyyy}/{MM}/{dd}", "*.cfg", "PM/h/{yyyy}/{MM}/{dd}/_DONE",
            "Glob", new ConfigurationMetadataRule(ConfigurationMetadataMode.Regex,
                @"^(?<ts>\d{10})\.zip$", [new ConfigurationMetadataMapping("ts", "timestamp", null)])));
        Assert.Contains(ConfigurationDefinitionValidator.Validate(noFormat), e => e.Contains("format is required"));

        var missingGroup = Def(history: new HistoryRule("PM/h/{yyyy}/{MM}/{dd}", "*.cfg", "PM/h/{yyyy}/{MM}/{dd}/_DONE",
            "Glob", new ConfigurationMetadataRule(ConfigurationMetadataMode.Regex,
                @"^(?<other>\d{10})\.zip$", [TsMap()])));
        Assert.Contains(ConfigurationDefinitionValidator.Validate(missingGroup), e => e.Contains("group not found"));

        var badFormat = Def(history: new HistoryRule("PM/h/{yyyy}/{MM}/{dd}", "*.cfg", "PM/h/{yyyy}/{MM}/{dd}/_DONE",
            "Glob", new ConfigurationMetadataRule(ConfigurationMetadataMode.Regex,
                @"^(?<ts>\d{10})\.zip$", [TsMap(format: "yyyyMMddHHmmss")])));
        Assert.Contains(ConfigurationDefinitionValidator.Validate(badFormat), e => e.Contains("only y, M, d, H, m"));
    }

    [Fact]
    public void Regex_quantifier_braces_are_not_treated_as_unknown_path_tokens()
        => Assert.Empty(ConfigurationDefinitionValidator.Validate(Def(
            current: new CurrentRule("regex:^PM[0-9]{2}$", "PM*.cfg"))));
}
