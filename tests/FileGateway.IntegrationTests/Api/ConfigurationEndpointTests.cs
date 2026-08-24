using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.UnitTests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FileGateway.IntegrationTests.Api;

// factory: EQ-001/PM 정의 + FakeFileAccess 시드(current PM1/PM2, history 08-21 marker, 08-22 marker+PM1.cfg)
// xUnit이 테스트마다 ctor를 다시 실행하므로 RemoveFile로 인한 시드 변형이 다른 테스트로 새어나가지 않는다.
public class ConfigurationEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ConfigurationEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        factory.SetSnapshot(Snapshot());
        factory.UseFakeFtp(SeedFtp);
    }

    private static ReferenceDataSnapshot Snapshot() => ReferenceDataSnapshotBuilder.Build(new(
        ["EQ-001"],
        [new RawServer("SRV1", "ftp1", "ftproot")],
        [],
        [new RawConfigurationDefinition("EQ-001", "PM", "SRV1",
            "PM/current", "PM*.cfg",
            "PM/history/{yyyy}{MM}{dd}", "PM*.cfg",
            "PM/history/{yyyy}{MM}{dd}.marker")]));

    private static void SeedFtp(FakeFileAccess ftp)
    {
        ftp.AddFile("PM/current/PM1.cfg", "pm1-current"u8.ToArray());
        ftp.AddFile("PM/current/PM2.cfg", "pm2-current"u8.ToArray());
        ftp.AddFile("PM/history/20260821.marker", "marker-21"u8.ToArray()); // 미완료 Set: marker만 존재
        ftp.AddFile("PM/history/20260822.marker", "marker-22"u8.ToArray());
        ftp.AddFile("PM/history/20260822/PM1.cfg", "pm1-hist"u8.ToArray());
    }

    [Fact]
    public async Task Current_returns_plain_sorted_array()
    {
        using var response = await _factory.CreateClient()
            .GetAsync("/api/v1/configurations/current?equipmentId=EQ-001&configurationType=PM");
        var arr = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, arr.ValueKind); // envelope 아님
        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal("PM1.cfg", arr[0].GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task Current_empty_returns_empty_array_with_200()
    {
        _factory.Ftp.RemoveFile("PM/current/PM1.cfg"); _factory.Ftp.RemoveFile("PM/current/PM2.cfg");
        using var response = await _factory.CreateClient()
            .GetAsync("/api/v1/configurations/current?equipmentId=EQ-001&configurationType=PM");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }

    [Fact]
    public async Task Current_download_multiple_is_409()
    {
        using var response = await _factory.CreateClient()
            .GetAsync("/api/v1/configurations/current/download?equipmentId=EQ-001&configurationType=PM");
        Assert.Equal(409, (int)response.StatusCode);
        Assert.Equal("MultipleFilesMatched", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Current_download_single_streams()
    {
        _factory.Ftp.RemoveFile("PM/current/PM2.cfg");
        using var response = await _factory.CreateClient()
            .GetAsync("/api/v1/configurations/current/download?equipmentId=EQ-001&configurationType=PM");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.True(response.Content.Headers.ContentLength > 0);
    }

    [Fact]
    public async Task Current_download_audit_log_carries_fileId()
    {
        var logs = new CollectingLoggerProvider();
        using var factory = new ApiFactory(s => s.AddSingleton<ILoggerProvider>(logs));
        factory.SetSnapshot(Snapshot());
        factory.UseFakeFtp(SeedFtp);
        factory.Ftp.RemoveFile("PM/current/PM2.cfg");
        using var response = await factory.CreateClient()
            .GetAsync("/api/v1/configurations/current/download?equipmentId=EQ-001&configurationType=PM");
        Assert.Equal(200, (int)response.StatusCode);

        var entry = logs.Entries.Single(e => e.Category == "FileGateway.Audit");
        var fileId = Regex.Match(entry.Message, @"fileId (\S+) fileName").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(fileId), $"audit message missing fileId: {entry.Message}");
        var fileSize = Regex.Match(entry.Message, @"fileSize (\S+) status").Groups[1].Value;
        Assert.True(long.TryParse(fileSize, out var size) && size > 0,
            $"audit message missing positive fileSize: {entry.Message}");
    }

    [Fact]
    public async Task History_requires_from_and_to()
        => Assert.Equal("InvalidRequest", (await GetError("/api/v1/configurations/history?equipmentId=EQ-001&configurationType=PM")).code);

    [Fact]
    public async Task History_over_max_range_is_400()
        => Assert.Equal("InvalidRequest", (await GetError("/api/v1/configurations/history?equipmentId=EQ-001&configurationType=PM&from=2020-01-01T00:00:00%2B09:00&to=2030-01-01T00:00:00%2B09:00")).code);

    [Fact]
    public async Task History_returns_only_marked_sets_with_envelope()
    {
        var body = await GetJson("/api/v1/configurations/history?equipmentId=EQ-001&configurationType=PM&from=2026-08-20T00:00:00%2B09:00&to=2026-08-23T00:00:00%2B09:00");
        var items = body.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength()); // 08-22만 marker 존재
        Assert.Equal("PM1.cfg", items[0].GetProperty("fileName").GetString());
        Assert.NotNull(items[0].GetProperty("snapshotTimestamp").GetString());
    }

    [Fact]
    public async Task Unknown_type_is_404_ConfigurationDefinitionNotFound()
        => Assert.Equal("ConfigurationDefinitionNotFound",
            (await GetError("/api/v1/configurations/current?equipmentId=EQ-001&configurationType=NOPE")).code);

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
}
