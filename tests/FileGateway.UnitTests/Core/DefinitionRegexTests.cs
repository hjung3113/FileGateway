using System.Text.RegularExpressions;
using FileGateway.Core.Files;

namespace FileGateway.UnitTests.Core;

public class DefinitionRegexTests
{
    [Theory]
    [InlineData("fooXXX")]
    [InlineData("XXXbar")]
    [InlineData("foo\nbar")]
    // ^foo|bar$는 anchor 문자열 검사를 통과하지만 alternation 우선순위로 부분 일치한다 —
    // \A(?:...)\z wrap이 구조적으로 전체 일치를 강제한다(설계 §5.2, P2-N1).
    public void Wrapped_regex_rejects_partial_matches(string input)
        => Assert.DoesNotMatch(DefinitionRegex.Compile("^foo|bar$", RegexOptions.IgnoreCase), input);

    [Fact]
    public void Wrapped_regex_rejects_trailing_newline()
        // .NET $는 trailing \n 앞에서도 성공하지만 \z는 그렇지 않다.
        => Assert.DoesNotMatch(DefinitionRegex.Compile("^foo$", RegexOptions.IgnoreCase), "foo\n");

    [Theory]
    [InlineData("^foo|bar$", "foo")]
    [InlineData("^foo|bar$", "bar")]
    [InlineData("^foo$", "foo")]
    [InlineData("^PM[0-9]$", "PM3")]
    [InlineData("^(?<ts>\\d{10})\\.(zip|gz)$", "2026082920.zip")]
    public void Full_matches_succeed(string pattern, string input)
        => Assert.Matches(DefinitionRegex.Compile(pattern, RegexOptions.IgnoreCase), input);

    [Fact]
    public void Compile_adds_culture_invariant()
    {
        var r = DefinitionRegex.Compile("^a$", RegexOptions.None);
        Assert.True(r.Options.HasFlag(RegexOptions.CultureInvariant));
    }

    [Fact]
    public void Default_timeout_is_250ms()
        => Assert.Equal(TimeSpan.FromMilliseconds(250), DefinitionRegex.DefaultMatchTimeout);

    [Fact]
    public async Task Timeout_is_deterministic_via_test_seam()
    {
        // 짧은 timeout 주입(설계 §9 — 250ms 실측 대기 없이 결정적 검증). 입력에 matchTimeout이 전달된다.
        var r = DefinitionRegex.Compile("^(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(1));
        var input = new string('a', 60) + "b"; // catastrophic backtracking
        await Assert.ThrowsAsync<RegexMatchTimeoutException>(() => Task.Run(() => r.IsMatch(input)));
    }

    [Fact]
    public void Null_pattern_throws()
        => Assert.Throws<ArgumentNullException>(() => DefinitionRegex.Compile(null!, RegexOptions.None));

    // — RemoteDirectoryNames 계약 —

    [Fact]
    public void Missing_is_exists_false_and_empty()
    {
        var m = RemoteDirectoryNames.Missing;
        Assert.False(m.Exists);
        Assert.Empty(m.Names);
    }

    [Fact]
    public void Empty_directory_is_exists_true_with_empty_names()
    {
        var e = new RemoteDirectoryNames(true, []);
        Assert.True(e.Exists);
        Assert.Empty(e.Names);
    }
}
