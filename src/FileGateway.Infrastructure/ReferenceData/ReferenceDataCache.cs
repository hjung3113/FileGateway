// src/FileGateway.Infrastructure/ReferenceData/ReferenceDataCache.cs
using FileGateway.Core.Errors;

namespace FileGateway.Infrastructure.ReferenceData;

public sealed class ReferenceDataCache(IReferenceDataSource source, TimeSpan ttl) : IReferenceDataView
{
    private readonly object _gate = new();
    private Task<ReferenceDataSnapshot>? _inFlight;
    private DateTimeOffset _loadedAt;

    public ReferenceDataSnapshot? CurrentSnapshot { get; private set; }
    public bool HasUsableSnapshot => CurrentSnapshot is not null;
    public DateTimeOffset? LastGoodRefreshAt => HasUsableSnapshot ? _loadedAt : null;
    public DateTime? LastRefreshFailedAt { get; private set; }
    public string? LastRefreshError { get; private set; }

    public async Task<ReferenceDataSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        Task<ReferenceDataSnapshot> load;
        lock (_gate)
        {
            var snapshot = CurrentSnapshot;
            if (snapshot is not null)
            {
                if (DateTimeOffset.UtcNow - _loadedAt < ttl)
                    return snapshot;
                // TTL 만료: single-flight background refresh 촉발 후 stale 즉시 반환.
                // 요청이 DB를 기다리지 않는다(확정 결정 14, 리뷰 P1 반영).
                _ = TriggerRefresh();
                // 동기 완료 refresh가 이미 스냅샷을 교체했을 수 있다 — 반환 시점의 현재 값을 돌려준다(기존 동작).
                return CurrentSnapshot!;
            }
            // 최초 로딩: 동시 요청이 동일 공유 로딩을 await
            load = _inFlight ?? StartLoadUnderLock();
        }
        // 공유 load 자체는 취소하지 않는다(다른 대기 호출자와 cache 상태 보존).
        // 이 호출자는 자기 ct로만 대기를 관찰한다 — 취소 시 이 await만 실패하고 load는 계속된다.
        return await load.WaitAsync(ct);
    }

    // _gate 잠금 안에서만 호출한다. LoadAsync 내부에서 _inFlight를 해제하면
    // 동기 완료 시 `_inFlight ??= ...` 배정보다 먼저 해제되어 영구 non-null이 되므로
    // 시작과 해제를 모두 배정 이후로 통제한다(해제는 identity 검사로).
    private Task<ReferenceDataSnapshot> StartLoadUnderLock()
    {
        var load = LoadAsync();
        _inFlight = load;
        if (load.IsCompleted) _inFlight = null; // 동기 완료 로딩도 즉시 재시도 허용
        else _ = ClearInFlightWhenComplete(load);
        return load;
    }

    private async Task ClearInFlightWhenComplete(Task<ReferenceDataSnapshot> load)
    {
        try { await load; } // 미관찰 예외 방지. 실패 상태 기록은 LoadAsync가 담당.
        catch { }
        lock (_gate) { if (ReferenceEquals(_inFlight, load)) _inFlight = null; }
    }

    private async Task TriggerRefresh()
    {
        Task<ReferenceDataSnapshot> refresh;
        lock (_gate)
        {
            if (_inFlight is not null) return; // single-flight
            refresh = LoadAsync();
            _inFlight = refresh;
        }
        try { await refresh; }
        catch { /* 실패는 LoadAsync에서 상태로 기록됨. stale cache 유지. */ }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_inFlight, refresh)) _inFlight = null; }
        }
    }

    private async Task<ReferenceDataSnapshot> LoadAsync()
    {
        try
        {
            // background refresh의 ct는 호출자 요청 취소와 분리한다(CancellationToken.None) —
            // 취소된 요청이 cache 상태를 좌우하지 않게 한다.
            var raw = await source.ReadAsync(CancellationToken.None);
            var snapshot = ReferenceDataSnapshotBuilder.Build(raw); // 검증 실패 → 새 스냅샷 전체 거부
            lock (_gate)
            {
                CurrentSnapshot = snapshot;   // atomic 참조 교체
                _loadedAt = DateTimeOffset.UtcNow;
                LastRefreshError = null; LastRefreshFailedAt = null;
                return snapshot;
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                LastRefreshFailedAt = DateTime.UtcNow;
                LastRefreshError = ex.Message;
                if (CurrentSnapshot is not null) return CurrentSnapshot; // stale 유지
                throw new FileGatewayException("ReferenceDataUnavailable", "reference data unavailable");
            }
        }
    }
}
