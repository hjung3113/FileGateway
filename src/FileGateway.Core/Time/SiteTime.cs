using System.Globalization;

namespace FileGateway.Core.Time;

public static class SiteTime
{
    public static readonly TimeZoneInfo Local = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");

    public static DateTimeOffset ToSiteLocal(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, Local);

    public static DateTimeOffset SiteLocalMidnight(DateTimeOffset t)
    {
        var l = ToSiteLocal(t);
        return new DateTimeOffset(l.Date, l.Offset);
    }

    /// <summary>API 시각 파싱. offset 포함 값은 그 offset을 그대로, offset 없는 값은 Asia/Seoul(+09:00)로 해석한다.
    /// 실행 머신 local timezone은 절대 사용하지 않는다(확정 결정 9).</summary>
    public static DateTimeOffset Parse(string iso)
    {
        if (!HasOffset(iso) && DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var naive))
            return new DateTimeOffset(naive, Local.GetUtcOffset(naive)); // offset 없음 → Asia/Seoul 확정
        if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
            return withOffset;
        throw new ArgumentException($"unparseable timestamp: {iso}");
    }

    private static bool HasOffset(string iso)
        => iso.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
        || iso.Contains('+')
        || iso.LastIndexOf('-') >= 10; // ISO 날짜부 "YYYY-MM-DD"는 10자: 이후의 '-'는 시간대 offset
}
