using System.Net.Http.Json;
using System.Text.Json;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.UnitTests.TestUtils;

namespace FileGateway.IntegrationTests.Api;

public class LogEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    // factory: snapshot(EQ-001/EventLog Hourly flat + TraceLog Continuous), FakeFileAccess 기반 IFileAccess 등록
    // FakeFileAccess 시드: Logs/all/2026082218_Event.zip 등
    public LogEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        factory.SetSnapshot(Snapshot());
        factory.SetFileAccess(FakeFtp());
    }

    private static ReferenceDataSnapshot Snapshot() => ReferenceDataSnapshotBuilder.Build(new(
        ["EQ-001"],
        [new RawServer("SRV1", "ftp1", "ftproot")],
        [
            new RawLogDefinition("EQ-001", "EventLog", "SRV1", "Hourly",
                "Logs/all", "*_Event*.zip", "Multiple", "Template",
                "Logs/all/{yyyy}{MM}{dd}{HH}_Event.zip", "[]"),
            new RawLogDefinition("EQ-001", "TraceLog", "SRV1", "Continuous",
                "Trace/current", "Trace_*.zip", "Multiple", "Template",
                "Trace/current/Trace_{subtype}.zip", "[]"),
        ],
        []));

    private static FakeFileAccess FakeFtp()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "event-18"u8.ToArray());
        ftp.AddFile("Logs/all/2026082217_Event.zip", "event-17"u8.ToArray());
        ftp.AddFile("Logs/all/2026082210_Event.zip", "event-10"u8.ToArray());
        ftp.AddFile("Trace/current/Trace_alpha.zip", "trace-alpha"u8.ToArray());
        return ftp;
    }

    private async Task<JsonElement> GetJson(string path)
    {
        using var response = await _factory.CreateClient().GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(string code, int status)> GetError(string path)
    {
        using var response = await _factory.CreateClient().GetAsync(path);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("code").GetString()!, (int)response.StatusCode);
    }

    [Fact]
    public async Task List_returns_envelope_with_camel_case_fields()
    {
        var body = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog");
        Assert.True(body.GetProperty("items").GetArrayLength() >= 1);
        var item = body.GetProperty("items")[0];
        foreach (var field in new[] { "fileId", "fileName", "equipmentId", "logType", "subtype", "timestamp", "size", "isContinuous", "attributes" })
            Assert.True(item.TryGetProperty(field, out _), $"missing {field}");
    }

    [Fact]
    public async Task Empty_result_is_items_empty_token_null()
    {
        var body = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&from=2020-01-01T00:00:00%2B09:00&to=2020-01-02T00:00:00%2B09:00");
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
        Assert.Null(body.GetProperty("continuationToken").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("continuationToken").ValueKind);
    }

    [Fact]
    public async Task Missing_required_params_is_400_InvalidRequest()
    {
        using var response = await _factory.CreateClient().GetAsync("/api/v1/logs?equipmentId=EQ-001");
        Assert.Equal(400, (int)response.StatusCode);
        Assert.Equal("InvalidRequest", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Continuous_with_from_is_400()
        => Assert.Equal("InvalidRequest", (await GetError("/api/v1/logs?equipmentId=EQ-001&logType=TraceLog&from=2026-08-22T00:00:00%2B09:00")).code);

    [Fact]
    public async Task Limit_above_max_is_400()
        => Assert.Equal("InvalidRequest", (await GetError($"/api/v1/logs?equipmentId=EQ-001&logType=EventLog&limit={1001}")).code);

    [Fact]
    public async Task Bad_timestamp_is_400()
        => Assert.Equal("InvalidRequest", (await GetError("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&from=yesterday")).code);

    [Fact]
    public async Task Pagination_walks_pages_and_allows_limit_change()
    {
        var p1 = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&limit=1");
        var token = p1.GetProperty("continuationToken").GetString();
        Assert.NotNull(token);
        var p2 = await GetJson($"/api/v1/logs?equipmentId=EQ-001&logType=EventLog&limit=2&continuationToken={Uri.EscapeDataString(token!)}");
        Assert.True(p2.GetProperty("items").GetArrayLength() <= 2);
    }

    [Fact]
    public async Task Continuation_with_changed_condition_is_400()
    {
        var p1 = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&limit=1");
        var token = Uri.EscapeDataString(p1.GetProperty("continuationToken").GetString()!);
        var error = await GetError($"/api/v1/logs?equipmentId=EQ-001&logType=EventLog&subtype=X&continuationToken={token}");
        Assert.Equal("InvalidRequest", error.code);
    }

    [Fact]
    public async Task Download_single_match_streams_with_headers()
    {
        using var response = await _factory.CreateClient()
            .GetAsync("/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog&from=2026-08-22T18:00:00%2B09:00&to=2026-08-22T19:00:00%2B09:00");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition.DispositionType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(response.Content.Headers.ContentLength, bytes.Length);
    }

    [Fact]
    public async Task Download_multiple_match_is_409()
    {
        var error = await GetError("/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog");
        Assert.Equal(409, error.status);
        Assert.Equal("MultipleFilesMatched", error.code);
    }

    [Fact]
    public async Task Download_no_match_is_404_FileNotFound()
    {
        var error = await GetError("/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog&from=2020-01-01T00:00:00%2B09:00&to=2020-01-02T00:00:00%2B09:00");
        Assert.Equal("FileNotFound", error.code);
    }

    [Fact]
    public async Task Error_body_has_no_physical_path()
    {
        using var response = await _factory.CreateClient()
            .GetAsync("/api/v1/logs?equipmentId=EQ-001&logType=Nope");
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ftp1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ftproot", json, StringComparison.Ordinal);
    }
}
