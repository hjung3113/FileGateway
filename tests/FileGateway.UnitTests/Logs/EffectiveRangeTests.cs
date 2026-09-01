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

    [Theory]
    [InlineData(GenerationType.Hourly)]
    [InlineData(GenerationType.Daily)]
    public void No_bounds_defaults_to_last_two_days(GenerationType type)
    {
        // 고정 시계로 From/To를 정확히 검증한다(기본: now-2d ~ now).
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero));
        var r = EffectiveRangePlanner.Normalize(Q(), type, Max, clock);
        Assert.Equal(new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero), r.From);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero), r.To);
    }

    [Theory]
    [InlineData(GenerationType.Hourly)]
    [InlineData(GenerationType.Daily)]
    public void From_only_extends_two_days(GenerationType type)
        => Assert.Equal(TimeSpan.FromDays(2),
             EffectiveRangePlanner.Normalize(Q(F), type, Max, Sys).To - F);

    [Theory]
    [InlineData(GenerationType.Hourly)]
    [InlineData(GenerationType.Daily)]
    public void Explicit_bounds_are_preserved(GenerationType type)
    {
        var to = F.AddHours(6);
        var range = EffectiveRangePlanner.Normalize(Q(F, to), type, Max, Sys);
        Assert.Equal(F, range.From);
        Assert.Equal(to, range.To);
    }

    [Theory]
    [InlineData(GenerationType.Hourly)]
    [InlineData(GenerationType.Daily)]
    public void To_only_is_invalid(GenerationType type)
        => Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(to: F), type, Max, Sys)).Code);

    [Theory]
    [InlineData(GenerationType.Hourly, 0)]
    [InlineData(GenerationType.Hourly, -1)]
    [InlineData(GenerationType.Daily, 0)]
    [InlineData(GenerationType.Daily, -1)]
    public void From_gte_to_invalid(GenerationType type, int deltaHours)
        => Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(F, F.AddHours(deltaHours)), type, Max, Sys)).Code);

    [Theory]
    [InlineData(GenerationType.Hourly)]
    [InlineData(GenerationType.Daily)]
    public void Over_max_range_invalid(GenerationType type)
        => Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(F, F.AddDays(32)), type, Max, Sys)).Code);

    [Fact] public void Continuous_rejects_any_time_bound()
    {
        Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(F), GenerationType.Continuous, Max, Sys)).Code);
        Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(to: F), GenerationType.Continuous, Max, Sys)).Code);
    }
}
