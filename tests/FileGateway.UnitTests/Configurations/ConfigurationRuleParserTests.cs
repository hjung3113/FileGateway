using FileGateway.Configurations.Definitions;
using FileGateway.Configurations.Internal;
using FileGateway.Core.Time;

namespace FileGateway.UnitTests.Configurations;

public class ConfigurationRuleParserTests
{
    private static readonly DateTimeOffset Slot = new(2026, 8, 29, 20, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void Parses_regex_prefix_and_templates()
    {
        var segments = ConfigurationRuleParser.ParsePath("config/regex:^PM[0-9]$/{yyyy}-{MM}-{dd}");
        Assert.Equal(PathSegmentKind.Literal, segments[0].Kind);
        Assert.Equal(PathSegmentKind.Regex, segments[1].Kind);
        Assert.Equal("^PM[0-9]$", segments[1].Value);
        Assert.Equal(PathSegmentKind.DateFormat, segments[2].Kind);
    }

    [Fact]
    public void Empty_segments_are_removed_like_existing_normalize()
        => Assert.Equal(2, ConfigurationRuleParser.ParsePath("a//b/").Count);

    [Fact]
    public void ExpandSegment_uses_site_local_slot()
        => Assert.Equal("2026-08-29-20",
            ConfigurationRuleParser.ExpandSegment(
                ConfigurationRuleParser.ParsePath("{yyyy}-{MM}-{dd}-{HH}")[0], Slot));

    [Fact]
    public void File_match_mode_defaults_to_glob_and_rejects_unknown()
    {
        Assert.Equal(FileMatchMode.Glob, ConfigurationRuleParser.ParseFileMatchMode(""));
        Assert.Throws<ArgumentException>(() => ConfigurationRuleParser.ParseFileMatchMode("Wildcard"));
    }

    [Fact]
    public void Literal_and_regex_matchers_use_case_insensitive_full_match()
    {
        var literal = FileMatcher.Create(FileMatchMode.Literal, "PM1.cfg");
        Assert.True(literal.Matches("pm1.CFG"));
        Assert.False(literal.Matches("xPM1.cfg"));

        var regex = FileMatcher.Create(FileMatchMode.Regex, @"^\d{10}\.(zip|txt\.gz)$");
        Assert.True(regex.Matches("2026082920.txt.gz"));
        Assert.False(regex.Matches("2026082920.zip.bak"));
    }

    // — metadata: Template stem 매칭(확장자 독립) —

    private static DateTimeOffset Ts(string fileName)
    {
        var rule = new ConfigurationMetadataRule(ConfigurationMetadataMode.Template, "{yyyy}{MM}{dd}{HH}", []);
        Assert.True(ParsedMetadataRule.Compile(rule).TryGetTimestamp(fileName, out var ts));
        return ts;
    }

    [Theory]
    [InlineData("2026082920.zip")]
    [InlineData("2026082920.gz")]
    [InlineData("2026082920.txt.gz")]
    public void Template_extracts_same_timestamp_across_extensions(string fileName)
        => Assert.Equal(Slot, Ts(fileName)); // stem = 첫 '.' 앞 → 확장자 무관 동일 ts

    [Fact]
    public void Template_without_hour_yields_midnight()
        => Assert.Equal(new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.FromHours(9)),
            Ts2("20260829.zip"));

    private static DateTimeOffset Ts2(string fileName)
    {
        var rule = new ConfigurationMetadataRule(ConfigurationMetadataMode.Template, "{yyyy}{MM}{dd}", []);
        Assert.True(ParsedMetadataRule.Compile(rule).TryGetTimestamp(fileName, out var ts));
        return ts;
    }

    [Fact]
    public void Template_non_matching_stem_fails()
    {
        var rule = new ConfigurationMetadataRule(ConfigurationMetadataMode.Template, "{yyyy}{MM}{dd}{HH}", []);
        Assert.False(ParsedMetadataRule.Compile(rule).TryGetTimestamp("readme.txt", out _));
    }

    // — metadata: Regex 단일 ts named group —

    [Fact]
    public void Regex_single_ts_group_parses_with_format()
    {
        var rule = new ConfigurationMetadataRule(ConfigurationMetadataMode.Regex,
            @"^(?<ts>\d{10})\.(zip|gz|txt\.gz)$",
            [new ConfigurationMetadataMapping("ts", "timestamp", "yyyyMMddHH")]);
        Assert.True(ParsedMetadataRule.Compile(rule).TryGetTimestamp("2026082920.zip", out var ts));
        Assert.Equal(Slot, ts);
    }

    [Fact]
    public void Regex_non_matching_or_bad_value_fails()
    {
        var rule = new ConfigurationMetadataRule(ConfigurationMetadataMode.Regex,
            @"^(?<ts>\d{10})\.zip$", [new ConfigurationMetadataMapping("ts", "timestamp", "yyyyMMddHH")]);
        var compiled = ParsedMetadataRule.Compile(rule);
        Assert.False(compiled.TryGetTimestamp("abcdefghij.zip", out _)); // 매칭은 하되 해석 불가
        Assert.False(compiled.TryGetTimestamp("nope", out _));          // 비매칭
    }
}
