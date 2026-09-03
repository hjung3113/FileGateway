using FileGateway.Core.Files;
using FileGateway.Core.Paths;

namespace FileGateway.Infrastructure.Ftp;

/// <summary>
/// Host=="localhost" 서버용 IFileAccess. RootPath를 로컬 절대 경로로 해석해 System.IO로 직접 읽는다.
/// 경로 검증/에러 매핑은 FtpFileAccess와 동등 수준을 유지한다(RemotePath.Combine 가드 + GetFullPath 접두사 검증 + reparse point 거부).
/// </summary>
public sealed class LocalFileAccess : IFileAccess
{
    public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var full = ResolvePhysicalPath(server, dir);
            var files = new List<RemoteFileEntry>();
            foreach (var p in Directory.EnumerateFiles(full)) // 파일만 — FTP의 Type==File 필터와 대응
            {
                ct.ThrowIfCancellationRequested(); // 대량 디렉터리 스캔 중 클라이언트 단절 즉시 반영
                try { files.Add(new RemoteFileEntry(Path.GetFileName(p), new FileInfo(p).Length)); }
                catch (FileNotFoundException) { /* 나열 도중 삭제/rotation된 항목은 스킵 */ }
            }
            return Task.FromResult(new RemoteDirectoryListing(true, files));
        }
        catch (DirectoryNotFoundException) { return Task.FromResult(RemoteDirectoryListing.Missing); }
        catch (Exception ex) when (ex is not FileAccessException and not OperationCanceledException)
        { throw Classify(ex); }
    }

    public Task<RemoteDirectoryNames> ListDirectoriesAsync(FileServerConnection server, string dir, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var full = ResolvePhysicalPath(server, dir);
            var names = new List<string>();
            foreach (var p in Directory.EnumerateDirectories(full))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var name = Path.GetFileName(p);
                    if (IsSafeDirectoryName(name)) names.Add(name);
                }
                catch (FileNotFoundException) { /* 나열 도중 삭제/rotation된 항목은 스킵 */ }
            }
            return Task.FromResult(new RemoteDirectoryNames(true, names));
        }
        catch (DirectoryNotFoundException) { return Task.FromResult(RemoteDirectoryNames.Missing); }
        catch (Exception ex) when (ex is not FileAccessException and not OperationCanceledException)
        { throw Classify(ex); }
    }

    public Task<FileStat> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var full = ResolvePhysicalPath(server, path);
            var info = new FileInfo(full);
            // Attributes/Length 조회는 실제 메타데이터 접근 — 권한 거부는 IoFailure로, 없음은 FileNotFound로만 분류된다.
            if (info.Attributes.HasFlag(FileAttributes.Directory))
                throw new FileAccessException(FileAccessError.FileNotFound, "file not found");
            // FileInfo.Name은 생성자에 넘긴 경로 문자열을 그대로 반영할 뿐 디스크의 실제 casing을
            // 조회하지 않는다(.NET 자체 동작) — 부모 디렉터리를 열거해 실제 대소문자를 찾는다.
            return Task.FromResult(new FileStat(info.Length, ResolveActualCasing(full)));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { throw new FileAccessException(FileAccessError.FileNotFound, "file not found", ex); }
        catch (Exception ex) when (ex is not FileAccessException and not OperationCanceledException)
        { throw Classify(ex); }
    }

    // 부모 디렉터리를 case-insensitive로 열거해 실제 on-disk 파일명을 찾는다. 열거 중 사라졌거나
    // (경합) 못 찾으면 요청 경로의 이름을 그대로 반환한다(존재 자체는 위에서 이미 확인했다).
    private static string ResolveActualCasing(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath);
        var requested = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(dir)) return requested;
        try
        {
            return Directory.EnumerateFiles(dir)
                .Select(Path.GetFileName)
                .FirstOrDefault(n => string.Equals(n, requested, StringComparison.OrdinalIgnoreCase))
                ?? requested;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return requested;
        }
    }

    public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            // 메타데이터 조회 기반 — 없음(FileNotFound*)일 때만 false, 권한/IO 오류는 IoFailure로 전파된다.
            return Task.FromResult(
                !new FileInfo(ResolvePhysicalPath(server, path)).Attributes.HasFlag(FileAttributes.Directory));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { return Task.FromResult(false); }
        catch (Exception ex) when (ex is not FileAccessException and not OperationCanceledException)
        { throw Classify(ex); }
    }

    public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var full = ResolvePhysicalPath(server, path);
            var stream = new FileStream(full, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Write | FileShare.Delete, // 생산자의 append/rotation과 병행 허용
                bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(new RemoteOpenRead(new LocalFileStream(stream), stream.Length));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { throw new FileAccessException(FileAccessError.FileNotFound, "file not found", ex); }
        catch (Exception ex) when (ex is not FileAccessException and not OperationCanceledException)
        { throw Classify(ex); }
    }

    /// <summary>RootPath + relative를 로컬 절대 경로로 정규화. FTP 어댑터와 동일한 RemotePath.Combine
    /// 가드로 unsafe relative를 차단하고, GetFullPath 정규화 후 루트 접두사를 재검증한다.
    /// 경로 구성요소에 symlink/junction(reparse point)이 있으면 거부한다 — GetFullPath는 링크 대상을
    /// 해석하지 않으므로 접두사 검사만으로는 root 밖 이탈을 막을 수 없다.</summary>
    private static string ResolvePhysicalPath(FileServerConnection server, string relative)
    {
        if (!Path.IsPathFullyQualified(server.RootPath))
            throw new FileAccessException(FileAccessError.ProtocolError,
                "RootPath must be an absolute local path"); // 상대경로가 CWD 기준으로 조용히 절대화되지 않도록 fail-fast

        if (!string.IsNullOrWhiteSpace(relative))
        {
            try { RemotePath.Combine(server.RootPath, relative); } // rooted/`..`/`.` 세그먼트 거부 — 결과값은 로컬 조합에 재사용하지 않음
            catch (ArgumentException ex)
            { throw new FileAccessException(FileAccessError.ProtocolError, "unsafe relative path", ex); }
        }

        // RemotePath.Normalize는 FTP 원격 경로 계약상 선행 '/'를 제거하므로 로컬 절대 경로에는 사용하지 않는다.
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(server.RootPath));
        var full = string.IsNullOrWhiteSpace(relative)
            ? root
            : Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var underRoot = full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        || full == root;
        if (!underRoot) throw new FileAccessException(FileAccessError.ProtocolError, "path escapes root");
        RejectReparsePoints(root, full);
        return full;
    }

    /// <summary>root(제외)부터 full(포함)까지 각 구성요소의 attribute를 조회해 symlink/junction을 거부한다.
    /// 없는 구성요소는 링크일 수 없으므로 통과시킨다(없음은 이후 실제 IO에서 FileNotFound로 분류).</summary>
    private static void RejectReparsePoints(string root, string full)
    {
        if (full == root) return; // 루트 자체는 검사 대상 없음
        var current = root;
        foreach (var segment in full.Substring(root.Length + 1)
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { return; } // 이후 구성요소는 존재할 수 없음
            catch (DirectoryNotFoundException) { return; }
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new FileAccessException(FileAccessError.ProtocolError, "path traverses a symbolic link");
        }
    }

    private static FileAccessException Classify(Exception ex) => ex switch
    {
        UnauthorizedAccessException => new(FileAccessError.IoFailure, "local access denied", ex),
        IOException                 => new(FileAccessError.IoFailure, "local I/O failure", ex), // sharing violation, 디스크, PathTooLong 포함
        _                           => new(FileAccessError.ProtocolError, "local failure", ex),  // FTP catch-all과 대응
    };

    private static bool IsSafeDirectoryName(string? name)
        => !string.IsNullOrEmpty(name)
           && name is not "." and not ".."
           && name.IndexOfAny(['/', '\\', ':']) < 0;

    /// <summary>읽기 중 IO 오류를 FileAccessError로 변환. 취소(클라이언트 단절 포함)는 변환 없이 그대로 전파한다.
    /// CanSeek=false — 소비자는 전진 전용으로 사용(OwnedFtpStream과 동일).</summary>
    private sealed class LocalFileStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override int Read(byte[] buffer, int offset, int count)
        {
            try { return inner.Read(buffer, offset, count); }
            catch (Exception ex) when (ex is not FileAccessException and not OperationCanceledException)
            { throw Classify(ex); }
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
        {
            try { return await inner.ReadAsync(buffer, ct); }
            catch (Exception ex) when (ex is not FileAccessException and not OperationCanceledException)
            { throw Classify(ex); }
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); }
        public override async ValueTask DisposeAsync() => await inner.DisposeAsync();
        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
    }
}
