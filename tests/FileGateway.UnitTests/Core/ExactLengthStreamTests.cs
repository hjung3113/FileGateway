using FileGateway.Core.Streams;
namespace FileGateway.UnitTests.Core;

public class ExactLengthStreamTests
{
    private static MemoryStream Source(byte[] data) => new(data);

    [Fact]
    public async Task Reads_exactly_declared_length_when_source_grew()
    {
        await using var capped = new ExactLengthStream(Source("0123456789"u8.ToArray()), 5);
        using var ms = new MemoryStream();
        await capped.CopyToAsync(ms);
        Assert.Equal(5, ms.Length);
    }

    [Fact]
    public async Task Reads_all_when_lengths_match()
    {
        await using var capped = new ExactLengthStream(Source("abc"u8.ToArray()), 3);
        using var ms = new MemoryStream();
        await capped.CopyToAsync(ms);
        Assert.Equal("abc"u8.ToArray(), ms.ToArray());
    }

    [Fact]
    public async Task Throws_when_source_ends_before_declared_length()
    {
        await using var capped = new ExactLengthStream(Source("ab"u8.ToArray()), 5);
        await Assert.ThrowsAsync<EndOfStreamException>(() => capped.CopyToAsync(new MemoryStream()));
    }

    [Fact]
    public async Task Zero_declared_length_returns_empty()
    {
        await using var capped = new ExactLengthStream(Source("ab"u8.ToArray()), 0);
        Assert.Equal(0, await capped.ReadAsync(new byte[8], CancellationToken.None));
    }
}
