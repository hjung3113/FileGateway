using System.Net.Http.Json;
using System.Text.Json;
using FileGateway.Api.Options;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.UnitTests.TestUtils;
using Microsoft.Extensions.DependencyInjection;

namespace FileGateway.IntegrationTests.Api;

// factory: EQ-001 EventLog(Hourly) + PM configuration 정의를 함께 시드 — fileId 라우팅이 두 feature를 모두 거친다.
public class FileEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public FileEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        factory.SetSnapshot(Snapshot());
        factory.UseFakeFtp(SeedFtp);
    }

    private static ReferenceDataSnapshot Snapshot() => ReferenceDataSnapshotBuilder.Build(new(
        ["EQ-001"],
        [new RawServer("SRV1", "ftp1", "ftproot")],
        [
            new RawLogDefinition("EQ-001", "EventLog", "SRV1", "Hourly",
                "Logs/all", "*_Event*.zip", "Multiple", "Template",
                "Logs/all/{yyyy}{MM}{dd}{HH}_Event.zip", "[]"),
        ],
        [new RawConfigurationDefinition("EQ-001", "PM", "SRV1",
            "PM/current", "PM*.cfg",
            "PM/history/{yyyy}{MM}{dd}", "PM*.cfg",
            "PM/history/{yyyy}{MM}{dd}.marker")]));

    private static void SeedFtp(FakeFileAccess ftp)
    {
        ftp.AddFile("Logs/all/2026082218_Event.zip", "event-18"u8.ToArray());
        ftp.AddFile("Logs/all/2026082217_Event.zip", "event-17"u8.ToArray());
        ftp.AddFile("PM/history/20260822.marker", "marker-22"u8.ToArray());
        ftp.AddFile("PM/history/20260822/PM1.cfg", "pm1-hist"u8.ToArray());
    }

    private async Task<string> GetFileIdAsync(ApiFactory? factory = null) // 로그 목록에서 fileId 확보
    {
        var body = await GetJson(factory ?? _factory, "/api/v1/logs?equipmentId=EQ-001&logType=EventLog");
        return body.GetProperty("items")[0].GetProperty("fileId").GetString()!;
    }

    [Fact]
    public async Task Metadata_returns_minimal_fields_only()
    {
        var fileId = await GetFileIdAsync();
        var body = await GetJson(_factory, $"/api/v1/files/{Uri.EscapeDataString(fileId)}");
        Assert.Equal(3, body.EnumerateObject().Count()); // fileId/fileName/size 만
        Assert.Equal(fileId, body.GetProperty("fileId").GetString());
        Assert.True(body.GetProperty("size").GetInt64() >= 0);
    }

    [Fact]
    public async Task Download_streams_with_content_length()
    {
        var fileId = await GetFileIdAsync();
        using var response = await _factory.CreateClient()
            .GetAsync($"/api/v1/files/{Uri.EscapeDataString(fileId)}/download");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.True(response.Content.Headers.ContentLength > 0);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(response.Content.Headers.ContentLength.Value, bytes.Length);
    }

    [Fact]
    public async Task Garbage_file_id_is_400_InvalidFileId()
    {
        var error = await GetError("/api/v1/files/garbage");
        Assert.Equal(400, error.status);
        Assert.Equal("InvalidFileId", error.code);
    }

    [Fact]
    public async Task Expired_file_id_is_410()
    {
        // FileIdTtl은 서비스 singleton 생성 시 고정되므로, 단시간 TTL로 별도 factory를 띄워 만료 토큰을 발급한다.
        using var factory = new ApiFactory(s => s.Configure<FileGatewayOptions>(
            o => o.Tokens.FileIdTtl = TimeSpan.FromMilliseconds(1)));
        factory.SetSnapshot(Snapshot());
        factory.UseFakeFtp(SeedFtp);
        var expiredId = await GetFileIdAsync(factory);
        var error = await GetError(factory, $"/api/v1/files/{Uri.EscapeDataString(expiredId)}");
        Assert.Equal(410, error.status);
        Assert.Equal("FileIdExpired", error.code);
    }

    [Fact]
    public async Task Deleted_logical_file_is_404_FileNotFound()
    {
        var fileId = await GetFileIdAsync();
        _factory.Ftp.RemoveFile("Logs/all/2026082218_Event.zip");
        var error = await GetError($"/api/v1/files/{Uri.EscapeDataString(fileId)}");
        Assert.Equal(404, error.status);
        Assert.Equal("FileNotFound", error.code);
    }

    [Fact]
    public async Task Snapshot_fileId_rechecks_marker()
    {
        var history = await GetJson(_factory, "/api/v1/configurations/history?equipmentId=EQ-001&configurationType=PM&from=2026-08-22T00:00:00%2B09:00&to=2026-08-23T00:00:00%2B09:00");
        var snapshotFileId = history.GetProperty("items")[0].GetProperty("fileId").GetString()!;
        _factory.Ftp.RemoveFile("PM/history/20260822.marker"); // marker 제거, 파일은 잔존
        var error = await GetError($"/api/v1/files/{Uri.EscapeDataString(snapshotFileId)}");
        Assert.Equal("FileNotFound", error.code);
    }

    [Fact]
    public async Task No_head_endpoint_exists()
    {
        var fileId = await GetFileIdAsync();
        using var response = await _factory.CreateClient()
            .SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/api/v1/files/{Uri.EscapeDataString(fileId)}"));
        Assert.Equal(405, (int)response.StatusCode); // MapGet만 존재
    }

    [Fact]
    public async Task Truncated_during_transfer_aborts_response()
    {
        var fileId = await GetFileIdAsync();
        _factory.Ftp.TruncateAfterOpen("Logs/all/2026082218_Event.zip", bytesToKeep: 1);
        using var response = await _factory.CreateClient()
            .GetAsync($"/api/v1/files/{Uri.EscapeDataString(fileId)}/download", HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        // 선언 길이보다 짧게 끝남 → 본문이 Content-Length 미달로 종료(TestServer in-memory 전송은
        // 연결 reset 대신 조기 EOF로 관찰된다) 또는 예외
        using var sink = new MemoryStream();
        try { await stream.CopyToAsync(sink); }
        catch { return; } // 소켓 전송에서는 reset이 IOException으로 관찰된다
        Assert.True(sink.Length < response.Content.Headers.ContentLength,
            $"body completed at declared length {response.Content.Headers.ContentLength}");
    }

    private async Task<JsonElement> GetJson(ApiFactory factory, string path)
    {
        using var response = await factory.CreateClient().GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<(string code, int status)> GetError(string path) => GetError(_factory, path);

    private async Task<(string code, int status)> GetError(ApiFactory factory, string path)
    {
        using var response = await factory.CreateClient().GetAsync(path);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("code").GetString()!, (int)response.StatusCode);
    }
}
