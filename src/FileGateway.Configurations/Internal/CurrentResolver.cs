using FileGateway.Configurations.Definitions;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;

namespace FileGateway.Configurations.Internal;

public sealed record ResolvedConfigFile(string RelativePath, RemoteFileEntry Entry);

public sealed class CurrentResolver(IFileAccess fileAccess)
{
    public async Task<IReadOnlyList<ResolvedConfigFile>> ResolveAsync(
        ResolvedConfigurationDefinition def, CancellationToken ct)
    {
        var rule = def.Definition.CurrentRule;
        var glob = new GlobPattern(rule.FilePattern);
        var listing = await fileAccess.ListFilesAsync(def.Server, rule.PathTemplate, ct);
        if (!listing.Exists) return [];

        var files = new List<ResolvedConfigFile>();
        var seen = new HashSet<string>(FileNameComparison.Comparer);
        foreach (var e in listing.Files)
        {
            if (!glob.Matches(e.Name)) continue;
            if (!seen.Add(e.Name))
                throw new FileGatewayException("FileDefinitionConflict", $"duplicate file name: {e.Name}");
            files.Add(new(rule.PathTemplate + "/" + e.Name, e));
        }
        return files.OrderBy(f => f.Entry.Name, FileNameComparison.Comparer).ToList();
    }
}
