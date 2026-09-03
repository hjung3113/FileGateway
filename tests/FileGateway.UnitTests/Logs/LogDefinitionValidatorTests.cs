using FileGateway.Logs.Definitions;

namespace FileGateway.UnitTests.Logs;

public class LogDefinitionValidatorTests
{
    private static EquipmentLogDefinition Def(
        GenerationType gen, Cardinality card, string fileNameTemplate,
        MetadataMode metaMode = MetadataMode.Template, string? metaPattern = null,
        IReadOnlyList<MetadataMapping>? mappings = null)
        => new("EQ-001", "EventLog", "SRV1", gen,
            new LogDiscoveryRule("Logs/{yyyy}/{MM}/{dd}/{HH}", "*.zip", card, fileNameTemplate),
            new LogMetadataRule(metaMode, metaPattern ?? DefaultMetaPattern(gen), mappings ?? []));

    // Daily는 metadata pattern에 시/분 토큰을 포함할 수 없다(기존 validator 규칙) — fileNameTemplate
    // validator만 독립적으로 검증하려면 generation에 맞는 기본 metadata pattern이 필요하다.
    private static string DefaultMetaPattern(GenerationType gen) => gen switch
    {
        GenerationType.Daily => "Logs/{yyyy}/{MM}/{dd}/x.zip",
        _ => "Logs/{yyyy}/{MM}/{dd}/{HH}/x.zip",
    };

    [Fact]
    public void Empty_fileNameTemplate_is_valid_regardless_of_cardinality()
    {
        var def = Def(GenerationType.Hourly, Cardinality.Multiple, "");
        Assert.Empty(LogDefinitionValidator.Validate(def));
    }

    [Fact]
    public void Hourly_fileNameTemplate_with_full_date_tokens_is_valid()
    {
        var def = Def(GenerationType.Hourly, Cardinality.Single, "EQ_{yyyy}{MM}{dd}{HH}.zip");
        Assert.Empty(LogDefinitionValidator.Validate(def));
    }

    [Fact]
    public void Daily_fileNameTemplate_with_hour_token_is_invalid()
    {
        var def = Def(GenerationType.Daily, Cardinality.Single, "EQ_{yyyy}{MM}{dd}{HH}.zip");
        Assert.Contains(LogDefinitionValidator.Validate(def), e => e.Contains("{HH}"));
    }

    [Fact]
    public void Daily_fileNameTemplate_without_hour_is_valid()
    {
        var def = Def(GenerationType.Daily, Cardinality.Single, "EQ_{yyyy}{MM}{dd}.zip");
        Assert.Empty(LogDefinitionValidator.Validate(def));
    }

    [Fact]
    public void Hourly_fileNameTemplate_missing_hour_token_is_invalid()
    {
        var def = Def(GenerationType.Hourly, Cardinality.Single, "EQ_{yyyy}{MM}{dd}.zip");
        Assert.Contains(LogDefinitionValidator.Validate(def), e => e.Contains("Hourly fileNameTemplate"));
    }

    [Fact]
    public void FileNameTemplate_with_cardinality_multiple_is_invalid()
    {
        var def = Def(GenerationType.Hourly, Cardinality.Multiple, "EQ_{yyyy}{MM}{dd}{HH}.zip");
        Assert.Contains(LogDefinitionValidator.Validate(def), e => e.Contains("cardinality=Single"));
    }

    [Fact]
    public void FileNameTemplate_on_continuous_is_invalid()
    {
        var def = Def(GenerationType.Continuous, Cardinality.Single, "EQ_{yyyy}{MM}{dd}{HH}.zip");
        Assert.Contains(LogDefinitionValidator.Validate(def), e => e.Contains("Continuous"));
    }

    [Fact]
    public void FileNameTemplate_with_slash_is_invalid()
    {
        var def = Def(GenerationType.Hourly, Cardinality.Single, "sub/EQ_{yyyy}{MM}{dd}{HH}.zip");
        Assert.Contains(LogDefinitionValidator.Validate(def), e => e.Contains("'/'"));
    }

    [Fact]
    public void FileNameTemplate_with_unknown_token_is_invalid()
    {
        var def = Def(GenerationType.Hourly, Cardinality.Single, "EQ_{yyyy}{MM}{dd}{HH}{mm}.zip");
        Assert.Contains(LogDefinitionValidator.Validate(def), e => e.Contains("unknown fileNameTemplate token"));
    }

    [Fact]
    public void FileNameTemplate_with_subtype_regex_extraction_is_invalid()
    {
        var def = Def(GenerationType.Hourly, Cardinality.Single, "EQ_{yyyy}{MM}{dd}{HH}.zip",
            MetadataMode.Regex, @"^Logs/\d{4}/\d{2}/\d{2}/\d{2}/EQ_\d{10}_(?<sub>[A-Za-z]+)\.zip$",
            [new MetadataMapping("sub", "subtype", null)]);
        Assert.Contains(LogDefinitionValidator.Validate(def),
            e => e.Contains("incompatible with subtype/attribute"));
    }
}
