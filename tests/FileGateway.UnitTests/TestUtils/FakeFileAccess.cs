using FileGateway.Core.Files;
using FileGateway.Core.Paths;
namespace FileGateway.UnitTests.TestUtils;

/// <summary>경로(대소문자 무시) → 파일 집합 in-memory IFileAccess. 디렉터리는 파일 경로의 부모로 유추.</summary>
public sealed class FakeFileAccess : IFileAccess
{
    private readonly Dictionary<string, byte[]> _files = new(FileNameComparison.Comparer);

    public void AddFile(string relativePath, byte[] content) => _files[relativePath] = content;
    public void RemoveFile(string relativePath) => _files.Remove(relativePath);

    private readonly Dictionary<string, int> _truncateAfterOpen = new(FileNameComparison.Comparer);

    /// <summary>open 성공 후 전송 도중 끊긴 시나리오: 선언 길이는 원본 그대로, 실제 읽기는 bytesToKeep까지만.</summary>
    public void TruncateAfterOpen(string relativePath, int bytesToKeep) => _truncateAfterOpen[relativePath] = bytesToKeep;

    private readonly Dictionary<string, long> _listingSize = new(FileNameComparison.Comparer);

    /// <summary>resolve/목록 시점 크기와 open 시점 실제 크기가 다른 race 시나리오: 목록/Stat에만 보고할 크기를 덮어쓴다.</summary>
    public void OverrideListingSize(string relativePath, long size) => _listingSize[relativePath] = size;

    public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
    {
        var prefix = RemotePath.Normalize(dir) + "/";
        if (!_files.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(RemoteDirectoryListing.Missing);
        var entries = _files.Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                         && !kv.Key[prefix.Length..].Contains('/'))
                            .Select(kv => new RemoteFileEntry(kv.Key[(kv.Key.LastIndexOf('/') + 1)..],
                                _listingSize.TryGetValue(kv.Key, out var s) ? s : kv.Value.Length))
                            .ToList();
        return Task.FromResult(new RemoteDirectoryListing(true, entries));
    }

    public Task<long> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
        => Task.FromResult<long>(_files.TryGetValue(path, out var v)
            ? _listingSize.TryGetValue(path, out var s) ? s : v.Length
            : throw new FileAccessException(FileAccessError.FileNotFound, "not found"));

    public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
        => Task.FromResult(_files.ContainsKey(path));

    public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
        => Task.FromResult(_files.TryGetValue(path, out var v)
            ? new RemoteOpenRead(
                _truncateAfterOpen.TryGetValue(path, out var keep)
                    ? new MemoryStream(v, 0, Math.Min(v.Length, keep), writable: false)
                    : new MemoryStream(v, writable: false),
                v.Length)
            : throw new FileAccessException(FileAccessError.FileNotFound, "not found"));
}
