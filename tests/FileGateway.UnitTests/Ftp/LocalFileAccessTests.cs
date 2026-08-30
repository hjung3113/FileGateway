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

        Assert.Equal(8, size);
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
        Assert.Equal(1, size);

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

