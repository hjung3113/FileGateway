// src/FileGateway.Infrastructure/ReferenceData/ReferenceDataCache.cs
using System.Diagnostics;
using FileGateway.Core.Errors;
using Microsoft.Extensions.Logging;

namespace FileGateway.Infrastructure.ReferenceData;

public sealed class ReferenceDataCache(
    IReferenceDataSource source, TimeSpan ttl, ILogger<ReferenceDataCache>? logger = null) : IReferenceDataView
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
                var refresh = StartRefreshUnderLock();
                // 동기 완료 refresh가 이미 스냅샷을 교체했을 수 있다 — 반환 시점의 현재 값을 돌려준다(기존 동작).
                var current = CurrentSnapshot!;
                if (refresh is not null)
                {
                    refresh.Diagnostics.StaleOrLkgUsed = ReferenceEquals(current, snapshot);
                    _ = ObserveRefresh(refresh);
                }
                return current;
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
        var diagnostics = new LoadDiagnostics("initial");
        var load = LoadAsync(diagnostics);
        _inFlight = load;
        if (load.IsCompleted)
        {
            _inFlight = null; // 동기 완료 로딩도 즉시 재시도 허용
            LogLoadCompleted(diagnostics);
        }
        else _ = ClearInFlightWhenComplete(load, diagnostics);
        return load;
    }

    private async Task ClearInFlightWhenComplete(
        Task<ReferenceDataSnapshot> load, LoadDiagnostics diagnostics)
    {
        try { await load; } // 미관찰 예외 방지. 실패 상태 기록은 LoadAsync가 담당.
        catch { }
        lock (_gate) { if (ReferenceEquals(_inFlight, load)) _inFlight = null; }
        LogLoadCompleted(diagnostics);
    }

    // _gate 잠금 안에서만 호출한다. 완료 관찰은 호출자가 실제 반환 snapshot을 확정한 뒤 시작한다.
    private RefreshAttempt? StartRefreshUnderLock()
    {
        if (_inFlight is not null) return null; // single-flight
        var diagnostics = new LoadDiagnostics("refresh");
        var refresh = LoadAsync(diagnostics);
        _inFlight = refresh;
        return new RefreshAttempt(refresh, diagnostics);
    }

    private async Task ObserveRefresh(RefreshAttempt attempt)
    {
        try { await attempt.Load; }
        catch { /* 실패는 LoadAsync에서 상태로 기록됨. stale cache 유지. */ }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_inFlight, attempt.Load)) _inFlight = null; }
            LogLoadCompleted(attempt.Diagnostics);
        }
    }

    private async Task<ReferenceDataSnapshot> LoadAsync(LoadDiagnostics diagnostics)
    {
        var total = Stopwatch.StartNew();
        try
        {
            // background refresh의 ct는 호출자 요청 취소와 분리한다(CancellationToken.None) —
            // 취소된 요청이 cache 상태를 좌우하지 않게 한다.
            var read = Stopwatch.StartNew();
            try { diagnostics.Raw = await source.ReadAsync(CancellationToken.None); }
            finally { diagnostics.SpReadElapsedMs = read.ElapsedMilliseconds; }

            ReferenceDataSnapshot snapshot;
            var validation = Stopwatch.StartNew();
            try
            {
                snapshot = ReferenceDataSnapshotBuilder.Build(diagnostics.Raw, logger); // 전역 검증 실패 → 새 스냅샷 전체 거부
            }
            finally { diagnostics.ValidationBuildElapsedMs = validation.ElapsedMilliseconds; }

            lock (_gate)
            {
                CurrentSnapshot = snapshot;   // atomic 참조 교체
                _loadedAt = DateTimeOffset.UtcNow;
                LastRefreshError = null; LastRefreshFailedAt = null;
            }
            diagnostics.Success = true;
            return snapshot;
        }
        catch (Exception ex)
        {
            switch (ex)
            {
                case ReferenceDataValidationException validationException:
                    logger?.LogError(validationException,
                        "reference data refresh failed: global validation failure with {ValidationErrorCount} error(s)",
                        validationException.Errors.Count);
                    // 오류를 하나의 무제한 문자열로 합치면 오류가 많을 때 로그 sink의 크기 제한에 잘려
                    // 일부 원인이 관측 불가능해질 수 있다 — 오류마다 별도 로그 항목으로 남긴다(PR #38 리뷰 반영).
                    for (var i = 0; i < validationException.Errors.Count; i++)
                        logger?.LogError(
                            "reference data global validation error {ValidationErrorIndex}/{ValidationErrorCount}: {ValidationError}",
                            i + 1, validationException.Errors.Count, validationException.Errors[i]);
                    break;
                case FileGatewayException { Code: "ReferenceDataIncomplete" }:
                    logger?.LogError(ex,
                        "reference data refresh failed: SP shape failure: {ErrorMessage}", ex.Message);
                    break;
                default:
                    logger?.LogError(ex,
                        "reference data refresh failed: source read failure {ExceptionType}: {ErrorMessage}",
                        ex.GetType().Name, ex.Message);
                    break;
            }
            ReferenceDataSnapshot? lastKnownGood;
            lock (_gate)
            {
                LastRefreshFailedAt = DateTime.UtcNow;
                LastRefreshError = ex.Message;
                lastKnownGood = CurrentSnapshot;
            }
            if (lastKnownGood is not null) return lastKnownGood; // stale 유지
            throw new FileGatewayException("ReferenceDataUnavailable", "reference data unavailable");
        }
        finally
        {
            diagnostics.TotalElapsedMs = total.ElapsedMilliseconds;
        }
    }

    private void LogLoadCompleted(LoadDiagnostics diagnostics)
    {
        logger?.LogInformation(
            "reference data load completed: loadKind={LoadKind} spReadElapsedMs={SpReadElapsedMs} validationBuildElapsedMs={ValidationBuildElapsedMs} totalElapsedMs={TotalElapsedMs} equipmentRows={EquipmentRowCount} serverRows={ServerRowCount} logDefinitionRows={LogDefinitionRowCount} configurationDefinitionRows={ConfigurationDefinitionRowCount} success={Success} staleOrLkgUsed={StaleOrLkgUsed}",
            diagnostics.LoadKind,
            diagnostics.SpReadElapsedMs,
            diagnostics.ValidationBuildElapsedMs,
            diagnostics.TotalElapsedMs,
            diagnostics.Raw?.EquipmentIds.Count,
            diagnostics.Raw?.Servers.Count,
            diagnostics.Raw?.LogDefinitions.Count,
            diagnostics.Raw?.ConfigurationDefinitions.Count,
            diagnostics.Success,
            diagnostics.StaleOrLkgUsed);
    }

    private sealed class LoadDiagnostics(string loadKind)
    {
        public string LoadKind { get; } = loadKind;
        public long SpReadElapsedMs { get; set; }
        public long ValidationBuildElapsedMs { get; set; }
        public long TotalElapsedMs { get; set; }
        public ReferenceDataRaw? Raw { get; set; }
        public bool Success { get; set; }
        public bool StaleOrLkgUsed { get; set; }
    }

    private sealed record RefreshAttempt(
        Task<ReferenceDataSnapshot> Load,
        LoadDiagnostics Diagnostics);
}
