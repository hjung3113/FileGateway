using FileGateway.Core.Time;
using FileGateway.Logs.Definitions;
using FileGateway.Logs.Internal;

namespace FileGateway.UnitTests.Logs;

public class SlotExpansionTests
{
    private static readonly DateTimeOffset From = new(2026, 8, 22, 10, 30, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset To = new(2026, 8, 22, 13, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void Hourly_enumerates_hour_slots_in_half_open_range()
    {
        var slots = SlotExpansion.EnumerateSlots(GenerationType.Hourly, new EffectiveRange(From, To)).ToList();
        // 10:30 → 10:00 슬롯부터, 13:00 제외
        Assert.Equal(
        [
            new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 11, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.FromHours(9)),
        ], slots);
    }

    [Fact]
    public void Daily_enumerates_midnight_slots()
    {
        var from = new DateTimeOffset(2026, 8, 22, 23, 0, 0, TimeSpan.FromHours(9));
        var to = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.FromHours(9));
        var slots = SlotExpansion.EnumerateSlots(GenerationType.Daily, new EffectiveRange(from, to)).ToList();
        Assert.Equal(2, slots.Count);
        Assert.All(slots, s => Assert.Equal(0, s.Hour));
    }

    [Fact]
    public void Continuous_returns_single_slot()
        => Assert.Single(SlotExpansion.EnumerateSlots(GenerationType.Continuous, new EffectiveRange(From, To)));

    [Fact]
    public void Expand_substitutes_site_local_components()
    {
        var slot = new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.FromHours(9));
        Assert.Equal("Logs/2026/08/22/18", PathTemplate.Expand("Logs/{yyyy}/{MM}/{dd}/{HH}", slot));
        Assert.Equal("Logs/flat", PathTemplate.Expand("Logs/flat", slot));
    }

    [Fact]
    public void Expand_rejects_unknown_token_via_validate()
        => Assert.Throws<ArgumentException>(() => PathTemplate.ValidateTokens("Logs/{yy}"));

    [Fact]
    public void SiteTime_parses_offsetless_as_seoul()
    {
        var parsed = SiteTime.Parse("2026-08-22T18:00:00");
        Assert.Equal(TimeSpan.FromHours(9), parsed.Offset);
    }

    [Theory]
    [InlineData("2026-08-22T18:00:00Z", 0)]          // Z → UTC
    [InlineData("2026-08-22T18:00:00+09:00", 9)]     // 명시적 offset 유지
    [InlineData("2026-08-22T18:00:00-05:00", -5)]
    [InlineData("2026-08-22T18:00:00", 9)]           // offset 없음 → Seoul (머신 timezone 무관)
    public void SiteTime_parse_respects_offset_contract(string iso, int expectedOffsetHours)
        => Assert.Equal(TimeSpan.FromHours(expectedOffsetHours), SiteTime.Parse(iso).Offset);

    [Fact]
    public void SiteTime_midnight_uses_seoul_offset()
        => Assert.Equal(new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)),
                        SiteTime.SiteLocalMidnight(new DateTimeOffset(2026, 8, 22, 15, 0, 0, TimeSpan.FromHours(9))));
}
