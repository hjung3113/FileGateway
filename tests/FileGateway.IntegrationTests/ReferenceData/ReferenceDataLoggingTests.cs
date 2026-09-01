using FileGateway.Infrastructure.ReferenceData;
using Microsoft.Extensions.Logging;

namespace FileGateway.IntegrationTests.Api;

public class ReferenceDataLoggingTests
{
    private sealed class StaticSource(ReferenceDataRaw raw) : IReferenceDataSource
    {
        public Task<ReferenceDataRaw> ReadAsync(CancellationToken ct) => Task.FromResult(raw);
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
        Assert.NotNull(snapshot.FindLog("EQ-LOG", "ValidLog"));
        Assert.Null(snapshot.FindLog("EQ-LOG", "BrokenLog"));
        Assert.Null(snapshot.FindConfiguration("EQ-CONFIG", "BrokenConfig"));
    }
}
