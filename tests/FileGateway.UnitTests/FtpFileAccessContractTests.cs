using System.Reflection;
using System.Net.Sockets;
using FileGateway.Core.Files;
using FileGateway.Infrastructure.Ftp;
using FluentFTP;
using FluentFTP.Exceptions;

namespace FileGateway.UnitTests.Infrastructure.Ftp;

public sealed class FtpFileAccessContractTests
{
    [Fact]
    public void IsFileNotFoundReply_does_not_treat_550_in_message_as_missing_status()
    {
        var unrelated = new FtpCommandException("500", "server diagnostic includes 550");
        var missing = new FtpCommandException("550", "file unavailable");

        Assert.False(InvokeIsFileNotFoundReply(unrelated));
        Assert.True(InvokeIsFileNotFoundReply(missing));
    }

    [Fact]
    public void FtpOptions_uses_implicit_tls_default_port_and_honors_override()
    {
        var method = typeof(FtpOptions).GetMethod(
            "ResolveHostPort", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.Equal(990, InvokeResolveHostPort(method!, new FtpOptions
            { Security = FtpSecurity.ImplicitTls }));
        Assert.Equal(21, InvokeResolveHostPort(method!, new FtpOptions()));
        Assert.Equal(2021, InvokeResolveHostPort(method!, new FtpOptions
            { Security = FtpSecurity.ImplicitTls, HostPortOverride = 2021 }));
    }

    [Fact]
    public void Classify_prefers_socket_failure_inside_io_exception()
    {
        var socket = new SocketException((int)SocketError.ConnectionReset);
        var wrapped = new IOException("stream reset", socket);

        var ex = InvokeClassify(wrapped);

        Assert.Equal(FileAccessError.ConnectionFailed, ex.Error);
    }

    [Fact]
    public async Task Sync_stream_read_maps_IOException_to_IoFailure()
    {
        await using var stream = await CreateOwnedStreamAsync(new ThrowingReadStream());

        var ex = Assert.Throws<FileAccessException>(() => stream.Read(new byte[1], 0, 1));

        Assert.Equal(FileAccessError.IoFailure, ex.Error);
    }

    [Fact]
    public async Task Async_stream_read_maps_IOException_to_IoFailure()
    {
        await using var stream = await CreateOwnedStreamAsync(new ThrowingReadStream());

        var ex = await Assert.ThrowsAsync<FileAccessException>(async () =>
            await stream.CopyToAsync(new MemoryStream()));

        Assert.Equal(FileAccessError.IoFailure, ex.Error);
    }

    private static bool InvokeIsFileNotFoundReply(FtpException exception)
    {
        var method = typeof(FtpFileAccess).GetMethod(
            "IsFileNotFoundReply", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("IsFileNotFoundReply was not found.");
        return (bool)(method.Invoke(null, [exception])
            ?? throw new InvalidOperationException("IsFileNotFoundReply returned no value."));
    }

    private static int InvokeResolveHostPort(MethodInfo method, FtpOptions options)
        => (int)(method.Invoke(null, [options])
            ?? throw new InvalidOperationException("ResolveHostPort returned no value."));

    private static FileAccessException InvokeClassify(Exception exception)
    {
        var method = typeof(FtpFileAccess).GetMethod(
            "Classify", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Classify was not found.");
        return (FileAccessException)(method.Invoke(null, [exception])
            ?? throw new InvalidOperationException("Classify returned no value."));
    }

    private static async Task<Stream> CreateOwnedStreamAsync(Stream inner)
    {
        var options = new FtpOptions { MaxConcurrentGlobal = 1, MaxConcurrentPerServer = 1 };
        var limiter = new FtpConcurrencyLimiter(options);
        var server = new FileServerConnection("S1", "127.0.0.1", "/");
        var lease = await limiter.AcquireAsync(server, CancellationToken.None);
        var client = new AsyncFtpClient("127.0.0.1", "anonymous", "", 1);

        try
        {
            var streamType = typeof(FtpFileAccess).GetNestedType(
                "OwnedFtpStream", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("OwnedFtpStream was not found.");
            var constructor = streamType.GetConstructors(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .SingleOrDefault()
                ?? throw new InvalidOperationException("OwnedFtpStream constructor was not found.");
            return (Stream)(constructor.Invoke([inner, client, lease])
                ?? throw new InvalidOperationException("OwnedFtpStream was not created."));
        }
        catch
        {
            await client.DisposeAsync();
            await lease.DisposeAsync();
            throw;
        }
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
            => throw new IOException("simulated streaming read failure");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
            => ValueTask.FromException<int>(new IOException("simulated streaming read failure"));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
