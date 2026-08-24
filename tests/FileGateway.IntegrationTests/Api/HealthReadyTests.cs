using System.Net.Http.Json;
using System.Text.Json;
using FileGateway.Infrastructure.ReferenceData;
using Microsoft.Extensions.DependencyInjection;

namespace FileGateway.IntegrationTests.Api;

/// <summary>/health/ready가 background refresh 실패 후 stale last-known-good 서빙 중임을 Degraded/stale=true로 보고하는지 검증.</summary>
public class HealthReadyTests
{
    private sealed class FlakySource(ReferenceDataRaw good) : IReferenceDataSource
    {
        public volatile bool Fail;
        public Task<ReferenceDataRaw> ReadAsync(CancellationToken ct)
            => Fail ? throw new Exception("db down") : Task.FromResult(good);
    }

    private static ReferenceDataRaw Raw() => new(
        ["EQ-001"], [new RawServer("SRV1", "h", "ftproot")], [], []);

    [Fact]
    public async Task Ready_reports_Degraded_stale_when_last_refresh_failed_but_snapshot_remains()
    {
        var src = new FlakySource(Raw());
        var cache = new ReferenceDataCache(src, TimeSpan.FromMilliseconds(50));
        using var factory = new ApiFactory(s => s.AddSingleton<IReferenceDataView>(cache));
        using var client = factory.CreateClient();

        // 최초 로딩 성공 → Healthy/stale=false
        using (var ok = await client.GetAsync("/health/ready"))
        {
            ok.EnsureSuccessStatusCode();
            var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Healthy", body.GetProperty("status").GetString());
            Assert.False(body.GetProperty("stale").GetBoolean());
        }

        // TTL 경과 + refresh 실패 유도: cache 조회로 실패한 refresh를 확정 반영시킨 뒤 ready 확인.
        src.Fail = true;
        await Task.Delay(100);
        await cache.GetSnapshotAsync(CancellationToken.None); // stale 반환 + background refresh 실패 기록
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (cache.LastRefreshFailedAt is null && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.NotNull(cache.LastRefreshFailedAt);

        using var res = await client.GetAsync("/health/ready");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Degraded", json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("stale").GetBoolean());
    }
}
