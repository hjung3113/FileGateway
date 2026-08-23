using FileGateway.Logs.Definitions;
using FileGateway.Logs.Internal;

namespace FileGateway.UnitTests.Logs;

public class MetadataRuleParserTests
{
    private const string Path244 = "Logs/2026/08/22/18/Event_A.zip";

    [Fact]
    public void Template_extracts_timestamp_subtype_and_attributes()
    {
        var rule = new LogMetadataRule(MetadataMode.Template,
            "Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip", []);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Hourly, Path244)!;
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.FromHours(9)), meta.Timestamp);
        Assert.Equal("A", meta.Subtype);
    }

    [Fact]
    public void Template_extracts_attributes()
    {
        var rule = new LogMetadataRule(MetadataMode.Template,
            "Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{attribute.lot}_{subtype}.zip", []);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Hourly,
            "Logs/2026/08/22/18/Event_L07_A.zip")!;
        Assert.Equal("L07", meta.Attributes["lot"]);
        Assert.Equal("A", meta.Subtype);
    }

    [Fact]
    public void Template_extracts_dotted_attribute_key()
    {
        var rule = new LogMetadataRule(MetadataMode.Template, "Trace/{attribute.source.ip}.log", []);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Continuous, "Trace/10.0.0.1.log")!;
        Assert.Equal("10.0.0.1", meta.Attributes["source.ip"]);
    }

    [Fact]
    public void Template_duplicate_hour_token_returns_null()
        => Assert.Null(MetadataRuleParser.Parse(
            new LogMetadataRule(MetadataMode.Template,
                "Logs/{yyyy}/{MM}/{dd}/{HH}/{HH}/Event.log", []),
            GenerationType.Hourly, "Logs/2026/08/22/18/18/Event.log"));

    [Fact]
    public void Daily_timestamp_is_site_local_midnight()
    {
        var rule = new LogMetadataRule(MetadataMode.Template, "Logs/{yyyy}/{MM}/{dd}/Event_{subtype}.zip", []);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Daily, "Logs/2026/08/22/Event_A.zip")!;
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)), meta.Timestamp);
    }

    [Fact]
    public void Continuous_without_date_tokens_yields_null_timestamp()
    {
        var rule = new LogMetadataRule(MetadataMode.Template, "Trace/Trace_{subtype}.log", []);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Continuous, "Trace/Trace_PM.log")!;
        Assert.Null(meta.Timestamp);
        Assert.Equal("PM", meta.Subtype);
    }

    [Fact]
    public void Template_mismatch_returns_null()
        => Assert.Null(MetadataRuleParser.Parse(
            new LogMetadataRule(MetadataMode.Template, "Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip", []),
            GenerationType.Hourly, "Logs/2026/08/22/18/Other_A.zip"));

    [Fact]
    public void Regex_named_groups_with_mappings()
    {
        var rule = new LogMetadataRule(MetadataMode.Regex,
            @"^Logs/(?<ts>\d{8}_\d{2})/Event_(?<s>[A-Z0-9]+)\.zip$",
            [new MetadataMapping("ts", "timestamp", "yyyyMMdd_HH"), new MetadataMapping("s", "subtype", null)]);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Hourly, "Logs/20260822_18/Event_A.zip")!;
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.FromHours(9)), meta.Timestamp);
        Assert.Equal("A", meta.Subtype);
    }

    [Fact]
    public void Regex_missing_required_group_returns_null()
        => Assert.Null(MetadataRuleParser.Parse(
            new LogMetadataRule(MetadataMode.Regex, @"^Logs/(?<s>x)/y\.zip$",
                [new MetadataMapping("ts", "timestamp", "yyyyMMdd")]),
            GenerationType.Hourly, "Logs/x/y.zip"));

    [Fact]
    public void Regex_attribute_mapping()
    {
        var rule = new LogMetadataRule(MetadataMode.Regex, @"^L/(?<v>\d+)/a\.log$",
            [new MetadataMapping("v", "attribute.version", null)]);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Continuous, "L/3/a.log")!;
        Assert.Equal("3", meta.Attributes["version"]);
    }

    [Fact]
    public void Regex_datetime_with_offsetless_value_interpreted_as_seoul()
    {
        var rule = new LogMetadataRule(MetadataMode.Regex, @"^D/(?<ts>\d{8})/x\.log$",
            [new MetadataMapping("ts", "timestamp", "yyyyMMdd")]);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Daily, "D/20260822/x.log")!;
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)), meta.Timestamp);
    }

    [Fact]
    public void Regex_timestamp_with_z_value_preserves_utc_offset()
    {
        var rule = new LogMetadataRule(MetadataMode.Regex, @"^U/(?<ts>\d{8}_\d{4}Z)/x\.log$",
            [new MetadataMapping("ts", "timestamp", "yyyyMMdd_HHmmK")]);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Hourly, "U/20260822_1800Z/x.log")!;
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.Zero), meta.Timestamp);
    }

    [Fact]
    public void Template_hourly_includes_minutes_when_mm_is_present()
    {
        var rule = new LogMetadataRule(MetadataMode.Template,
            "Logs/{yyyy}/{MM}/{dd}/{HH}/{mm}/Event.log", []);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Hourly,
            "Logs/2026/08/22/18/42/Event.log")!;
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 18, 42, 0, TimeSpan.FromHours(9)), meta.Timestamp);
    }

    [Fact]
    public void Template_continuous_includes_minutes_when_mm_is_present()
    {
        var rule = new LogMetadataRule(MetadataMode.Template,
            "Trace/{yyyy}/{MM}/{dd}/{HH}/{mm}.log", []);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Continuous,
            "Trace/2026/08/22/18/42.log")!;
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 18, 42, 0, TimeSpan.FromHours(9)), meta.Timestamp);
    }
}
