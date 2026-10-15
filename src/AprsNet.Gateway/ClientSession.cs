using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using AprsNet.Filters;
using AprsNet.Packets;

namespace AprsNet.Gateway;

/// <summary>One connected client: its login, its filter and the lines queued for it.</summary>
internal sealed class ClientSession : IDisposable
{
    // APRS-IS lines are at most 512 bytes; a little more leaves room for a long filter command.
    private const int MaxLineLength = 1024;

    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly AprsIsServerOptions _options;
    private readonly Channel<ReadOnlyMemory<byte>> _queue;
    private readonly CancellationTokenSource _closing;
    private volatile AprsFilter _filter = AprsFilter.Empty;
    private volatile LoginLine? _login;
    private readonly Action<ClientSession, LoginLine> _join;

    public ClientSession(
        TcpClient tcp,
        AprsIsServerOptions options,
        Action<ClientSession, LoginLine> join,
        CancellationToken serverStopping
    )
    {
        _join = join;
        _tcp = tcp;
        _stream = tcp.GetStream();
        _options = options;
        _queue = Channel.CreateBounded<ReadOnlyMemory<byte>>(
            new BoundedChannelOptions(options.ClientQueueLength) { SingleReader = true }
        );
        _closing = CancellationTokenSource.CreateLinkedTokenSource(serverStopping);
    }

    /// <summary>Whether the client was dropped for falling too far behind.</summary>
    public bool Overflowed { get; private set; }

    /// <summary>Whether the session ended on an unexpected exception.</summary>
    public bool Faulted { get; private set; }

    public async Task RunAsync()
    {
        var token = _closing.Token;
        try
        {
            await WriteNowAsync($"# {_options.SoftwareName} {_options.SoftwareVersion}", token).ConfigureAwait(false);
            var reader = new BoundedLineReader(_stream, MaxLineLength);
            var line = await reader
                .ReadLineAsync(token)
                .AsTask()
                .WaitAsync(_options.LoginTimeout, _options.TimeProvider, token)
                .ConfigureAwait(false);
            var login = line is null ? null : LoginLine.TryParse(line);
            if (login is null)
            {
                return;
            }

            // Ready for packets before the response goes out, so a client that has read it misses nothing; anything
            // published meanwhile waits in the queue until the writer starts.
            var filterReply = login.Filter.Length > 0 ? ApplyFilter(login.Filter) : null;
            using var keepAlive = new PeriodicTimer(_options.KeepAliveInterval, _options.TimeProvider);
            _join(this, login);
            var status = login.IsVerified ? "verified" : "unverified";
            await WriteNowAsync($"# logresp {login.Callsign} {status}, server {_options.ServerName}", token)
                .ConfigureAwait(false);
            if (filterReply is not null)
            {
                await WriteNowAsync(filterReply, token).ConfigureAwait(false);
            }

            var writing = WriteQueuedAsync(token);
            var keepingAlive = KeepAliveAsync(keepAlive, token);
            var reading = ReadCommandsAsync(reader, token);
            await Task.WhenAny(writing, keepingAlive, reading).ConfigureAwait(false);
            await _closing.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(writing, keepingAlive, reading).ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or TimeoutException or IOException or SocketException)
        {
            // The client left, stalled, was too slow, or the server is stopping: in every case the session ends.
        }
        catch (Exception)
        {
            // Anything else is a fault in this session alone; it must not reach the server or the other clients.
            Faulted = true;
        }
        finally
        {
            await _closing.CancelAsync().ConfigureAwait(false);
            _tcp.Close();
        }
    }

    /// <summary>Marks the client logged in, so <see cref="Offer"/> queues packets for it.</summary>
    public void Activate(LoginLine login) => _login = login;

    /// <summary>Queues the packet if the client's filter passes it, or it is a message to the client.</summary>
    public void Offer(AprsPacket packet, ReadOnlyMemory<byte> line)
    {
        if (_login is not { } login || _closing.IsCancellationRequested)
        {
            return;
        }

        var addressedToClient =
            packet.Payload is MessagePayload message
            && message.Addressee.Equals(login.Callsign, StringComparison.OrdinalIgnoreCase);
        if (!addressedToClient && !_filter.Matches(packet))
        {
            return;
        }

        if (!_queue.Writer.TryWrite(line))
        {
            Drop();
        }
    }

    public void Dispose()
    {
        _closing.Dispose();
        _tcp.Dispose();
    }

    private string ApplyFilter(string text)
    {
        if (!AprsFilter.TryParse(text, out var filter, out var error))
        {
            return $"# filter {text} is invalid: {error}";
        }

        _filter = filter;
        return $"# filter {filter} active";
    }

    private async Task ReadCommandsAsync(BoundedLineReader reader, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            // Only #filter is a command; other comments and packets are ignored until clients can send.
            if (line.StartsWith("#filter", StringComparison.OrdinalIgnoreCase))
            {
                Enqueue(ApplyFilter(line["#filter".Length..].Trim()));
            }
        }
    }

    private async Task KeepAliveAsync(PeriodicTimer timer, CancellationToken token)
    {
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            var now = _options.TimeProvider.GetUtcNow().ToString("dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            Enqueue($"# {_options.SoftwareName} {_options.SoftwareVersion} {now} GMT {_options.ServerName}");
        }
    }

    private async Task WriteQueuedAsync(CancellationToken token)
    {
        var reader = _queue.Reader;
        while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
        {
            while (reader.TryRead(out var line))
            {
                await _stream.WriteAsync(line, token).ConfigureAwait(false);
            }

            await _stream.FlushAsync(token).ConfigureAwait(false);
        }
    }

    private void Enqueue(string comment)
    {
        if (!_queue.Writer.TryWrite(Line(comment)))
        {
            Drop();
        }
    }

    private void Drop()
    {
        Overflowed = true;
        try
        {
            _closing.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Publish raced the end of the session; it is already closed.
        }
    }

    private async Task WriteNowAsync(string comment, CancellationToken token)
    {
        await _stream.WriteAsync(Line(comment), token).ConfigureAwait(false);
        await _stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static byte[] Line(string text) => Encoding.Latin1.GetBytes(text + "\r\n");
}
