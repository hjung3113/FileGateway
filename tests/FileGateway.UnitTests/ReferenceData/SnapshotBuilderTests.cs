// tests/FileGateway.UnitTests/ReferenceData/SnapshotBuilderTests.cs
using FileGateway.Configurations.Definitions;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.Logs.Definitions;

namespace FileGateway.UnitTests.ReferenceData;

public class SnapshotBuilderTests
{
    private static ReferenceDataRaw Valid() => new(
        ["EQ-001", "EQ-002"],
        [new RawServer("SRV1", "ftp1.internal", "ftproot")],
        [new RawLogDefinition("EQ-001", "EventLog", "SRV1", "Hourly",
            "Logs/{yyyy}/{MM}/{dd}/{HH}", "*.zip", "Multiple",
            "Template", "{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip", "[]")],
        []);

    [Fact]
    public void Builds_snapshot_with_indexes()
    {
        var snap = ReferenceDataSnapshotBuilder.Build(Valid());
        Assert.Contains("EQ-001", snap.EquipmentIds);
        var def = snap.FindLog("EQ-001", "eventlog"); // logType 조회는 대소문자 그대로 지원(정확 일치)
        Assert.Null(def);
        def = snap.FindLog("EQ-001", "EventLog");
        Assert.NotNull(def);
        Assert.Equal("ftp1.internal", def.Server.Host);
        Assert.Equal("EventLog", Assert.Single(snap.GetLogSummaries("EQ-001")).LogType);
    }

    [Fact]
    public void Rejects_duplicate_equipment_logType()
    {
        var raw = Valid();
        raw = raw with { LogDefinitions = [.. raw.LogDefinitions, raw.LogDefinitions[0]] };
        Assert.Throws<ReferenceDataValidationException>(() => ReferenceDataSnapshotBuilder.Build(raw));
    }

    [Fact]
    public void Rejects_unknown_server_and_unknown_equipment()
    {
        var raw = Valid() with
        {
            LogDefinitions = [Valid().LogDefinitions[0] with { ServerId = "NOPE" }]
        };
        Assert.Throws<ReferenceDataValidationException>(() => ReferenceDataSnapshotBuilder.Build(raw));

        var raw2 = Valid() with
        {
            ConfigurationDefinitions = [new RawConfigurationDefinition(
                "EQ-X", "PM", "SRV1", "PM", "PM_*.cfg", "History/{yyyy}/{MM}/{dd}", "PM_*.cfg", "{yyyy}/{MM}/{dd}/_DONE")]
        };
        Assert.Throws<ReferenceDataValidationException>(() => ReferenceDataSnapshotBuilder.Build(raw2));
    }

    [Fact]
    public void Rejects_path_escape_attempt()
    {
        var raw = Valid() with
        {
            LogDefinitions = [Valid().LogDefinitions[0] with { PathTemplate = "../other/{yyyy}" }]
        };
        Assert.Throws<ReferenceDataValidationException>(() => ReferenceDataSnapshotBuilder.Build(raw));
    }

