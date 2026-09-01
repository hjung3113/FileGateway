using System.Globalization;
using FileGateway.Core.Errors;
using FileGateway.Core.Time;
using FileGateway.Core.Tokens;
using FileGateway.Logs;
using FileGateway.Logs.Internal;
using FileGateway.Infrastructure.Tokens;
using FileGateway.Logs.Tokens;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using FileGateway.UnitTests.TestUtils;

namespace FileGateway.UnitTests.Logs;

public class LogCursorTests
{
    // FakeCodec(System.Text.Json)은 TimeSpan 미지원으로 직렬화 자체가 불가 → 실제 codec으로 검증한다.
    private static readonly ITokenCodec Codec = new DataProtectionTokenCodec(
        new ServiceCollection().AddDataProtection().Services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>());

    private static readonly IReadOnlyDictionary<string, string> NoAttrs = new Dictionary<string, string>();
    private static readonly DateTimeOffset From = new(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset To = new(2026, 8, 23, 0, 0, 0, TimeSpan.FromHours(9));

    internal static string Issue(LogListQuery q, DateTimeOffset? lastTs, string? lastName)
        => LogCursor.Encode(Codec, TimeProvider.System, q, lastTs, lastName,
            new EffectiveRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), TimeSpan.FromMinutes(30));

    [Fact]
    public void Binding_same_raw_conditions_matches()
    {
        var attrs = new Dictionary<string, string> { ["lot"] = "7", ["line"] = "2" };
        var q = new LogListQuery("EQ-1", "Event", From, To, "A", attrs, 50, null);
        var token = Issue(q, null, null);

        LogCursor.AssertBinding(Codec, token, q); // 예외 없음

        // 속성 열거 순서가 달라도 정규화(canonical) 후 동일해야 한다
        var reordered = new Dictionary<string, string> { ["line"] = "2", ["lot"] = "7" };
        LogCursor.AssertBinding(Codec, token, q with { Attributes = reordered });
    }

    [Fact]
    public void Binding_different_conditions_throws_InvalidRequest()
    {
        var q = new LogListQuery("EQ-1", "Event", From, To, "A", NoAttrs, 50, null);
        var token = Issue(q, null, null);
        var changed = q with { Subtype = "B" };
        Assert.Equal("InvalidRequest",
            Assert.Throws<FileGatewayException>(() => LogCursor.AssertBinding(Codec, token, changed)).Code);
    }

    [Fact]
    public void Limit_change_is_allowed()
    {
        var q = new LogListQuery("EQ-1", "Event", From, To, "A", NoAttrs, 50, null);
        var token = Issue(q, null, null);
        LogCursor.AssertBinding(Codec, token, q with { Limit = 200 });
    }

    [Fact]
    public void Decode_round_trips_last_position()
    {
        var q = new LogListQuery("EQ-1", "Event", From, To, null, NoAttrs, 50, null);
        var ts = new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.FromHours(9));

        var (lastTs, lastName, _) = LogCursor.Decode(Codec, Issue(q, ts, "2026082218_Event.zip"));
        Assert.Equal(ts, lastTs);
        Assert.Equal("2026082218_Event.zip", lastName);

