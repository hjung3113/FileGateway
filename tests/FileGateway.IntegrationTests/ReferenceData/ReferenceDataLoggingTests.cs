using FileGateway.Core.Errors;
using FileGateway.Infrastructure.ReferenceData;
using Microsoft.Extensions.Logging;

namespace FileGateway.IntegrationTests.Api;

public class ReferenceDataLoggingTests
{
    private sealed class StaticSource(ReferenceDataRaw raw) : IReferenceDataSource
    {
        public Task<ReferenceDataRaw> ReadAsync(CancellationToken ct) => Task.FromResult(raw);
    }

    private sealed class ThrowingSource(Exception exception) : IReferenceDataSource
    {
        public Task<ReferenceDataRaw> ReadAsync(CancellationToken ct) => throw exception;
    }

    [Fact]
    public async Task Cache_logs_each_quarantined_definition_with_equipment_and_reason()
    {
        var raw = new ReferenceDataRaw(
            ["EQ-LOG", "EQ-CONFIG"],
            [new RawServer("SRV1", "host", "root")],
            [
                new RawLogDefinition("EQ-LOG", "ValidLog", "SRV1", "Hourly",
                    "Logs/{yyyy}/{MM}/{dd}/{HH}", "*.log", "Multiple", "Template",
                    "{yyyy}/{MM}/{dd}/{HH}/Event.log", "[]"),
                new RawLogDefinition("EQ-LOG", "BrokenLog", "SRV1", "Hourly",
                    "Logs/{yyyy}/{MM}/{dd}/{HH}", "*.log", "Multiple", "Regex",
                    "not-anchored", "[]")
            ],
            [
                new RawConfigurationDefinition("EQ-CONFIG", "BrokenConfig", "SRV1",
                    "/unsafe/current", "PM_*.cfg",
                    "PM/history/{yyyy}/{MM}/{dd}", "PM_*.cfg",
                    "PM/history/{yyyy}/{MM}/{dd}/_DONE")
            ]);

        var logs = new CollectingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var cache = new ReferenceDataCache(
            new StaticSource(raw),
            TimeSpan.FromMinutes(15),
            loggerFactory.CreateLogger<ReferenceDataCache>());

        var snapshot = await cache.GetSnapshotAsync(CancellationToken.None);
        var warnings = logs.Entries.Where(e => e.Level == LogLevel.Warning).ToList();

        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, e =>
            e.Message.Contains("EQ-LOG", StringComparison.Ordinal) &&
            e.Message.Contains("metadata regex must be anchored", StringComparison.Ordinal));
        Assert.Contains(warnings, e =>
            e.Message.Contains("EQ-CONFIG", StringComparison.Ordinal) &&
            e.Message.Contains("currentRule pathTemplate unsafe", StringComparison.Ordinal));
        // 물리 경로 원문(/unsafe/current)은 quarantine 경고에 남지 않아야 한다 — 카테고리만 노출.
        Assert.DoesNotContain(warnings, e => e.Message.Contains("/unsafe/current", StringComparison.Ordinal));
        Assert.NotNull(snapshot.FindLog("EQ-LOG", "ValidLog"));
        Assert.Null(snapshot.FindLog("EQ-LOG", "BrokenLog"));
        Assert.Null(snapshot.FindConfiguration("EQ-CONFIG", "BrokenConfig"));
    }

    [Fact]
    public async Task Quarantine_reason_uses_current_sp_contract_column_names()
    {
        var raw = new ReferenceDataRaw(
            ["EQ-LOG"],
            [new RawServer("SRV1", "host", "root")],
            [
                new RawLogDefinition("EQ-LOG", "BrokenMetadataMode", "SRV1", "Hourly",
                    "Logs/{yyyy}/{MM}/{dd}/{HH}", "*.log", "Multiple", "Bogus",
                    "{yyyy}/{MM}/{dd}/{HH}/Event.log", "[]")
            ],
            []);

        var logs = new CollectingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var cache = new ReferenceDataCache(
            new StaticSource(raw),
            TimeSpan.FromMinutes(15),
            loggerFactory.CreateLogger<ReferenceDataCache>());

        await cache.GetSnapshotAsync(CancellationToken.None);
        var warnings = logs.Entries.Where(e => e.Level == LogLevel.Warning).ToList();

        // 진단 문자열은 현재 SP 컬럼명(metadataParseMode)을 써야 한다 — 구 컬럼명(metadataMode)이 아니다.
        Assert.Contains(warnings, e => e.Message.Contains("unsupported metadataParseMode", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, e => e.Message.Contains("unsupported metadataMode:", StringComparison.Ordinal));
    }

    [Fact]
    public void Global_failure_logs_no_quarantine_warnings_for_individual_definitions()
    {
        var raw = new ReferenceDataRaw(
            ["EQ-001", "EQ-001"], // duplicate equipmentId — 전역 실패
            [new RawServer("SRV1", "host", "root")],
            [
                new RawLogDefinition("EQ-001", "BrokenLog", "SRV1", "Hourly",
                    "Logs/{yyyy}/{MM}/{dd}/{HH}", "*.log", "Multiple", "Regex",
                    "not-anchored", "[]")
            ],
            []);

        var logs = new CollectingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var logger = loggerFactory.CreateLogger("ReferenceDataSnapshotBuilder");

        Assert.Throws<ReferenceDataValidationException>(() => ReferenceDataSnapshotBuilder.Build(raw, logger));

        // 전역 오류로 snapshot 전체가 거부됐다면, BrokenLog가 "격리"된 것처럼 보이는 경고를 남기면 안 된다.
        Assert.DoesNotContain(logs.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Cache_logs_global_validation_failure_with_each_identifier_error()
    {
        var raw = new ReferenceDataRaw(
            ["EQ-001", "EQ-001"],
            [
                new RawServer("SRV1", "host", "root"),
                new RawServer("SRV1", "host", "root")
            ],
            [],
            []);

        var logs = new CollectingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var cache = new ReferenceDataCache(
            new StaticSource(raw),
            TimeSpan.FromMinutes(15),
            loggerFactory.CreateLogger<ReferenceDataCache>());

        var exception = await Assert.ThrowsAsync<FileGatewayException>(
            () => cache.GetSnapshotAsync(CancellationToken.None));
        Assert.Equal("ReferenceDataUnavailable", exception.Code);

        var errors = logs.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        // 요약 1건 + 오류별 개별 로그 항목 — 하나의 무제한 문자열로 합치지 않는다(오류가 많을 때
        // 로그 sink 크기 제한에 잘려 일부 원인이 관측 불가능해지는 것을 방지, PR #38 리뷰 반영).
        var summary = Assert.Single(errors, e => e.Message.Contains("global validation failure", StringComparison.Ordinal));
        Assert.Contains("2 error(s)", summary.Message, StringComparison.Ordinal);
        Assert.Contains(errors, e => e.Message.Contains("duplicate equipmentId: EQ-001", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Message.Contains("duplicate serverId: SRV1", StringComparison.Ordinal));
        Assert.Equal(3, errors.Count); // 요약 1 + 오류 2건 각각 별도 항목
    }

    [Fact]
    public async Task Cache_logs_every_global_validation_error_individually_when_many_exist()
    {
        var equipmentIds = Enumerable.Range(1, 6).SelectMany(i => new[] { $"EQ-{i:000}", $"EQ-{i:000}" }).ToList();
        var raw = new ReferenceDataRaw(equipmentIds, [new RawServer("SRV1", "host", "root")], [], []);

        var logs = new CollectingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var cache = new ReferenceDataCache(
            new StaticSource(raw),
            TimeSpan.FromMinutes(15),
            loggerFactory.CreateLogger<ReferenceDataCache>());

        await Assert.ThrowsAsync<FileGatewayException>(() => cache.GetSnapshotAsync(CancellationToken.None));

        var errors = logs.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        Assert.Equal(7, errors.Count); // 요약 1 + 중복 equipmentId 6건, 각각 별도 항목으로 전부 관측 가능
        for (var i = 1; i <= 6; i++)
        {
            var id = $"EQ-{i:000}";
            Assert.Contains(errors, e => e.Message.Contains($"duplicate equipmentId: {id}", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Cache_logs_reference_data_incomplete_as_sp_shape_failure()
    {
        const string message = "reference data result set 'Servers' missing";
        var logs = new CollectingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var cache = new ReferenceDataCache(
            new ThrowingSource(new FileGatewayException("ReferenceDataIncomplete", message)),
            TimeSpan.FromMinutes(15),
            loggerFactory.CreateLogger<ReferenceDataCache>());

        await Assert.ThrowsAsync<FileGatewayException>(
            () => cache.GetSnapshotAsync(CancellationToken.None));

        var entry = Assert.Single(logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("SP shape failure", entry.Message, StringComparison.Ordinal);
        Assert.Contains(message, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cache_logs_other_exceptions_as_source_read_failure_with_exception()
    {
        var failure = new InvalidOperationException("boom");
        var logs = new CollectingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var cache = new ReferenceDataCache(
            new ThrowingSource(failure),
            TimeSpan.FromMinutes(15),
            loggerFactory.CreateLogger<ReferenceDataCache>());

        await Assert.ThrowsAsync<FileGatewayException>(
            () => cache.GetSnapshotAsync(CancellationToken.None));

        var entry = Assert.Single(logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("source read failure", entry.Message, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", entry.Message, StringComparison.Ordinal);
        Assert.Contains("boom", entry.Message, StringComparison.Ordinal);
        Assert.Same(failure, entry.Exception);
    }
}
