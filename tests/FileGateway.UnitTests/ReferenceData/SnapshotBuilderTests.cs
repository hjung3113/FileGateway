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
    public void Maps_configuration_match_and_metadata_tail_columns()
    {
        var raw = Valid() with
        {
            ConfigurationDefinitions =
            [
                new RawConfigurationDefinition("EQ-001", "PM", "SRV1",
                    "PM/current", "PM*.cfg",
                    "PM/history/{yyyy}/{MM}/{dd}", "^PM.*\\.cfg$", "PM/history/{yyyy}/{MM}/{dd}/_DONE",
                    "Literal", "Regex", "Regex", "^(?<ts>\\d{10})\\.(zip|gz)$",
                    "[{\"group\":\"ts\",\"target\":\"timestamp\",\"format\":\"yyyyMMddHH\"}]")
            ]
        };

        var configuration = Assert.IsType<ResolvedConfigurationDefinition>(
            ReferenceDataSnapshotBuilder.Build(raw).FindConfiguration("EQ-001", "PM"));

        Assert.Equal("Literal", configuration.Definition.CurrentRule.FileMatchMode);
        Assert.Equal("Regex", configuration.Definition.HistoryRule.FileMatchMode);
        var metadata = configuration.Definition.HistoryRule.Metadata;
        Assert.NotNull(metadata);
        Assert.Equal(ConfigurationMetadataMode.Regex, metadata!.Mode);
        Assert.Equal("^(?<ts>\\d{10})\\.(zip|gz)$", metadata.Pattern);
        var mapping = Assert.Single(metadata.Mappings);
        Assert.Equal("ts", mapping.Group);
        Assert.Equal("timestamp", mapping.Target);
        Assert.Equal("yyyyMMddHH", mapping.Format);
    }

    [Fact]
    public void Quarantines_invalid_log_definition_and_keeps_valid_definitions()
    {
        var raw = Valid() with
        {
            LogDefinitions =
            [
                Valid().LogDefinitions[0],
                new RawLogDefinition("EQ-002", "BrokenLog", "SRV1", "Continuous",
                    "Trace/current", "Trace_*.log", "Multiple", "Regex",
                    "Trace/(?<name>.*)", "[]")
            ]
        };

        var snapshot = ReferenceDataSnapshotBuilder.Build(raw);

        Assert.NotNull(snapshot.FindLog("EQ-001", "EventLog"));
        Assert.Null(snapshot.FindLog("EQ-002", "BrokenLog"));
        Assert.Equal(["EventLog"], snapshot.GetLogSummaries("EQ-001").Select(s => s.LogType));
        Assert.Empty(snapshot.GetLogSummaries("EQ-002"));
    }

    [Fact]
    public void Quarantines_invalid_configuration_definition_and_keeps_valid_definitions()
    {
        var validConfiguration = new RawConfigurationDefinition(
            "EQ-001", "PM", "SRV1", "PM/current", "PM_*.cfg",
            "PM/history/{yyyy}/{MM}/{dd}", "PM_*.cfg", "PM/history/{yyyy}/{MM}/{dd}/_DONE");
        var raw = Valid() with
        {
            ConfigurationDefinitions =
            [
                validConfiguration,
                validConfiguration with { ConfigurationType = "Broken", CurrentPathTemplate = "/unsafe/current" }
            ]
        };

        var snapshot = ReferenceDataSnapshotBuilder.Build(raw);

        Assert.NotNull(snapshot.FindConfiguration("EQ-001", "PM"));
        Assert.Null(snapshot.FindConfiguration("EQ-001", "Broken"));
        Assert.Equal(["PM"], snapshot.GetConfigurationTypeSummaries("EQ-001"));
    }

    [Fact]
    public void Duplicate_log_key_quarantines_every_conflicting_row()
    {
        var valid = Valid().LogDefinitions[0];
        var duplicate1 = valid with { LogType = "DuplicateLog" };
        var duplicate2 = duplicate1 with { PathTemplate = "Other/{yyyy}/{MM}/{dd}/{HH}" };
        var raw = Valid() with { LogDefinitions = [valid, duplicate1, duplicate2] };

        var snapshot = ReferenceDataSnapshotBuilder.Build(raw);

        Assert.NotNull(snapshot.FindLog("EQ-001", "EventLog"));
        Assert.Null(snapshot.FindLog("EQ-001", "DuplicateLog"));
    }

    [Fact]
    public void Duplicate_configuration_key_quarantines_every_conflicting_row()
    {
        var valid = new RawConfigurationDefinition(
            "EQ-001", "PM", "SRV1", "PM/current", "PM_*.cfg",
            "PM/history/{yyyy}/{MM}/{dd}", "PM_*.cfg", "PM/history/{yyyy}/{MM}/{dd}/_DONE");
        var duplicate1 = valid with { ConfigurationType = "Duplicate" };
        var duplicate2 = duplicate1 with { CurrentFilePattern = "Other_*.cfg" };
        var raw = Valid() with { ConfigurationDefinitions = [valid, duplicate1, duplicate2] };

        var snapshot = ReferenceDataSnapshotBuilder.Build(raw);

        Assert.NotNull(snapshot.FindConfiguration("EQ-001", "PM"));
        Assert.Null(snapshot.FindConfiguration("EQ-001", "Duplicate"));
    }

    [Fact]
    public void Unknown_references_quarantine_only_the_affected_definitions()
    {
        var validConfiguration = new RawConfigurationDefinition(
            "EQ-001", "PM", "SRV1", "PM/current", "PM_*.cfg",
            "PM/history/{yyyy}/{MM}/{dd}", "PM_*.cfg", "PM/history/{yyyy}/{MM}/{dd}/_DONE");
        var raw = Valid() with
        {
            LogDefinitions =
            [
                Valid().LogDefinitions[0],
                Valid().LogDefinitions[0] with { EquipmentId = "EQ-X", LogType = "UnknownEquipment" },
                Valid().LogDefinitions[0] with { EquipmentId = "EQ-002", LogType = "UnknownServer", ServerId = "NOPE" }
            ],
            ConfigurationDefinitions =
            [
                validConfiguration,
                validConfiguration with { EquipmentId = "EQ-X", ConfigurationType = "UnknownEquipment" },
                validConfiguration with { EquipmentId = "EQ-002", ConfigurationType = "UnknownServer", ServerId = "NOPE" }
            ]
        };

        var snapshot = ReferenceDataSnapshotBuilder.Build(raw);

        Assert.NotNull(snapshot.FindLog("EQ-001", "EventLog"));
        Assert.Null(snapshot.FindLog("EQ-X", "UnknownEquipment"));
        Assert.Null(snapshot.FindLog("EQ-002", "UnknownServer"));
        Assert.NotNull(snapshot.FindConfiguration("EQ-001", "PM"));
        Assert.Null(snapshot.FindConfiguration("EQ-X", "UnknownEquipment"));
        Assert.Null(snapshot.FindConfiguration("EQ-002", "UnknownServer"));
    }

    [Fact]
    public void Duplicate_equipment_or_server_identity_is_still_a_global_failure()
    {
        var duplicateEquipment = Valid() with { EquipmentIds = ["EQ-001", "EQ-001"] };
        Assert.Throws<ReferenceDataValidationException>(() => ReferenceDataSnapshotBuilder.Build(duplicateEquipment));

        var server = Valid().Servers[0];
        var duplicateServer = Valid() with { Servers = [server, server] };
        Assert.Throws<ReferenceDataValidationException>(() => ReferenceDataSnapshotBuilder.Build(duplicateServer));

        var emptyServerId = Valid() with { Servers = [server with { ServerId = "" }] };
        Assert.Throws<ReferenceDataValidationException>(() => ReferenceDataSnapshotBuilder.Build(emptyServerId));
    }

    [Fact]
    public void Quarantines_path_escape_definition()
    {
        var raw = Valid() with
        {
            LogDefinitions = [Valid().LogDefinitions[0] with { PathTemplate = "../other/{yyyy}" }]
        };

        var snapshot = ReferenceDataSnapshotBuilder.Build(raw);

        Assert.Null(snapshot.FindLog("EQ-001", "EventLog"));
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

    [Fact]
    public void Configuration_validator_unsafe_path_reports_only_safety_error()
    {
        // rooted(unsafe) path에 unknown token이 함께 있어도 — 안전 오류만, token 오류는 중복 없음
        var def = new EquipmentConfigurationDefinition("E", "PM", "S",
            new CurrentRule("/PM/{plant}", "PM_*.cfg"),
            new HistoryRule("PM/hist/{yyyy}/{MM}/{dd}", "PM_*.cfg", "PM/hist/{yyyy}/{MM}/{dd}/_DONE"));
        var errors = ConfigurationDefinitionValidator.Validate(def);
        Assert.Single(errors);
        Assert.Contains("currentRule pathTemplate unsafe: /PM/{plant}", errors);
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