    [Theory]
    [InlineData("Logs/../../x", "Escape")]
    [InlineData("/abs/{yyyy}", "Rooted")]
    [InlineData("Logs/{yyyy}", "BadGlob")]
    public void Validator_reports_specific_errors(string pathTemplate, string _case)
    {
        var def = new EquipmentLogDefinition("EQ-001", "EventLog", "SRV1", GenerationType.Hourly,
            new LogDiscoveryRule(pathTemplate, _case == "BadGlob" ? "a/b" : "*.zip", Cardinality.Single),
            new LogMetadataRule(MetadataMode.Template,
                _case == "BadGlob" ? "{yyyy}/{MM}/{dd}/{HH}/Event.zip" : pathTemplate + "/Event.zip", []));
        var errors = LogDefinitionValidator.Validate(def);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validator_requires_hour_tokens_for_hourly_and_forbids_for_daily()
    {
        var hourly = new EquipmentLogDefinition("E", "L", "S", GenerationType.Hourly,
            new LogDiscoveryRule("Logs", "*.log", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Template, "{yyyy}/{MM}/{dd}/Event.log", [])); // {HH} 없음
        Assert.Contains(LogDefinitionValidator.Validate(hourly), e => e.Contains("HH"));

        var daily = new EquipmentLogDefinition("E", "L", "S", GenerationType.Daily,
            new LogDiscoveryRule("Logs", "*.log", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Template, "{yyyy}/{MM}/{dd}/{HH}/Event.log", [])); // Daily에 HH
        Assert.Contains(LogDefinitionValidator.Validate(daily), e => e.Contains("Daily"));
    }

    [Fact]
    public void Validator_accepts_continuous_without_date_tokens()
    {
        var def = new EquipmentLogDefinition("E", "Trace", "S", GenerationType.Continuous,
            new LogDiscoveryRule("Trace/current", "Trace_*.log", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Template, "Trace/current/Trace_{subtype}.log", []));
        Assert.Empty(LogDefinitionValidator.Validate(def));
    }

    [Fact]
    public void Validator_rejects_unsupported_regex_target_or_missing_format()
    {
        var def = new EquipmentLogDefinition("E", "L", "S", GenerationType.Hourly,
            new LogDiscoveryRule("Logs", "*.log", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Regex, @"^Logs/(\d{4})/",
                [new MetadataMapping("1", "timestamp", null)])); // 숫자 그룹명 불가/형식 없음
        Assert.NotEmpty(LogDefinitionValidator.Validate(def));
    }

    [Fact]
    public void Configuration_validator_requires_date_tokens_in_history_rules()
    {
        var def = new EquipmentConfigurationDefinition("E", "PM", "S",
            new CurrentRule("PM/current", "PM_*.cfg"),
            new HistoryRule("PM/history", "PM_*.cfg", "PM/history/_DONE")); // 날짜 토큰 없음
        Assert.NotEmpty(ConfigurationDefinitionValidator.Validate(def));
    }

    [Fact]
    public void Configuration_validator_rejects_unknown_path_tokens()
    {
        var def = new EquipmentConfigurationDefinition("E", "PM", "S",
            new CurrentRule("PM/{plant}", "PM_*.cfg"),
            new HistoryRule("PM/hist/{yyyy}/{MM}/{dd}", "PM_*.cfg", "PM/hist/{yyyy}/{MM}/{dd}/{week}"));
        var errors = ConfigurationDefinitionValidator.Validate(def);
        Assert.Contains(errors, e => e.Contains("{plant}")); // currentRule pathTemplate
        Assert.Contains(errors, e => e.Contains("{week}"));  // historyRule markerPathTemplate
    }

    [Theory]
    [InlineData("Logs/2024/file.log")]
    [InlineData("^Logs/2024/file.log")]
    [InlineData("Logs/2024/file.log$")]
    public void Validator_rejects_unanchored_metadata_regex(string pattern)
    {
        var def = new EquipmentLogDefinition("E", "L", "S", GenerationType.Hourly,
            new LogDiscoveryRule("Logs", "*.log", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Regex, pattern,
                [new MetadataMapping("ts", "timestamp", "yyyy/MM/dd/HH")]));
        Assert.Contains(LogDefinitionValidator.Validate(def), e => e.Contains("anchored"));
    }

    [Fact]
    public void Validator_accepts_anchored_full_path_regex()
    {
        var def = new EquipmentLogDefinition("E", "L", "S", GenerationType.Hourly,
            new LogDiscoveryRule("Logs", "*.log", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Regex, @"^Logs/(?<ts>\d{4}/\d{2}/\d{2}/\d{2})/[^/]+\.log$",
                [new MetadataMapping("ts", "timestamp", "yyyy/MM/dd/HH")]));
        Assert.Empty(LogDefinitionValidator.Validate(def));
    }

    [Fact]
    public void Validator_rejects_hourly_regex_timestamp_format_missing_hour()
    {
        var def = new EquipmentLogDefinition("E", "L", "S", GenerationType.Hourly,
            new LogDiscoveryRule("Logs", "*.log", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Regex, @"^Logs/(?<ts>\d{4}\d{2}\d{2})/[^/]+\.log$",
                [new MetadataMapping("ts", "timestamp", "yyyyMMdd")])); // 시간 없는 부분 포맷
        Assert.Contains(LogDefinitionValidator.Validate(def), e => e.Contains("Hourly"));
    }

    [Fact]
    public void Validator_checks_group_existence_for_subtype_and_attribute_mappings()
    {
        var def = new EquipmentLogDefinition("E", "L", "S", GenerationType.Continuous,
            new LogDiscoveryRule("Logs", "*.log", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Regex, @"^Cfg/(?<env>prod)/[^/]+\.zip$",
            [
                new MetadataMapping("env", "subtype", null),        // 존재 — 오류 아님
                new MetadataMapping("nope", "attribute.line", null) // 부재 — 오류
            ]));
        Assert.Single(LogDefinitionValidator.Validate(def), e => e.Contains("nope"));
    }

    [Fact]
    public void Snapshot_exposes_root_boundary_via_servers() // rootPath 경계 데이터가 스냅샷에 보존됨
    {
        var snap = ReferenceDataSnapshotBuilder.Build(Valid());
        Assert.Equal("ftproot", snap.Servers["SRV1"].RootPath);
    }
}
