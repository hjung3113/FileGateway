using FileGateway.Configurations.Definitions;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Time;

namespace FileGateway.Configurations.Internal;

public sealed record ResolvedConfigFile(string RelativePath, RemoteFileEntry Entry);

/// <summary>Current 탐색. slot은 resolve마다 주입받은 TimeProvider에서 site-local 현재 시각을 정확히
/// 1회 캡처해 사용한다(설계 §1.3, P2-2) — 목록/재해석은 각자의 resolve 시작 시점에 독립 캡처.
/// 파일 매칭은 FileMatcher(Literal/Glob/Regex 기본 Glob), 경로는 PathRuleResolver(regex fan-out)가 담당.</summary>
public sealed class CurrentResolver(IFileAccess fileAccess, TimeProvider? clock = null)
{
    public async Task<IReadOnlyList<ResolvedConfigFile>> ResolveAsync(
        ResolvedConfigurationDefinition def, CancellationToken ct)
    {
        var rule = ConfigurationRuleParser.ParseCurrent(def.Definition.CurrentRule);
        var slot = SiteTime.ToSiteLocal((clock ?? TimeProvider.System).GetLocalNow());

        var directories = await new PathRuleResolver(fileAccess).ResolveAsync(def.Server, rule.Path, slot, ct);

        var files = new List<ResolvedConfigFile>();
        var seen = new HashSet<string>(FileNameComparison.Comparer); // leaf 전역 ci 충돌 검사(설계 §8)
        foreach (var dir in directories)
        {
            var listing = await fileAccess.ListFilesAsync(def.Server, dir, ct);
            if (!listing.Exists) continue;
            foreach (var e in listing.Files)
            {
                if (!rule.File.Matches(e.Name)) continue;
                if (!seen.Add(e.Name))
                    throw new FileGatewayException("FileDefinitionConflict", $"duplicate file name: {e.Name}");
                files.Add(new(dir + "/" + e.Name, e));
            }
        }
        return files.OrderBy(f => f.Entry.Name, FileNameComparison.Comparer).ToList();
    }
}
