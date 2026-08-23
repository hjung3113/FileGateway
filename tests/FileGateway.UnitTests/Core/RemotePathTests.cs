// tests/FileGateway.UnitTests/Core/RemotePathTests.cs
using FileGateway.Core.Paths;

namespace FileGateway.UnitTests.Core;

public class RemotePathTests
{
    [Theory]
[InlineData("a/b/c", "a/b/c")]
    [InlineData("/a//b/", "a/b")]
    [InlineData(@"a\b\c", "a/b/c")]
    [InlineData("a/./b", "a/./b")]
    public void Normalize_unifies_separators_and_trims(string input, string expected)
        => Assert.Equal(expected, RemotePath.Normalize(input));

    [Fact]
    public void Combine_joins_root_and_relative()
        => Assert.Equal("ftproot/Logs/2026", RemotePath.Combine("ftproot", "Logs/2026"));

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData(@"C:\x")]
    [InlineData("a/../b")]
    public void IsSafeDefinitionPath_rejects_unsafe(string path)
        => Assert.False(RemotePath.IsSafeDefinitionPath(path));

    [Theory]
    [InlineData("Logs/{yyyy}")]
    [InlineData("a/b/c")]
    public void IsSafeDefinitionPath_accepts_relative(string path)
        => Assert.True(RemotePath.IsSafeDefinitionPath(path));

    [Fact]
    public void IsUnderRoot_accepts_child_and_rejects_sibling_or_escape()
    {
        Assert.True(RemotePath.IsUnderRoot("ftproot", "ftproot/Logs/x.log"));
        Assert.True(RemotePath.IsUnderRoot("FTPRoot", "ftproot/x"));   // case-insensitive
        Assert.False(RemotePath.IsUnderRoot("ftproot", "ftproot2/x"));  // prefix 함정
        Assert.False(RemotePath.IsUnderRoot("ftproot", "other/x"));
    }

    [Theory]
    [InlineData("ftproot", "ftproot/../outside", false)]   // traversal 우회 차단
    [InlineData("ftproot", "../ftproot/x", false)]          // 루트 위 잉여 ..
    [InlineData("ftproot", "ftproot/./sub/../x", true)]     // 무해한 dot 세그먼트 정규화
    [InlineData("ftproot", "ftproot/sub", true)]            // 회귀
    [InlineData("FTPRoot", "ftproot/./Sub/../x", true)]     // 대소문자 무시 + 정규화
    public void IsUnderRoot_canonicalizes_dot_segments(string root, string path, bool expected)
        => Assert.Equal(expected, RemotePath.IsUnderRoot(root, path));
}
