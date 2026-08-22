// src/FileGateway.Core/Paths/RemotePath.cs
namespace FileGateway.Core.Paths;

public static class RemotePath
{
    public static string Normalize(string path)
        => string.Join("/",
             path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string Combine(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return Normalize(root);
        if (IsRooted(relative)) throw new ArgumentException("relative path must not be rooted", nameof(relative));
        if (!IsSafeDefinitionPath(relative)) throw new ArgumentException("unsafe relative path", nameof(relative));
        return Normalize(root + "/" + relative);
    }

    public static bool IsRooted(string path)
    {
        var p = path.Trim();
        return p.StartsWith('/') || p.StartsWith('\\') || p.Contains(':');
    }

    public static bool IsSafeDefinitionPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsRooted(path)) return false;
        foreach (var seg in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            if (seg is "." or "..") return false;
        return true;
    }

    public static bool IsUnderRoot(string root, string path)
    {
        var r = Normalize(root);
        var p = Normalize(path);
        return (p + "/").StartsWith(r + "/", StringComparison.OrdinalIgnoreCase);
    }
}
