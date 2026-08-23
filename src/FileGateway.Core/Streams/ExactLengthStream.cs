namespace FileGateway.Core.Streams;

/// <summary>선언 길이까지만 전송하고(파일 growth 무시), 선언 길이 전에 소스가 끝나면 실패(truncate/rotation).</summary>
public sealed class ExactLengthStream(Stream source, long declaredLength) : Stream
{
    private readonly long _declaredLength = declaredLength;
    private long _remaining = declaredLength;
    private int _disposed; // sync/async 경로 간 이중 해제 방지
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _declaredLength;
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (_remaining == 0) return 0;
        var toRead = (int)Math.Min(buffer.Length, _remaining);
        var read = await source.ReadAsync(buffer[..toRead], ct);
        if (read == 0)
            throw new EndOfStreamException(
                $"remote stream ended after {_declaredLength - _remaining} of {_declaredLength} declared bytes");
        _remaining -= read;
        return read;
    }

    // 기본 Stream.DisposeAsync는 Dispose()를 호출하므로 Dispose(bool) 재진입 대신 _disposed로만 방지한다.
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        base.Dispose(disposing);
        if (disposing) source.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        await source.DisposeAsync();
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int ReadTimeout { get => source.ReadTimeout; set => source.ReadTimeout = value; }
}
