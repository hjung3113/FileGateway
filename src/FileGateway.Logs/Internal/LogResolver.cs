using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Time;
using FileGateway.Logs.Definitions;

namespace FileGateway.Logs.Internal;

public sealed record ResolvedLogFile(ParsedMetadata Metadata, RemoteFileEntry Entry, string RelativePath);

/// <summary>슬롯→디렉터리(중복 제거)→목록→glob→metadata→ci 중복 검사→cardinality→시간 필터→정렬.</summary>
public sealed class LogResolver(IFileAccess fileAccess)
{
    public async Task<IReadOnlyList<ResolvedLogFile>> ResolveAsync(
        ResolvedLogDefinition def, EffectiveRange range, CancellationToken ct)
    {
        var d = def.Definition;
        var rule = d.DiscoveryRule;
        var glob = new GlobPattern(rule.FilePattern);

        // 슬롯 → 디렉터리(중복 제거: 여러 슬롯이 같은 물리 디렉터리일 수 있다)
        var directories = SlotExpansion.EnumerateSlots(d.GenerationType, range)
            .Select(slot => PathTemplate.Expand(rule.PathTemplate, slot))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var files = new List<ResolvedLogFile>();

        foreach (var dir in directories)
        {
            var listing = await fileAccess.ListFilesAsync(def.Server, dir, ct); // I/O 오류는 그대로 상향(전체 실패)
            if (!listing.Exists) continue;                                      // 디렉터리 부재 = 정상 0개

            // 중복 판정은 "동일 탐색 결과(동일 디렉터리)" 기준이다(문서: 동일 탐색 범위의 case-insensitive 동일 파일명).
            // 서로 다른 디렉터리의 같은 basename은 논리 timestamp가 다른 한 별개 파일이다 —
            // (timestamp, fileName) 전역 유일성은 아래 SortAndCheckIdentity에서 강제한다.
            var seenNames = new HashSet<string>(FileNameComparison.Comparer);
            foreach (var entry in listing.Files)
            {
                if (!glob.Matches(entry.Name)) continue;
                if (!seenNames.Add(entry.Name))
                    throw new FileGatewayException("FileDefinitionConflict",
                        $"case-insensitive duplicate file name in {dir}: {entry.Name}");
                var relativePath = dir + "/" + entry.Name;
                var meta = MetadataRuleParser.Parse(d.MetadataRule, d.GenerationType, relativePath);
                if (meta is null)
                    throw new FileGatewayException("FileDefinitionConflict",
                        $"file matched pattern but metadata unparseable: {relativePath}");
                files.Add(new(meta, entry, relativePath));
            }
        }

        CheckCardinality(d, rule.Cardinality, files);

        if (d.GenerationType != GenerationType.Continuous)
            files = files.Where(f => f.Metadata.Timestamp >= range.From && f.Metadata.Timestamp < range.To).ToList();

        return d.GenerationType == GenerationType.Continuous
            ? files.OrderBy(f => f.Entry.Name, FileNameComparison.Comparer).ToList()
            : SortAndCheckIdentity(files);
    }

    // Hourly/Daily: 논리 identity는 (timestamp, fileName ci)이며 전역(전 디렉터리) 유일해야 한다.
    // 정렬(ts DESC, name ASC ci) 후 인접 검사로 중복을 찾는다 — 서로 다른 디렉터리에서 regex 메타가
    // 같은 basename을 같은 timestamp로 매핑하면 커서 키가 충돌해 pagination이 항목을 잃는다.
    // Continuous는 단일 슬롯(단일 디렉터리)이라 위 per-directory 검사가 이미 전역이다.
    private static List<ResolvedLogFile> SortAndCheckIdentity(List<ResolvedLogFile> files)
    {
        var sorted = files.OrderByDescending(f => f.Metadata.Timestamp!.Value)
            .ThenBy(f => f.Entry.Name, FileNameComparison.Comparer).ToList();
        for (var i = 1; i < sorted.Count; i++)
            if (sorted[i - 1].Metadata.Timestamp == sorted[i].Metadata.Timestamp
                && FileNameComparison.Same(sorted[i - 1].Entry.Name, sorted[i].Entry.Name))
                throw new FileGatewayException("FileDefinitionConflict",
                    $"duplicate logical file identity (timestamp, fileName): {sorted[i].Entry.Name}");
        return sorted;
    }

    private static void CheckCardinality(EquipmentLogDefinition d, Cardinality card, List<ResolvedLogFile> files)
    {
        if (card != Cardinality.Single) return;
        Func<ResolvedLogFile, object> slotKeys = d.GenerationType switch
        {
            GenerationType.Hourly => SiteHourSlot,
            GenerationType.Daily => f => SiteTime.SiteLocalMidnight(f.Metadata.Timestamp!.Value),
            _ => _ => 0,
        };
        foreach (var g in files.GroupBy(slotKeys))
            if (g.Count() > 1)
                throw new FileGatewayException("FileDefinitionConflict",
                    $"cardinality=Single but slot has {g.Count()} files");
    }

    // 시간대 offset은 SiteTime(Asia/Seoul)에서 유도한다(+9 하드코딩 금지).
    private static object SiteHourSlot(ResolvedLogFile f)
    {
        var l = SiteTime.ToSiteLocal(f.Metadata.Timestamp!.Value);
        return new DateTimeOffset(l.Year, l.Month, l.Day, l.Hour, 0, 0, l.Offset);
    }
}
