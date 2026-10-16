using System.Net.Sockets;

namespace AprsNet.Kiss;

/// <summary>
/// A lasting connection to a KISS TNC over TCP: like <see cref="KissTcpClient"/>, but it reconnects as
/// <see cref="KissConnection"/> describes, so a restarted Dire Wolf or a soundmodem that starts after the app only
/// pauses the reads.
/// </summary>
public sealed class KissTcpConnection : KissConnection
{
    /// <summary>
    /// Creates a connection to the TNC at <paramref name="host"/>:<paramref name="port"/> with the other settings at
    /// their defaults; nothing connects until a read starts.
    /// </summary>
    /// <exception cref="ArgumentException">The host is empty or the port is not 1 to 65535.</exception>
    public KissTcpConnection(string host, int port = KissTcpClient.DefaultPort)
        : this(new KissTcpOptions { Host = host, Port = port }) { }

    /// <summary>Creates a connection with <paramref name="options"/>; nothing connects until a read starts.</summary>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public KissTcpConnection(KissTcpOptions options)
        : this(ConnectTcp(Validated(options)), options) { }

    internal KissTcpConnection(Func<CancellationToken, Task<Stream>> connect, KissTcpOptions options)
        : base(
            connect,
            $"{Validated(options).Host}:{options.Port}",
            options.TncPort,
            options.ReconnectDelay,
            options.MaxReconnectDelay,
            options.TimeProvider,
            options.CreateTransmitLimit()
        ) { }

    private static KissTcpOptions Validated(KissTcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return options;
    }

    private static Func<CancellationToken, Task<Stream>> ConnectTcp(KissTcpOptions options)
    {
        var (host, port, connectTimeout) = (options.Host, options.Port, options.ConnectTimeout);
        return async token =>
        {
            var client = new TcpClient { NoDelay = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(connectTimeout);
            try
            {
                await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
                return new OwningStream(client);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                client.Dispose();
                throw new TimeoutException($"Timed out connecting to the TNC at {host}:{port}.");
            }
            catch
            {
                client.Dispose();
                throw;
            }
        };
    }

    /// <summary>A TCP client's stream that closes the client with it.</summary>
    private sealed class OwningStream(TcpClient client) : Stream
    {
        private readonly NetworkStream _stream = client.GetStream();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _stream.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _stream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _stream.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => _stream.Write(buffer, offset, count);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => _stream.WriteAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stream.Dispose();
                client.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
