namespace FileGateway.Core.Streams;

/// <summary>선언 길이까지만 전송하고(파일 growth 무시), 선언 길이 전에 소스가 끝나면 실패(truncate/rotation).</summary>
public sealed class ExactLengthStream(Stream source, long declaredLength) : Stream
{
    private readonly long _declaredLength = declaredLength;
    private long _remaining = declaredLength;
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

    public override async ValueTask DisposeAsync() { await source.DisposeAsync(); base.DisposeAsync().AsTask().Dispose(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int ReadTimeout { get => source.ReadTimeout; set => source.ReadTimeout = value; }
}
