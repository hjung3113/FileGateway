using System.Globalization;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Queries;
using FileGateway.Core.Time;
using FileGateway.Core.Tokens;
using FileGateway.Infrastructure.Diagnostics;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.Logs.Definitions;
using FileGateway.Logs.Internal;
using FileGateway.Logs.Tokens;

namespace FileGateway.Logs;
// Infrastructure에 둔 이유: ReferenceDataView(Task 7)를 소비하는 조립 지점은 물리 의존 방향이
// Infrastructure→Logs여야 해서다. 네임스페이스는 도메인(FileGateway.Logs)을 유지해
// 소비자(Api/DI)가 프로젝트 경계를 의식하지 않게 한다.
/// <summary>Resolver+cursor+fileId 조립. DI 진입점 — TimeProvider.System으로 등록한다.</summary>
public sealed class LogQueryService(
    IReferenceDataView referenceData,
    IFileAccess fileAccess,
    ITokenCodec tokens,
    TimeProvider clock,
    TimeSpan maxQueryRange,
    int limitDefault,
    int limitMaximum,
    TimeSpan fileTtl,
    TimeSpan pageTtl,
    IFileAccessFailureLogger failureLogger) : ILogQueryService
{
    public async Task<PagedResult<LogFileDescriptor>> ListAsync(LogListQuery query, CancellationToken ct)
    {
        var core = await ListCoreAsync(query, ct);
        return new(core.Page.Select(f => ToDescriptor(core.Def, f, core.Query)).ToList(), core.Next);
    }

    public async Task<IReadOnlyList<LocatedLogFile>> ListLocatedAsync(LogListQuery query, CancellationToken ct)
    {
        // 목록이 이미 확정한 매치를 그대로 사용한다. 파일별 재탐색(원격 listing)을 추가하지 않는다.
        var core = await ListCoreAsync(query, ct);
        return core.Page.Select(f => new LocatedLogFile(
            ToDescriptor(core.Def, f, core.Query),
            new LocatedFile(core.Def.Server, f.RelativePath, f.Entry.Name, f.Entry.Size))).ToList();
    }

    private sealed record ListCore(
        ResolvedLogDefinition Def, LogListQuery Query, IReadOnlyList<ResolvedLogFile> Page, string? Next);

    private async Task<ListCore> ListCoreAsync(LogListQuery query, CancellationToken ct)
    {
        if (query.Limit is < 1)
            throw new FileGatewayException("InvalidRequest", "limit must be at least 1");
        if (query.Limit is int logLimit && logLimit > limitMaximum)
            throw new FileGatewayException("InvalidRequest", $"limit exceeds maximum of {limitMaximum}");

        query = Normalize(query);

        // 커서 검증(복호화+바인딩)은 탐색 전에: 잘못된 토큰은 resolver/FTP 워크 없이 즉시 거부한다.
        (DateTimeOffset? LastTs, string? LastName, EffectiveRange Range)? cursor = null;
        if (query.ContinuationToken is not null)
        {
            LogCursor.AssertBinding(tokens, query.ContinuationToken, query);
            cursor = LogCursor.Decode(tokens, query.ContinuationToken);
        }

        var def = await FindDefinitionAsync(query.EquipmentId, query.LogType, ct);
        // continuation은 첫 페이지의 effective range를 토큰에서 재사용한다(from/to==null 기본 2일의
        // 재계산 금지). 첫 페이지에서만 Normalize가 range를 결정하고 이후 페이지는 그 값을 고정한다.
        var range = cursor?.Range
            ?? EffectiveRangePlanner.Normalize(query, def.Definition.GenerationType, maxQueryRange, clock);
        var files = ApplyFilters(await ResolveAsync(def, range, ct), query);
        if (cursor is not null)
            files = SkipUntilAfter(files, def.Definition.GenerationType, cursor.Value.LastTs, cursor.Value.LastName);

        var limit = query.Limit ?? limitDefault;
        var page = files.Take(limit).ToList();
        string? next = null;
        if (files.Count > limit)
        {
            var last = page[^1];
            var lastTs = def.Definition.GenerationType == GenerationType.Continuous
                ? null : last.Metadata.Timestamp;
            next = LogCursor.Encode(tokens, clock, query, lastTs, last.Entry.Name, range, pageTtl);
        }
        return new(def, query, page, next);
    }

    public async Task<SingleFileMatch> ResolveSingleAsync(LogListQuery query, CancellationToken ct)
    {
        query = Normalize(query);
        var def = await FindDefinitionAsync(query.EquipmentId, query.LogType, ct);
        var range = EffectiveRangePlanner.Normalize(query, def.Definition.GenerationType, maxQueryRange, clock);
        var files = ApplyFilters(await ResolveAsync(def, range, ct), query);
        return files.Count switch
        {
            0 => new(null, MatchCount.Zero),
            1 => new(new LocatedFile(def.Server, files[0].RelativePath, files[0].Entry.Name, files[0].Entry.Size),
                MatchCount.One, MintFileId(files[0], query)),
            _ => new(null, MatchCount.Many),
        };
    }

    public async Task<LocatedFile> LocateByFileIdAsync(TokenPayload payload, CancellationToken ct)
    {
        if (payload.Purpose != LogTokenKinds.FileIdPurpose)
            throw new FileGatewayException("InvalidFileId", "not a log file id");
        var equipmentId = payload.Claims.GetValueOrDefault("equipmentId");
        var logType = payload.Claims.GetValueOrDefault("logType");
        var fileName = payload.Claims.GetValueOrDefault("fileName");
        if (equipmentId is null || logType is null || fileName is null)
            throw new FileGatewayException("InvalidFileId", "incomplete log file id claims");
        var ts = payload.Claims.TryGetValue("ts", out var t) && t.Length > 0
            ? DateTimeOffset.Parse(t, CultureInfo.InvariantCulture) : (DateTimeOffset?)null;

        var def = await FindDefinitionAsync(equipmentId, logType, ct);
        // ts가 있으면 해당 슬롯만 재탐색, 없으면(Continuous) 전체 범위
        var range = ts is null
            ? new EffectiveRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
            : new EffectiveRange(ts.Value, ts.Value.AddSeconds(1));
        var files = await ResolveAsync(def, range, ct);
        var match = files.SingleOrDefault(f => FileNameComparison.Same(f.Entry.Name, fileName))
            ?? throw new FileGatewayException("FileNotFound", "logical file no longer exists");
        return new(def.Server, match.RelativePath, match.Entry.Name, match.Entry.Size);
    }

    private async Task<ResolvedLogDefinition> FindDefinitionAsync(
        string equipmentId, string logType, CancellationToken ct)
    {
        var snapshot = await referenceData.GetSnapshotAsync(ct);
        return snapshot.FindLog(equipmentId, logType)
            ?? throw new FileGatewayException("LogDefinitionNotFound", $"no log definition for {equipmentId}/{logType}");
    }

    // 한 요청이 DB에 기록할 미스 진단 로그의 상한. 넓은 범위(Hourly 31일 = 744슬롯)에서
    // 무제한 insert가 몰리는 것을 막는다 — 초과분은 진단 편의를 위해 버린다.
    private const int MaxMissLogsPerRequest = 20;

    private async Task<IReadOnlyList<ResolvedLogFile>> ResolveAsync(
        ResolvedLogDefinition def, EffectiveRange range, CancellationToken ct)
    {
        var result = await new LogResolver(fileAccess).ResolveAsync(def, range, ct);
        // 미스 진단 로그는 부가 기능 — 미스마다 순차 await하면 요청 latency가 DB 왕복 횟수에
        // 비례하므로 fire-and-forget으로 전환한다. 원 요청의 ct는 응답 완료와 함께 취소되므로
        // CancellationToken.None을 쓴다.
        foreach (var miss in result.Misses.Take(MaxMissLogsPerRequest))
            _ = LogMissInBackgroundAsync(def, miss);
        return result.Files;
    }

    // fire-and-forget 태스크의 예외는 절대 상위로 전파되지 않는다(SpFileAccessFailureLogger가
    // 자체 삼킴 + 여기서도 한 번 더 방어).
    private async Task LogMissInBackgroundAsync(ResolvedLogDefinition def, DeterministicMiss miss)
    {
        try
        {
            await failureLogger.LogAsync(def.Definition.EquipmentId, def.Definition.LogType,
                def.Definition.ServerId, miss.Slot, miss.RelativePath, miss.Reason, CancellationToken.None);
        }
        catch
        {
            // 진단 로그 실패는 원래 요청 응답에 영향을 주지 않는다.
        }
    }

    private LogFileDescriptor ToDescriptor(ResolvedLogDefinition def, ResolvedLogFile f, LogListQuery q)
    {
        var fileId = MintFileId(f, q);
        return new(fileId, q.EquipmentId, q.LogType, f.Metadata.Subtype, f.Metadata.Timestamp,
            f.Entry.Name, f.Entry.Size, def.Definition.GenerationType == GenerationType.Continuous,
            f.Metadata.Attributes);
    }

    // fileId 발급(목록/단일 다운로드 공통): LocateByFileIdAsync가 재해석 가능한 동일 claim 구조.
    private string MintFileId(ResolvedLogFile f, LogListQuery q)
    {
        var claims = new Dictionary<string, string>
        {
            ["equipmentId"] = q.EquipmentId,
            ["logType"] = q.LogType,
            ["ts"] = f.Metadata.Timestamp?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            ["fileName"] = f.Entry.Name,
        };
        return tokens.Protect(new TokenPayload(LogTokenKinds.FileIdPurpose, claims,
            clock.GetUtcNow(), fileTtl));
    }

    // 진입부 1회 정규화: 빈 subtype는 미지정과 같은 의미로 바인딩(Canonical)·필터(ApplyFilters)
    // 모두에 적용한다(null=필터 없음, ""는 미지정과 동일 취급).
    private static LogListQuery Normalize(LogListQuery q)
        => q.Subtype is { Length: 0 } ? q with { Subtype = null } : q;

    // subtype/attribute 필터는 case-sensitive 정확 일치
    private static IReadOnlyList<ResolvedLogFile> ApplyFilters(IReadOnlyList<ResolvedLogFile> files, LogListQuery q)
    {
        IEnumerable<ResolvedLogFile> filtered = files;
        if (q.Subtype is not null)
            filtered = filtered.Where(f => string.Equals(f.Metadata.Subtype, q.Subtype, StringComparison.Ordinal));
        if (q.Attributes.Count > 0)
            filtered = filtered.Where(f => q.Attributes.All(kv =>
                f.Metadata.Attributes.TryGetValue(kv.Key, out var v) &&
                string.Equals(v, kv.Value, StringComparison.Ordinal)));
        return filtered.ToList();
    }

    // Hourly/Daily(timestamp desc, fileName asc 정렬): ts가 커서보다 새 파일과
    // 동일 ts에서 커서 파일까지(이름 순)를 건너뛴다. Continuous는 이름 순서만 사용.
    private static IReadOnlyList<ResolvedLogFile> SkipUntilAfter(
        IReadOnlyList<ResolvedLogFile> files, GenerationType type,
        DateTimeOffset? lastTs, string? lastName)
    {
        if (type == GenerationType.Continuous)
            return lastName is null
                ? files
                : files.SkipWhile(f => FileNameComparison.Compare(f.Entry.Name, lastName) <= 0).ToList();
        if (lastTs is null) return files;
        return files.SkipWhile(f => f.Metadata.Timestamp is { } ts && AtOrBeforeCursor(ts, f.Entry.Name, lastTs.Value, lastName))
            .ToList();
    }

    private static bool AtOrBeforeCursor(DateTimeOffset ts, string name, DateTimeOffset lastTs, string? lastName)
        => ts > lastTs
           || (ts == lastTs && lastName is not null && FileNameComparison.Compare(name, lastName) <= 0);
}
