using FileGateway.Core.Files;
using FileGateway.Core.Paths;

namespace FileGateway.UnitTests.Configurations;

/// <summary>메모리 디렉터리/파일 트리를 실제로 열거·매칭하는 IFileAccess double.
/// 명시적 디렉터리(빈 디렉터리 포함)와 파일 경로로부터 유추된 디렉터리를 모두 지원하고,
/// ListDirectoriesAsync 계약(Exists=false/빈 디렉터리 Exists=true, Names=[]/파일·디렉터리 구분)을
/// 실제 어댑터와 같은 의미로 구현한다.</summary>
internal sealed class InMemoryFileAccess : IFileAccess
{
    private readonly Dictionary<string, byte[]> _files = new(FileNameComparison.Comparer);
    private readonly HashSet<string> _dirs = new(FileNameComparison.Comparer);

    public void AddDirectory(string path) => _dirs.Add(RemotePath.Normalize(path));
    public void AddFile(string path, byte[] content) => _files[RemotePath.Normalize(path)] = content;

    private bool DirExists(string p)
        => p.Length == 0
           || _dirs.Contains(p)
           || _dirs.Any(d => d.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase))
           || _files.Keys.Any(k => k.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));
    public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
    {
        var p = RemotePath.Normalize(dir);
        if (!DirExists(p)) return Task.FromResult(RemoteDirectoryListing.Missing);
        var prefix = p.Length == 0 ? "" : p + "/";
        var entries = _files
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                         && kv.Key.Length > prefix.Length
                         && !kv.Key[prefix.Length..].Contains('/'))
            .Select(kv => new RemoteFileEntry(kv.Key[(kv.Key.LastIndexOf('/') + 1)..], kv.Value.Length))
            .ToList();
        return Task.FromResult(new RemoteDirectoryListing(true, entries));
    }

    public Task<RemoteDirectoryNames> ListDirectoriesAsync(FileServerConnection server, string dir, CancellationToken ct)
    {
        var p = RemotePath.Normalize(dir);
        if (!DirExists(p)) return Task.FromResult(RemoteDirectoryNames.Missing);
        var prefix = p.Length == 0 ? "" : p + "/";
        var names = new SortedSet<string>(FileNameComparison.Comparer);
        foreach (var d in _dirs)
            if (d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && d.Length > prefix.Length)
            {
                var rest = d[prefix.Length..];
                var slash = rest.IndexOf('/');
                if (slash > 0) names.Add(rest[..slash]); // 중간 경유 디렉터리도 직계 자식으로 노출
                else if (slash < 0) names.Add(rest);
            }
        foreach (var f in _files.Keys)
            if (f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && f.Length > prefix.Length)
            {
                var rest = f[prefix.Length..];
                var slash = rest.IndexOf('/');
                if (slash > 0) names.Add(rest[..slash]); // 파일 경로로 유추되는 중간 디렉터리
            }
        return Task.FromResult(new RemoteDirectoryNames(true, [.. names]));
    }

    public Task<FileStat> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
    {
        var key = _files.Keys.FirstOrDefault(k => FileNameComparison.Same(k, RemotePath.Normalize(path)));
        return Task.FromResult(key is null
            ? throw new FileAccessException(FileAccessError.FileNotFound, "not found")
            : new FileStat(_files[key].Length, key[(key.LastIndexOf('/') + 1)..]));
    }

    public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
        => Task.FromResult(_files.ContainsKey(RemotePath.Normalize(path)));

    public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
        => Task.FromResult(_files.TryGetValue(RemotePath.Normalize(path), out var v)
            ? new RemoteOpenRead(new MemoryStream(v, writable: false), v.Length)
            : throw new FileAccessException(FileAccessError.FileNotFound, "not found"));
}
