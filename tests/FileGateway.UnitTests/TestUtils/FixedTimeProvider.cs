namespace FileGateway.UnitTests.TestUtils;

/// <summary>고정 시각을 반환하는 TimeProvider — 발급/정규화 시각을 결정적으로 만든다(GetUtcNow 계약대로 offset 0).</summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
