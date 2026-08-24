using System.Globalization;
using FileGateway.Configurations.Definitions;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Time;

namespace FileGateway.Configurations.Internal;

public sealed record ResolvedSnapshotFile(DateTimeOffset SnapshotTimestamp, string RelativePath, RemoteFileEntry Entry);

/// <summary>날짜 슬롯별 marker 존재 확인(FileExists) → 미완료 Set skip → 디렉터리 부재 skip →
/// glob → ci 중복 검사 → snapshotTimestamp DESC/fileName ASC 정렬. I/O 오류는 전체 실패.</summary>
public sealed class HistoryResolver(IFileAccess fileAccess)
{
    public async Task<IReadOnlyList<ResolvedSnapshotFile>> ResolveAsync(
        ResolvedConfigurationDefinition def, EffectiveRange range, CancellationToken ct)
    {
        var rule = def.Definition.HistoryRule;
        var glob = new GlobPattern(rule.FilePattern);
        var files = new List<ResolvedSnapshotFile>();
        var seen = new HashSet<string>(FileNameComparison.Comparer);

        // [from, to)의 정확한 하한: from이 자정이 아니면 그날 자정 snapshot은 from 이전이므로 제외한다.
        var start = SiteTime.SiteLocalMidnight(range.From);
        if (start < range.From) start = start.AddDays(1);
        for (var date = start; date < range.To; date = date.AddDays(1))
        {
            var markerRel = ExpandDate(rule.MarkerPathTemplate, date);
            if (!await fileAccess.FileExistsAsync(def.Server, markerRel, ct)) continue; // 미완료 Set 제외

            var dir = ExpandDate(rule.PathTemplate, date);
            var listing = await fileAccess.ListFilesAsync(def.Server, dir, ct);
            if (!listing.Exists) continue;

            foreach (var e in listing.Files)
            {
                if (!glob.Matches(e.Name)) continue;
                // 완료 marker 자체는 결과에 포함하지 않는다(04b) — glob이 marker와 일치해도 제외.
                if (FileNameComparison.Same(dir + "/" + e.Name, markerRel)) continue;
                if (!seen.Add($"{date:O}|{e.Name}"))
                    throw new FileGatewayException("FileDefinitionConflict", $"duplicate: {e.Name}");
                files.Add(new(date, dir + "/" + e.Name, e));
            }
        }
        return files.OrderByDescending(f => f.SnapshotTimestamp)
                    .ThenBy(f => f.Entry.Name, FileNameComparison.Comparer).ToList();
    }

    private static string ExpandDate(string template, DateTimeOffset siteLocalMidnight)
        => template.Replace("{yyyy}", siteLocalMidnight.ToString("yyyy", CultureInfo.InvariantCulture))
                   .Replace("{MM}", siteLocalMidnight.ToString("MM", CultureInfo.InvariantCulture))
                   .Replace("{dd}", siteLocalMidnight.ToString("dd", CultureInfo.InvariantCulture));
}
