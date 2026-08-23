using FileGateway.Core.Time;
using FileGateway.Logs.Definitions;

namespace FileGateway.Logs.Internal;

internal static class SlotExpansion
{
    public static IEnumerable<DateTimeOffset> EnumerateSlots(GenerationType type, EffectiveRange range)
    {
        switch (type)
        {
            case GenerationType.Hourly:
                for (var s = FloorToSiteHour(range.From); s < range.To; s = s.AddHours(1))
                    yield return s;
                break;
            case GenerationType.Daily:
                for (var d = SiteTime.SiteLocalMidnight(range.From); d < range.To; d = d.AddDays(1))
                    yield return d;
                break;
            default:
                yield return DateTimeOffset.MinValue; // Continuous: 토큰 미사용 단일 슬롯
                break;
        }
    }

    private static DateTimeOffset FloorToSiteHour(DateTimeOffset t)
    {
        var l = SiteTime.ToSiteLocal(t);
        var floored = new DateTimeOffset(l.Year, l.Month, l.Day, l.Hour, 0, 0, l.Offset);
        return floored;
    }
}
