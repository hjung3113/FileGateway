// src/FileGateway.Configurations/Internal/PathRuleResolver.cs
using FileGateway.Configurations.Definitions;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Paths;

namespace FileGateway.Configurations.Internal;

/// <summary>세그먼트 리스트와 논리 슬롯 날짜로 leaf 디렉터리 상대경로 목록을 반환한다(설계 §1.3).
/// Template 세그먼트는 열거 없이 경로를 확정하고, Regex 세그먼트마다 ListDirectoriesAsync fan-out.
/// regex 세그먼트가 하나도 없으면 I/O 없이 단일 확정 경로만 반환 — 기존 I/O 패턴과 동일하다.
/// Regex 세그먼트는 CompilePath가 정의(스냅샷) 수명에 바인딩해 컴파일한다(P2-S1).</summary>
public sealed class PathRuleResolver(IFileAccess fileAccess)
{
    public async Task<IReadOnlyList<string>> ResolveAsync(
        FileServerConnection server, IReadOnlyList<CompiledPathSegment> segments, DateTimeOffset slot, CancellationToken ct)
    {
        var current = new List<string> { "" }; // "" = root 상대 기준점(첫 세그먼트가 regex일 때의 열거 대상)
        foreach (var segment in segments)
        {
            if (current.Count == 0) return [];
            if (segment.Segment.Kind != PathSegmentKind.Regex)
            {
                var expanded = ConfigurationRuleParser.ExpandSegment(segment.Segment, slot);
                current = current.Select(p => p.Length == 0 ? expanded : p + "/" + expanded).ToList();
                continue;
            }
            var next = new List<string>();
            foreach (var path in current)
            {
                var listing = await fileAccess.ListDirectoriesAsync(server, path, ct);
                if (!listing.Exists) continue; // 디렉터리 부재 = branch prune(정상 빈 결과)
                foreach (var name in listing.Names.Where(n => IsEnumerableChildName(n) && FileMatcher.SafeIsMatch(segment.Regex!, n)))
                    next.Add(path.Length == 0 ? name : path + "/" + name);
            }
            // 매칭 자식 이름 ci 정렬로 결정론 보장(설계 §1.3).
            current = next.OrderBy(n => n, FileNameComparison.Comparer).ToList();
        }

        // 방어적 최종 확인(P1-2): root 결합 결과 기준 IsUnderRoot — resolver는 상대 leaf만 다룬다.
        foreach (var leaf in current)
        {
            var abs = RemotePath.Combine(server.RootPath, leaf);
            if (!RemotePath.IsUnderRoot(server.RootPath, abs))
                throw new FileGatewayException("FileDefinitionConflict", $"resolved path escapes root: {leaf}");
        }
        return current;
    }

    /// <summary>서버가 돌려준 이름 중 결합 불가능한 항목(""·"."·".."·구분자/드라이브 문자 포함)은 매칭에서 제외(설계 §6.2).</summary>
    internal static bool IsEnumerableChildName(string name)
        => name.Length > 0 && name is not "." and not ".."
           && !name.Contains('/') && !name.Contains('\\') && !name.Contains(':');
}
