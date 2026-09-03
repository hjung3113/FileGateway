using System.Reflection;
using FileGateway.Core.Files;
using FileGateway.Infrastructure.Ftp;

namespace FileGateway.UnitTests.Infrastructure.Ftp;

public sealed class LocalFileAccessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgw-local-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileAccess _access = new();

    public LocalFileAccessTests()
        => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* rotation race — 베스트 에포트 */ }
    }

    private FileServerConnection Server(string? rootPath = null)
        => new("S1", "localhost", rootPath ?? _root);

    [Fact] // L1 — AC-22-1
    public async Task AC_22_1_ListFilesReturnsFilesWithSizesAndExcludesSubdirectories()
    {
        File.WriteAllText(Path.Combine(_root, "a.log"), "12345");
        File.WriteAllText(Path.Combine(_root, "b.log"), "1234567");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));

        var listing = await _access.ListFilesAsync(Server(), "", CancellationToken.None);

        Assert.True(listing.Exists);
        var names = listing.Files.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(["a.log", "b.log"], names);
        var a = listing.Files.Single(f => f.Name == "a.log");
        Assert.Equal(5, a.Size);
        Assert.Equal(7, listing.Files.Single(f => f.Name == "b.log").Size);
    }

    [Fact] // L2 — AC-22-1
    public async Task AC_22_1_ListFilesOnEmptyDirectoryReturnsEmptyListing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        var listing = await _access.ListFilesAsync(Server(), "empty", CancellationToken.None);

        Assert.True(listing.Exists);
        Assert.Empty(listing.Files);
    }

    [Fact]
    public async Task ListDirectories_returns_immediate_children_and_distinguishes_empty_and_missing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        Directory.CreateDirectory(Path.Combine(_root, "beta", "nested"));
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        File.WriteAllText(Path.Combine(_root, "root.log"), "x");

        var listing = await _access.ListDirectoriesAsync(Server(), "", CancellationToken.None);

        Assert.True(listing.Exists);
        Assert.Equal(["alpha", "beta", "empty"],
            listing.Names.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        var empty = await _access.ListDirectoriesAsync(Server(), "empty", CancellationToken.None);
        Assert.True(empty.Exists);
        Assert.Empty(empty.Names);

        var missing = await _access.ListDirectoriesAsync(Server(), "missing", CancellationToken.None);
        Assert.False(missing.Exists);
        Assert.Empty(missing.Names);
    }

    [Fact]
    public async Task ListDirectories_observes_canceled_token()
    {
        Directory.CreateDirectory(Path.Combine(_root, "child"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _access.ListDirectoriesAsync(Server(), "", cts.Token));
    }

    [Fact] // L3 — AC-22-1, AC-22-3
    public async Task AC_22_3_ListFilesOnMissingDirectoryReturnsMissing()
    {
        var listing = await _access.ListFilesAsync(Server(), "nope", CancellationToken.None);

        Assert.Equal(RemoteDirectoryListing.Missing, listing);
        Assert.False(listing.Exists);
    }

    [Fact] // L4 — AC-22-1
    public async Task AC_22_1_StatFileReturnsActualByteLength()
    {
        File.WriteAllText(Path.Combine(_root, "a.log"), "12345678");

        var size = await _access.StatFileAsync(Server(), "a.log", CancellationToken.None);

        Assert.Equal(8, size.Size);
    }

    [Fact]
    public async Task StatFile_returns_actual_on_disk_casing_not_requested_casing()
    {
        // FileInfo.Name은 생성자 인자 문자열을 그대로 반영할 뿐 디스크의 실제 대소문자를 조회하지
        // 않는다(.NET 자체 동작) — 요청과 다른 casing으로 만든 실제 파일을 다른 casing으로 조회해
        // StatFileAsync가 실제 디스크 이름을 돌려주는지 검증한다.
        File.WriteAllText(Path.Combine(_root, "RealCasing.log"), "x");

        var stat = await _access.StatFileAsync(Server(), "realcasing.log", CancellationToken.None);

        Assert.Equal("RealCasing.log", stat.ActualName);
    }

    [Fact] // L5 — AC-22-3
    public async Task AC_22_3_StatMissingFileThrowsFileNotFound()
    {
        var ex = await Assert.ThrowsAsync<FileAccessException>(
            () => _access.StatFileAsync(Server(), "missing.log", CancellationToken.None));

        Assert.Equal(FileAccessError.FileNotFound, ex.Error);
        Assert.Equal("file not found", ex.Message);
    }

    [Fact] // L6 — AC-22-3
    public async Task AC_22_3_StatDirectoryPathThrowsFileNotFound()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));

        var ex = await Assert.ThrowsAsync<FileAccessException>(
            () => _access.StatFileAsync(Server(), "sub", CancellationToken.None));

        Assert.Equal(FileAccessError.FileNotFound, ex.Error);
    }

    [Fact] // L7 — AC-22-3
    public async Task AC_22_3_FileExistsDistinguishesFileMissingAndDirectory()
    {
        File.WriteAllText(Path.Combine(_root, "a.log"), "x");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));

        Assert.True(await _access.FileExistsAsync(Server(), "a.log", CancellationToken.None));
        Assert.False(await _access.FileExistsAsync(Server(), "missing.log", CancellationToken.None));
        Assert.False(await _access.FileExistsAsync(Server(), "sub", CancellationToken.None));
    }

    [Fact] // L8 — AC-22-1
    public async Task AC_22_1_OpenReadReturnsLengthAndFullContent()
    {
        var content = "hello local file"u8.ToArray();
        File.WriteAllBytes(Path.Combine(_root, "a.log"), content);

        var opened = await _access.OpenReadAsync(Server(), "a.log", CancellationToken.None);
        await using (opened.Stream)
        {
            Assert.Equal(content.Length, opened.Length);
            using var ms = new MemoryStream();
            await opened.Stream.CopyToAsync(ms);
            Assert.Equal(content, ms.ToArray());
        }
    }

    [Fact] // L9 — AC-22-3
    public async Task AC_22_3_OpenMissingFileThrowsFileNotFound()
    {
        var ex = await Assert.ThrowsAsync<FileAccessException>(
            () => _access.OpenReadAsync(Server(), "missing.log", CancellationToken.None));

        Assert.Equal(FileAccessError.FileNotFound, ex.Error);
    }

    [Theory] // L10 — AC-22-4
    [InlineData("..")]
    [InlineData("a/../../x")]
    [InlineData("/etc/passwd")]
    [InlineData("\\evil")]
    [InlineData("C:\\evil")]
    [InlineData("x:y")]
    public async Task AC_22_4_UnsafeRelativePathsAreRejectedWithProtocolErrorBeforeAnyIo(string relative)
    {
        // 루트 밖에 실제로 존재하는 파일/디렉터리(임시 루트의 부모)를 대상으로 시도해도 거부가 IO보다 선행해야 한다.
        var ex = await Assert.ThrowsAsync<FileAccessException>(
            () => _access.StatFileAsync(Server(), relative, CancellationToken.None));

        Assert.Equal(FileAccessError.ProtocolError, ex.Error);
        Assert.Equal("unsafe relative path", ex.Message);
    }

    [Fact] // L11 — AC-22-4
    public async Task AC_22_4_RootPathTrailingSeparatorVariantIsAbsorbedAndEscapeIsRejected()
    {
        File.WriteAllText(Path.Combine(_root, "a.log"), "x");
        var server = Server(_root + Path.DirectorySeparatorChar);

        var size = await _access.StatFileAsync(server, "a.log", CancellationToken.None);
        Assert.Equal(1, size.Size);

        var ex = Assert.Throws<FileAccessException>(() => InvokeResolvePhysicalPath(
            new FileServerConnection("S1", "localhost", _root + Path.DirectorySeparatorChar), "a/../../escape"));
        Assert.Equal("unsafe relative path", ex.Message);
    }

    [Fact] // L12 — AC-22-1
    public async Task AC_22_1_EmptyRelativeListsRootDirectoryItself()
    {
        File.WriteAllText(Path.Combine(_root, "a.log"), "12345");

        var listing = await _access.ListFilesAsync(Server(), "", CancellationToken.None);

        Assert.True(listing.Exists);
        Assert.Single(listing.Files, f => f.Name == "a.log");
    }

    [Fact] // L13 — design §9.2: read 중 일반 IO 오류는 IoFailure로 변환
    public async Task AC_22_3_StreamReadMapsIOExceptionToFileAccessIoFailure()
    {
        await using var stream = CreateLocalStream(new ThrowingReadStream(
            () => new IOException("simulated streaming read failure")));

        var sync = Assert.Throws<FileAccessException>(() => stream.Read(new byte[1], 0, 1));
        Assert.Equal(FileAccessError.IoFailure, sync.Error);

        var asyncEx = await Assert.ThrowsAsync<FileAccessException>(
            () => stream.ReadAsync(new byte[1], CancellationToken.None).AsTask());
        Assert.Equal(FileAccessError.IoFailure, asyncEx.Error);
    }

    [Fact] // L13 — design §9.2: 취소는 변환 없이 그대로 전파(클라이언트 단절 분류 회귀 방지)
    public async Task AC_22_3_StreamReadPropagatesCancellationWithoutMapping()
    {
        await using var stream = CreateLocalStream(new ThrowingReadStream(
            () => new OperationCanceledException()));

        Assert.Throws<OperationCanceledException>(() => stream.Read(new byte[1], 0, 1));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => stream.ReadAsync(new byte[1], CancellationToken.None).AsTask());
    }

    [Fact] // L13 — TaskCanceledException(OperationCanceledException 파생)도 변환 없이 전파
    public async Task AC_22_3_StreamReadPropagatesTaskCanceledWithoutMapping()
    {
        await using var stream = CreateLocalStream(new ThrowingReadStream(
            () => new TaskCanceledException()));

        var sync = Assert.Throws<TaskCanceledException>(() => stream.Read(new byte[1], 0, 1));
        Assert.IsNotType<FileAccessException>(sync);
        var asyncEx = await Assert.ThrowsAsync<TaskCanceledException>(
            () => stream.ReadAsync(new byte[1], CancellationToken.None).AsTask());
        Assert.IsNotType<FileAccessException>(asyncEx);
    }

    [Fact] // L15 — PR#23 review: symlink/junction으로 RootPath 밖을 가리키는 경로는 reparse point 거부
    public async Task AC_22_4_SymlinkEscapingRootIsRejectedWithProtocolError()
    {
        var outside = Path.Combine(Path.GetTempPath(), "fgw-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "secret.log"), "x");
            Directory.CreateSymbolicLink(Path.Combine(_root, "link"), outside); // <root>/link -> outside
            File.CreateSymbolicLink(Path.Combine(_root, "link.log"), Path.Combine(outside, "secret.log"));

            foreach (var relative in new[] { "link/secret.log", "link.log" })
            {
                var ex = await Assert.ThrowsAsync<FileAccessException>(
                    () => _access.StatFileAsync(Server(), relative, CancellationToken.None));
                Assert.Equal(FileAccessError.ProtocolError, ex.Error);
                Assert.Equal("path traverses a symbolic link", ex.Message);
            }
        }
        finally { Directory.Delete(outside, recursive: true); }
    }

    [Fact] // L15 — root 내부를 가리키는 symlink라도 일관된 거부(링크 허용 여부를 대상 경로로 판단하지 않는다)
    public async Task AC_22_4_SymlinkInsideRootIsAlsoRejected()
    {
        Directory.CreateDirectory(Path.Combine(_root, "real"));
        File.WriteAllText(Path.Combine(_root, "real", "a.log"), "x");
        Directory.CreateSymbolicLink(Path.Combine(_root, "alias"), Path.Combine(_root, "real"));

        var ex = await Assert.ThrowsAsync<FileAccessException>(
            () => _access.StatFileAsync(Server(), "alias/a.log", CancellationToken.None));
        Assert.Equal(FileAccessError.ProtocolError, ex.Error);
    }

    [Fact] // L16 — PR#23 review: 상대 RootPath는 CWD 기준 절대화 대신 ProtocolError로 fail-fast
    public async Task AC_22_3_RelativeRootPathFailsFastWithProtocolError()
    {
        var server = new FileServerConnection("S1", "localhost", "logs");

        var stat = await Assert.ThrowsAsync<FileAccessException>(
            () => _access.StatFileAsync(server, "a.log", CancellationToken.None));
        Assert.Equal(FileAccessError.ProtocolError, stat.Error);
        Assert.Equal("RootPath must be an absolute local path", stat.Message);

        var list = await Assert.ThrowsAsync<FileAccessException>(
            () => _access.ListFilesAsync(server, "", CancellationToken.None));
        Assert.Equal(FileAccessError.ProtocolError, list.Error);
    }

    [Fact] // L17 — PR#23 review: 취소 토큰이 목록 나열 중에도 관찰되어야 한다(선확인 통과 후에도 즉시 전파)
    public async Task AC_22_1_ListFilesObservesCanceledToken()
    {
        File.WriteAllText(Path.Combine(_root, "a.log"), "x");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _access.ListFilesAsync(Server(), "", cts.Token));
    }

    private static Stream CreateLocalStream(Stream inner)
    {
        var streamType = typeof(LocalFileAccess).GetNestedType(
            "LocalFileStream", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("LocalFileStream was not found.");
        var constructor = streamType.GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .SingleOrDefault()
            ?? throw new InvalidOperationException("LocalFileStream constructor was not found.");
        return (Stream)(constructor.Invoke([inner])
            ?? throw new InvalidOperationException("LocalFileStream was not created."));
    }

    private sealed class ThrowingReadStream(Func<Exception> factory) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw factory();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
            => ValueTask.FromException<int>(factory());
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { }
    }


    [Fact] // L14 — AC-22-3
    public void AC_22_3_ClassifyMapsUnauthorizedIoAndOtherExceptions()
    {
        Assert.Equal(FileAccessError.IoFailure, InvokeClassify(new UnauthorizedAccessException("denied")).Error);
        Assert.Equal(FileAccessError.IoFailure, InvokeClassify(new IOException("io")).Error);
        Assert.Equal(FileAccessError.ProtocolError, InvokeClassify(new InvalidOperationException("other")).Error);
    }

    private static string InvokeResolvePhysicalPath(FileServerConnection server, string relative)
    {
        var method = typeof(LocalFileAccess).GetMethod(
            "ResolvePhysicalPath", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ResolvePhysicalPath was not found.");
        try
        {
            return (string)(method.Invoke(null, [server, relative])
                ?? throw new InvalidOperationException("ResolvePhysicalPath returned no value."));
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }

    private static FileAccessException InvokeClassify(Exception exception)
    {
        var method = typeof(LocalFileAccess).GetMethod(
            "Classify", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Classify was not found.");
        return (FileAccessException)(method.Invoke(null, [exception])
            ?? throw new InvalidOperationException("Classify returned no value."));
    }

}