        var (noTs, noName, _) = LogCursor.Decode(Codec, Issue(q, null, null));
        Assert.Null(noTs);
        Assert.Null(noName);
    }

    [Fact]
    public void Decode_round_trips_effective_range()
    {
        // from/to==null 첫 페이지의 effective range(기본 2일)가 토큰에 그대로 담겨야 한다
        var q = new LogListQuery("EQ-1", "Event", null, null, null, NoAttrs, 50, null);
        var range = new EffectiveRange(
            DateTimeOffset.Parse("2026-08-21T20:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-08-22T20:00:00Z", CultureInfo.InvariantCulture));
        var token = LogCursor.Encode(Codec, TimeProvider.System, q, null, null, range,
            TimeSpan.FromMinutes(30));
        Assert.Equal(range, LogCursor.Decode(Codec, token).EffectiveRange);
    }

    [Fact]
    public void Garbage_or_expired_token_is_rejected()
    {
        var q = new LogListQuery("EQ-1", "Event", From, To, null, NoAttrs, 50, null);
        Assert.Equal("InvalidRequest",
            Assert.Throws<FileGatewayException>(() => LogCursor.Decode(Codec, "not-a-token")).Code);
        var expired = LogCursor.Encode(Codec, TimeProvider.System, q, null, null,
            new EffectiveRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), TimeSpan.FromSeconds(-1));
        Assert.Equal("InvalidRequest",
            Assert.Throws<FileGatewayException>(() => LogCursor.Decode(Codec, expired)).Code);
        Assert.Throws<FileGatewayException>(() => LogCursor.AssertBinding(Codec, expired, q));
    }

    [Fact]
    public void Subtype_null_and_empty_bind_identically()
    {
        var q = new LogListQuery("EQ-1", "Event", From, To, null, NoAttrs, 50, null);
        var token = Issue(q, null, null);

        // ""는 null과 동일 바인딩이어야 한다(빈 subtype로 재요청한 페이지가 바인딩 실패하지 않는다)
        LogCursor.AssertBinding(Codec, token, q with { Subtype = "" });
    }

    [Fact]
    public void Encode_uses_injected_clock_for_issued_at()
    {
        var q = new LogListQuery("EQ-1", "Event", From, To, null, NoAttrs, 50, null);
        var issuedInPast = LogCursor.Encode(Codec,
            new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            q, null, null, new EffectiveRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue),
            TimeSpan.FromMinutes(30));

        // 발급 시각이 주입 시계 기준 과거 → 현재 시점에 만료: clock이 페이로드에 반영됐다는 증거
        Assert.Equal("InvalidRequest",
            Assert.Throws<FileGatewayException>(() => LogCursor.Decode(Codec, issuedInPast)).Code);
    }

    [Fact]
    public void Cross_purpose_token_is_rejected()
    {
        var fileId = Codec.Protect(new TokenPayload(LogTokenKinds.FileIdPurpose,
            new Dictionary<string, string> { ["fileName"] = "f.zip" }, DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5)));
        Assert.Equal("InvalidRequest",
            Assert.Throws<FileGatewayException>(() => LogCursor.Decode(Codec, fileId)).Code);
    }

    [Fact]
    public void Binding_subtype_with_delimiter_round_trips()
    {
        var q = new LogListQuery("EQ-1", "Event", From, To, "A|B", NoAttrs, 50, null);
        var token = Issue(q, null, null);
        LogCursor.AssertBinding(Codec, token, q); // 예외 없음

        // 구분자를 포함한 subtype이 다른 값과 충돌하지 않아야 한다
        Assert.Equal("InvalidRequest",
            Assert.Throws<FileGatewayException>(() =>
                LogCursor.AssertBinding(Codec, token, q with { Subtype = "A", Attributes = new Dictionary<string, string> { ["B"] = "" } })).Code);
    }

    [Fact]
    public void Binding_attribute_value_with_delimiters_round_trips()
    {
        var attrs = new Dictionary<string, string> { ["lot"] = "7&8=9", ["line"] = "2" };
        var q = new LogListQuery("EQ-1", "Event", From, To, null, attrs, 50, null);
        var token = Issue(q, null, null);
        LogCursor.AssertBinding(Codec, token, q); // 예외 없음

        // '&'와 '='가 다른 key/value 구조로 해석되지 않아야 한다
        var crafted = new Dictionary<string, string> { ["lot"] = "7", ["8"] = "9", ["line"] = "2" };
        Assert.Equal("InvalidRequest",
            Assert.Throws<FileGatewayException>(() =>
                LogCursor.AssertBinding(Codec, token, q with { Attributes = crafted })).Code);
    }
}
