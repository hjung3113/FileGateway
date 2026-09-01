using System.Net.Http.Json;
using System.Text.Json;
using FileGateway.Infrastructure.ReferenceData;

namespace FileGateway.IntegrationTests.Api;

public class CatalogEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static ReferenceDataSnapshot Snapshot() => ReferenceDataSnapshotBuilder.Build(new(
        ["EQ-001"],
        [new RawServer("SRV1", "ftp1", "ftproot")],
        [
            new RawLogDefinition("EQ-001", "TraceLog", "SRV1", "Continuous",
                "Trace/cur", "Trace_*.log", "Multiple", "Template", "Trace/cur/Trace_{subtype}.log", "[]"),
            new RawLogDefinition("EQ-001", "EventLog", "SRV1", "Hourly",
                "Logs/{yyyy}/{MM}/{dd}/{HH}", "Event_*.zip", "Multiple", "Template",
                "Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip", "[]"),
        ],
        [new RawConfigurationDefinition("EQ-001", "PM", "SRV1",
            "PM/current", "PM_*.cfg", "PM/history/{yyyy}/{MM}/{dd}", "PM_*.cfg",
            "PM/history/{yyyy}/{MM}/{dd}/_DONE")]));

    private static ReferenceDataSnapshot SnapshotWithQuarantinedDefinitions() => ReferenceDataSnapshotBuilder.Build(new(
        ["EQ-001"],
        [new RawServer("SRV1", "ftp1", "ftproot")],
        [
            new RawLogDefinition("EQ-001", "EventLog", "SRV1", "Hourly",
                "Logs/{yyyy}/{MM}/{dd}/{HH}", "Event_*.zip", "Multiple", "Template",
                "Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip", "[]"),
            new RawLogDefinition("EQ-001", "BrokenLog", "SRV1", "Hourly",
                "Logs/{yyyy}/{MM}/{dd}/{HH}", "Broken_*.zip", "Multiple", "Regex",
                "Logs/(?<name>.*)", "[]")
        ],
        [
            new RawConfigurationDefinition("EQ-001", "PM", "SRV1",
                "PM/current", "PM_*.cfg", "PM/history/{yyyy}/{MM}/{dd}", "PM_*.cfg",
                "PM/history/{yyyy}/{MM}/{dd}/_DONE"),
            new RawConfigurationDefinition("EQ-001", "Broken", "SRV1",
                "/unsafe/current", "Broken_*.cfg", "PM/history/{yyyy}/{MM}/{dd}", "PM_*.cfg",
                "PM/history/{yyyy}/{MM}/{dd}/_DONE")
        ]));

    private async Task<JsonElement> GetAsync(string path)
    {
        var client = factory.CreateClient(); // ApiFactory: FixedSnapshotView(Snapshot()) + key "test-key" 기본 헤더
        using var response = await client.GetAsync(path);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Returns_projection_sorted_without_internal_fields()
    {
        factory.SetSnapshot(Snapshot());
        var body = await GetAsync("/api/v1/equipments/EQ-001/file-types");
        Assert.Equal("EQ-001", body.GetProperty("equipmentId").GetString());
        var logs = body.GetProperty("logs");
        Assert.Equal(2, logs.GetArrayLength());
        Assert.Equal("EventLog", logs[0].GetProperty("logType").GetString());
        Assert.Equal("Hourly", logs[0].GetProperty("generationType").GetString());
        Assert.Equal("TraceLog", logs[1].GetProperty("logType").GetString());
        var json = body.GetRawText();
        Assert.DoesNotContain("serverId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("host", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pathTemplate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rootPath", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_equipment_is_404_EquipmentNotFound()
    {
        factory.SetSnapshot(Snapshot());
        var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/equipments/EQ-X/file-types");
        Assert.Equal(404, (int)response.StatusCode);
        Assert.Equal("EquipmentNotFound",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Valid_equipment_without_definitions_returns_empty_arrays()
    {
        factory.SetSnapshot(ReferenceDataSnapshotBuilder.Build(new(["EQ-EMPTY"], [], [], [])));
        var body = await GetAsync("/api/v1/equipments/EQ-EMPTY/file-types");
        Assert.Equal(0, body.GetProperty("logs").GetArrayLength());
        Assert.Equal(0, body.GetProperty("configurations").GetArrayLength());
    }

    [Fact]
    public async Task Quarantined_definitions_are_not_exposed_in_catalog()
    {
        factory.SetSnapshot(SnapshotWithQuarantinedDefinitions());

        var body = await GetAsync("/api/v1/equipments/EQ-001/file-types");

        Assert.Equal(["EventLog"], body.GetProperty("logs").EnumerateArray()
            .Select(item => item.GetProperty("logType").GetString()!).ToArray());
        Assert.Equal(["PM"], body.GetProperty("configurations").EnumerateArray()
            .Select(item => item.GetProperty("configurationType").GetString()!).ToArray());
    }
}
