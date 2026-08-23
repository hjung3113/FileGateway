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

    [Fact]
    public async Task Sync_dispose_releases_source_and_double_dispose_is_harmless()
    {
        var source = Source("abc"u8.ToArray());
        using var capped = new ExactLengthStream(source, 3);

        capped.Dispose(); // using 문의 sync Dispose 경로 — 이 시점에 source까지 해제돼야 한다
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => capped.ReadAsync(new byte[8], CancellationToken.None).AsTask());
        Assert.Throws<ObjectDisposedException>(() => source.Read(new byte[8], 0, 8));

        await capped.DisposeAsync(); // sync 후 async 이중 해제 무해성
    }
}
