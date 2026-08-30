using FileGateway.Configurations.Definitions;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Time;

namespace FileGateway.Configurations.Internal;

public sealed record ResolvedSnapshotFile(DateTimeOffset SnapshotTimestamp, string RelativePath, RemoteFileEntry Entry);

/// <summary>날짜 슬롯별 marker 존재 확인(FileExists) → 미완료 batch skip → PathRuleResolver 순회(regex fan-out 포함)
/// → 파일 매처 → metadata 추출(있으면 추출 ts=snapshotTimestamp + 물리 슬롯 날짜 일치 검증)
/// → (ts, ci-name) dedupe → snapshotTimestamp DESC/fileName ASC 정렬. I/O 오류는 전체 실패.</summary>
public sealed class HistoryResolver(IFileAccess fileAccess)
{
    public async Task<IReadOnlyList<ResolvedSnapshotFile>> ResolveAsync(
        ResolvedConfigurationDefinition def, EffectiveRange range, CancellationToken ct)
    {
        var rule = ConfigurationRuleParser.ParseHistory(def.Definition.HistoryRule);
        var hasMetadata = rule.Metadata is not null;
        var files = new List<ResolvedSnapshotFile>();
        var seen = new HashSet<string>(FileNameComparison.Comparer);

        // [from, to) 하한: metadata rule이 있으면 추출 ts 필터가 경계를 정확히 자르므로 비자정 보정 없이
        // 그날 자정부터 순회하고(Logs와 같은 구조), 없으면 기존 보정 로직 그대로(설계 §3.3).
        var start = SiteTime.SiteLocalMidnight(range.From);
        if (!hasMetadata && start < range.From) start = start.AddDays(1);
        for (var date = start; date < range.To; date = date.AddDays(1))
        {
            var markerRel = ExpandTemplatePath(rule.MarkerPath, date);
            if (!await fileAccess.FileExistsAsync(def.Server, markerRel, ct)) continue; // 미완료 batch 제외

            var directories = await new PathRuleResolver(fileAccess).ResolveAsync(def.Server, rule.Path, date, ct);
            foreach (var dir in directories)
            {
                var listing = await fileAccess.ListFilesAsync(def.Server, dir, ct);
                if (!listing.Exists) continue;

                foreach (var e in listing.Files)
                {
                    if (!rule.File.Matches(e.Name)) continue;
                    // 완료 marker 자체는 결과에 포함하지 않는다(04b) — 매처가 marker와 일치해도 제외.
                    if (FileNameComparison.Same(dir + "/" + e.Name, markerRel)) continue;

                    var ts = date;
                    if (hasMetadata)
                    {
                        if (!rule.Metadata!.TryGetTimestamp(e.Name, out var extracted))
                            throw new FileGatewayException("FileDefinitionConflict",
                                $"metadata extraction failed: {e.Name}");
                        // round-trip 불변식(P1-N2): 추출 ts의 site-local 날짜 == 물리 슬롯 날짜. drop 없이 거부.
                        if (SiteTime.SiteLocalMidnight(extracted) != SiteTime.SiteLocalMidnight(date))
                            throw new FileGatewayException("FileDefinitionConflict",
                                $"snapshot timestamp date does not match physical slot: {e.Name}");
                        ts = extracted;
                        // metadata rule이 있으면 [from, to) 필터는 추출 ts 기준(설계 §3.3).
                        if (ts < range.From || ts >= range.To) continue;
                    }

                    // dedupe 키는 (snapshotTimestamp, ci-name)으로 일반화 — rule이 없으면 ts=슬롯 자정이라 기존과 동치.
                    if (!seen.Add($"{ts:O}|{e.Name}"))
                        throw new FileGatewayException("FileDefinitionConflict", $"duplicate: {e.Name}");
                    files.Add(new(ts, dir + "/" + e.Name, e));
                }
            }
        }
        return files.OrderByDescending(f => f.SnapshotTimestamp)
                    .ThenBy(f => f.Entry.Name, FileNameComparison.Comparer).ToList();
    }

    // marker는 template 전용(regex 세그먼트 금지 — validator)이므로 결합 전 확장으로 재구성한다.
    private static string ExpandTemplatePath(IReadOnlyList<PathSegment> segments, DateTimeOffset slot)
        => string.Join("/", segments.Select(s => ConfigurationRuleParser.ExpandSegment(s, slot)));
}
