using FileGateway.Core.Files;
namespace FileGateway.UnitTests.Core;

public class GlobPatternTests
{
    [Theory]
    [InlineData("*.zip", "Event_A.ZIP", true)]      // case-insensitive
    [InlineData("*.zip", "Event_A.zip", true)]
    [InlineData("Event_*.log", "Event_2026.log", true)]
    [InlineData("Event_*.log", "Trace_2026.log", false)]
    [InlineData("PM?.cfg", "PM1.cfg", true)]
    [InlineData("PM?.cfg", "PM12.cfg", false)]
    [InlineData("*.log", "sub/Event.log", false)]    // *는 / 안 넘음(파일명 전용)
    [InlineData("Event.log", "event.LOG", true)]
    public void Matches_applies_case_insensitive_glob(string pattern, string name, bool expected)
        => Assert.Equal(expected, new GlobPattern(pattern).Matches(name));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a/b")]
    public void Validate_rejects_invalid(string pattern)
        => Assert.Throws<ArgumentException>(() => GlobPattern.Validate(pattern));
}
