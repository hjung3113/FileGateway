using FileGateway.Core.Errors;
using FileGateway.Core.Time;
using FileGateway.Logs;
using FileGateway.Logs.Definitions;
using FileGateway.UnitTests.TestUtils;

namespace FileGateway.UnitTests.Logs;

public class EffectiveRangeTests
{
    private static readonly TimeSpan Max = TimeSpan.FromDays(31);
    private static readonly DateTimeOffset F = new(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9));
    private static readonly IReadOnlyDictionary<string, string> NoAttrs = new Dictionary<string, string>();
    private static readonly TimeProvider Sys = TimeProvider.System;

    private static LogListQuery Q(DateTimeOffset? from = null, DateTimeOffset? to = null)
        => new("EQ-001", "EventLog", from, to, null, NoAttrs, null, null);

    [Fact] public void No_bounds_defaults_to_last_24h()
    {
        // 고정 시계로 From/To를 정확히 검증한다(기본: now-24h ~ now).
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero));
        var r = EffectiveRangePlanner.Normalize(Q(), GenerationType.Hourly, Max, clock);
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.FromHours(9)), r.From);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.FromHours(9)), r.To);
    }

    [Fact] public void From_only_extends_two_days()
        => Assert.Equal(TimeSpan.FromDays(2),
             EffectiveRangePlanner.Normalize(Q(F), GenerationType.Hourly, Max, Sys).To - F);

    [Fact] public void To_only_is_invalid()
        => Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(to: F), GenerationType.Hourly, Max, Sys)).Code);

    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public void From_gte_to_invalid(int deltaHours)
        => Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(F, F.AddHours(deltaHours)), GenerationType.Hourly, Max, Sys)).Code);

    [Fact] public void Over_max_range_invalid()
        => Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(F, F.AddDays(32)), GenerationType.Hourly, Max, Sys)).Code);

    [Fact] public void Continuous_rejects_any_time_bound()
    {
        Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(F), GenerationType.Continuous, Max, Sys)).Code);
        Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(to: F), GenerationType.Continuous, Max, Sys)).Code);
    }
}
