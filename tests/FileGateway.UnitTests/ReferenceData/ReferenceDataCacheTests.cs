// tests/FileGateway.UnitTests/ReferenceData/ReferenceDataCacheTests.cs
using FileGateway.Core.Errors;
using FileGateway.Infrastructure.ReferenceData;

namespace FileGateway.UnitTests.ReferenceData;

public class ReferenceDataCacheTests
{
    private sealed class FakeSource(ReferenceDataRaw first) : IReferenceDataSource
    {
        public int Calls; public Func<Task<ReferenceDataRaw>>? Next;
        public Task<ReferenceDataRaw> ReadAsync(CancellationToken ct)
        { Calls++; return Next != null ? Next() : Task.FromResult(first); }
    }

    private static ReferenceDataRaw Raw(string equipment = "EQ-001") => new(
        [equipment], [new RawServer("SRV1", "h", "ftproot")], [], []);

    [Fact]
    public async Task First_load_failure_without_cache_throws_ReferenceDataUnavailable()
    {
        var src = new FakeSource(Raw()) { Next = () => throw new SqlExceptionSim() };
        var cache = new ReferenceDataCache(src, TimeSpan.FromMinutes(15));
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() => cache.GetSnapshotAsync(CancellationToken.None));
        Assert.Equal("ReferenceDataUnavailable", ex.Code);
    }

    [Fact]
    public async Task Concurrent_first_load_shares_single_refresh()
    {
        var src = new FakeSource(Raw()) { Next = async () => { await Task.Delay(200); return Raw(); } };
        var cache = new ReferenceDataCache(src, TimeSpan.FromMinutes(15));
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => cache.GetSnapshotAsync(CancellationToken.None)));
        Assert.Equal(1, src.Calls);
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public async Task Expired_cache_returns_stale_immediately_and_refreshes_in_background()
    {
        var v1 = Raw("EQ-A");
        var v2 = Raw("EQ-B");
        var src = new FakeSource(v1);
        // 최초 로딩은 v1, 이후 refresh부터 v2를 반환한다(Refresh_failure 테스트와 동일한 재무장 관례).
        src.Next = () => { src.Next = async () => { await Task.Delay(300); return v2; }; return Task.FromResult(v1); };
        var cache = new ReferenceDataCache(src, TimeSpan.FromMilliseconds(50));
        var first = await cache.GetSnapshotAsync(CancellationToken.None);

        await Task.Delay(100); // TTL 경과, refresh는 300ms 지연
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var second = await cache.GetSnapshotAsync(CancellationToken.None);
        sw.Stop();

        Assert.Same(first, second);                     // DB refresh를 기다리지 않고 stale 즉시 반환
        Assert.True(sw.ElapsedMilliseconds < 200);

        await Task.Delay(400);                           // background refresh 완료 대기
        Assert.Contains("EQ-B", cache.CurrentSnapshot!.EquipmentIds); // atomic 교체 확인
    }

    [Fact]
    public async Task Refresh_failure_keeps_last_known_good_stale_snapshot()
    {
        var good = Raw();
        var src = new FakeSource(good);
        src.Next = () => { src.Next = () => throw new SqlExceptionSim(); return Task.FromResult(good); };
        var cache = new ReferenceDataCache(src, TimeSpan.FromMilliseconds(50));
        var first = await cache.GetSnapshotAsync(CancellationToken.None);

        await Task.Delay(100); // TTL 경과
        var second = await cache.GetSnapshotAsync(CancellationToken.None);

        Assert.Same(first, second);          // atomic 교체 없이 동일 인스턴스
        Assert.True(cache.HasUsableSnapshot);
        Assert.NotNull(cache.LastRefreshError);
    }

    [Fact]
    public async Task Validation_failure_rejects_new_snapshot_entirely()
    {
        var good = Raw();
        var broken = new ReferenceDataRaw(["EQ-1", "EQ-1"], [], [], []); // 장비 중복
        var src = new FakeSource(good);
        // 최초 로딩은 good, 이후 refresh부터 broken을 반환한다.
        src.Next = () => { src.Next = () => Task.FromResult(broken); return Task.FromResult(good); };
        var cache = new ReferenceDataCache(src, TimeSpan.FromMilliseconds(50));
        var first = await cache.GetSnapshotAsync(CancellationToken.None);

        await Task.Delay(100);
        Assert.Same(first, await cache.GetSnapshotAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Successful_validation_swaps_atomically()
    {
        var v1 = Raw("EQ-A"); var v2 = Raw("EQ-B");
        var src = new FakeSource(v1);
        // 최초 로딩은 v1, 이후 refresh부터 v2를 반환한다.
        src.Next = () => { src.Next = () => Task.FromResult(v2); return Task.FromResult(v1); };
        var cache = new ReferenceDataCache(src, TimeSpan.FromMilliseconds(50));
        var first = await cache.GetSnapshotAsync(CancellationToken.None);

        await Task.Delay(100);
        var second = await cache.GetSnapshotAsync(CancellationToken.None);
        Assert.NotSame(first, second);
        Assert.Contains("EQ-B", second.EquipmentIds);
    }

    private sealed class SqlExceptionSim : Exception;
}
