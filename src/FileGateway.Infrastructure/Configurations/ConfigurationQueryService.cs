using System.Globalization;
using FileGateway.Configurations.Definitions;
using FileGateway.Configurations.Internal;
using FileGateway.Configurations.Tokens;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Queries;
using FileGateway.Core.Tokens;
using FileGateway.Core.Time;
using FileGateway.Infrastructure.ReferenceData;

namespace FileGateway.Configurations;
// Infrastructure에 둔 이유: ReferenceDataView(Task 7)를 소비하는 조립 지점은 물리 의존 방향이
// Infrastructure→Configurations여야 해서다(LogQueryService와 동일한 판정). 네임스페이스는
// 도메인(FileGateway.Configurations)을 유지해 소비자(Api/DI)가 프로젝트 경계를 의식하지 않게 한다.
/// <summary>Current/History resolver + cursor + fileId 조립. DI 진입점 — TimeProvider.System으로 등록한다.</summary>
public sealed class ConfigurationQueryService(
    IReferenceDataView referenceData,
    CurrentResolver currentResolver,
    HistoryResolver historyResolver,
    IFileAccess fileAccess,
    ITokenCodec tokens,
    TimeProvider clock,
    TimeSpan historyMaxQueryRange,
    int limitDefault,
    int limitMaximum,
    TimeSpan fileTtl,
    TimeSpan pageTtl) : IConfigurationQueryService
{
    public async Task<IReadOnlyList<ConfigurationItem>> GetCurrentAsync(
        string equipmentId, string configurationType, CancellationToken ct)
    {
        var def = await FindDefinitionAsync(equipmentId, configurationType, ct);
        var files = await currentResolver.ResolveAsync(def, ct);
        return files.Select(f => new ConfigurationItem(
            IssueFileId(ConfigurationTokenKinds.FileIdCurrentPurpose, equipmentId, configurationType, null, f.Entry.Name),
            equipmentId, configurationType, f.Entry.Name, f.Entry.Size)).ToList();
    }

    public async Task<SingleFileMatch> ResolveCurrentSingleAsync(
        string equipmentId, string configurationType, CancellationToken ct)
    {
        var def = await FindDefinitionAsync(equipmentId, configurationType, ct);
        var files = await currentResolver.ResolveAsync(def, ct);
        return files.Count switch
        {
            0 => new(null, MatchCount.Zero),
            1 => new(new LocatedFile(def.Server, files[0].RelativePath, files[0].Entry.Name, files[0].Entry.Size),
                MatchCount.One,
                IssueFileId(ConfigurationTokenKinds.FileIdCurrentPurpose,
                    equipmentId, configurationType, null, files[0].Entry.Name)),
            _ => new(null, MatchCount.Many),
        };
    }

    public async Task<PagedResult<ConfigurationHistoryItem>> GetHistoryAsync(ConfigurationHistoryQuery q, CancellationToken ct)
    {
        if (q.Limit is < 1)
            throw new FileGatewayException("InvalidRequest", "limit must be at least 1");
        if (q.Limit is int cfgLimit && cfgLimit > limitMaximum)
            throw new FileGatewayException("InvalidRequest", $"limit exceeds maximum of {limitMaximum}");
        // from/to 필수·범위 검증은 Api 파싱 후에도 서비스가 방어한다(Global Constraints).
        if (q.From >= q.To)
            throw new FileGatewayException("InvalidRequest", "from must be before to");
        if (q.To - q.From > historyMaxQueryRange)
            throw new FileGatewayException("InvalidRequest",
                $"history range exceeds maximum of {historyMaxQueryRange.TotalDays} days");

        // 커서 검증(복호화+바인딩)은 탐색 전에: 잘못된 토큰은 resolver/FTP 워크 없이 즉시 거부한다.
        (DateTimeOffset LastTs, string? LastName)? cursor = null;
        if (q.ContinuationToken is not null)
        {
            HistoryCursor.AssertBinding(tokens, q.ContinuationToken, q);
            cursor = HistoryCursor.Decode(tokens, q.ContinuationToken);
        }

        var def = await FindDefinitionAsync(q.EquipmentId, q.ConfigurationType, ct);
        var files = await historyResolver.ResolveAsync(def, new EffectiveRange(q.From, q.To), ct);
        if (cursor is not null)
            files = SkipUntilAfter(files, cursor.Value.LastTs, cursor.Value.LastName);

        var limit = q.Limit ?? limitDefault;
        var page = files.Take(limit).ToList();
        string? next = null;
        if (files.Count > limit)
            next = HistoryCursor.Encode(tokens, clock, q, page[^1].SnapshotTimestamp, page[^1].Entry.Name, pageTtl);
        return new(page.Select(f => new ConfigurationHistoryItem(
            IssueFileId(ConfigurationTokenKinds.FileIdSnapshotPurpose,
                q.EquipmentId, q.ConfigurationType, f.SnapshotTimestamp, f.Entry.Name),
            q.EquipmentId, q.ConfigurationType, f.SnapshotTimestamp, f.Entry.Name, f.Entry.Size)).ToList(), next);
    }

    public async Task<LocatedFile> LocateByFileIdAsync(TokenPayload payload, CancellationToken ct)
    {
        if (payload.Purpose is not (ConfigurationTokenKinds.FileIdCurrentPurpose
            or ConfigurationTokenKinds.FileIdSnapshotPurpose))
            throw new FileGatewayException("InvalidFileId", "not a configuration file id");
        var equipmentId = payload.Claims.GetValueOrDefault("equipmentId");
        var configurationType = payload.Claims.GetValueOrDefault("configurationType");
        var fileName = payload.Claims.GetValueOrDefault("fileName");
        if (equipmentId is null || configurationType is null || fileName is null)
            throw new FileGatewayException("InvalidFileId", "incomplete configuration file id claims");

        var def = await FindDefinitionAsync(equipmentId, configurationType, ct);
        if (payload.Purpose == ConfigurationTokenKinds.FileIdCurrentPurpose)
        {
            var match = (await currentResolver.ResolveAsync(def, ct))
                .SingleOrDefault(f => FileNameComparison.Same(f.Entry.Name, fileName))
                ?? throw new FileGatewayException("FileNotFound", "current configuration file no longer exists");
        var currentSize = await fileAccess.StatFileAsync(def.Server, match.RelativePath, ct);
        return new(def.Server, match.RelativePath, match.Entry.Name, currentSize);
        }

        var ts = payload.Claims.TryGetValue("ts", out var t) && t.Length > 0
            ? DateTimeOffset.Parse(t, CultureInfo.InvariantCulture)
            : throw new FileGatewayException("InvalidFileId", "snapshot file id requires ts claim");
        // 해당 날짜 슬롯만 재탐색 — resolver가 marker 존재를 다시 확인한다(marker 부재 → 그날 0건).
        var files = await historyResolver.ResolveAsync(def, new EffectiveRange(ts, ts.AddDays(1)), ct);
        var snapshot = files.SingleOrDefault(f => FileNameComparison.Same(f.Entry.Name, fileName))
            ?? throw new FileGatewayException("FileNotFound", "snapshot file no longer exists");
        var size = await fileAccess.StatFileAsync(def.Server, snapshot.RelativePath, ct);
        return new(def.Server, snapshot.RelativePath, snapshot.Entry.Name, size);
    }

    private async Task<ResolvedConfigurationDefinition> FindDefinitionAsync(
        string equipmentId, string configurationType, CancellationToken ct)
    {
        var snapshot = await referenceData.GetSnapshotAsync(ct);
        return snapshot.FindConfiguration(equipmentId, configurationType)
            ?? throw new FileGatewayException("ConfigurationDefinitionNotFound",
                $"no configuration definition for {equipmentId}/{configurationType}");
    }

    private string IssueFileId(string purpose, string equipmentId, string configurationType,
        DateTimeOffset? ts, string fileName)
    {
        var claims = new Dictionary<string, string>
        {
            ["equipmentId"] = equipmentId,
            ["configurationType"] = configurationType,
            ["ts"] = ts?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            ["fileName"] = fileName,
        };
        return tokens.Protect(new TokenPayload(purpose, claims, clock.GetUtcNow(), fileTtl));
    }

    // snapshotTimestamp DESC, fileName ASC(ci) 정렬에서 커서보다 새 파일과 동일 ts에서 커서 파일까지
    // (이름 순)를 건너뛴다.
    private static IReadOnlyList<ResolvedSnapshotFile> SkipUntilAfter(
        IReadOnlyList<ResolvedSnapshotFile> files, DateTimeOffset lastTs, string? lastName)
        => files.SkipWhile(f => AtOrBeforeCursor(f.SnapshotTimestamp, f.Entry.Name, lastTs, lastName)).ToList();

    private static bool AtOrBeforeCursor(DateTimeOffset ts, string name, DateTimeOffset lastTs, string? lastName)
        => ts > lastTs
           || (ts == lastTs && lastName is not null && FileNameComparison.Compare(name, lastName) <= 0);
}
