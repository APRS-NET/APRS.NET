using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AprsNet.Filters;
using AprsNet.Packets;
using AprsNet.Packets.Json;

namespace AprsNet.Gateway;

/// <summary>One WebSocket client of an <see cref="AprsWebSocketHub"/>: its filter and the messages queued for it.</summary>
internal sealed class WebSocketSession : IPacketSubscriber
{
    private readonly WebSocket _socket;
    private readonly AprsWebSocketHub _hub;
    private readonly AprsWebSocketHubOptions _options;
    private readonly string? _callsign;
    private readonly Channel<ReadOnlyMemory<byte>> _queue;
    private volatile AprsFilter _filter = AprsFilter.Empty;
    private AprsFilterContext? _context;
    private CancellationTokenSource? _closing;
    private bool _tooLong;

    public WebSocketSession(WebSocket socket, AprsWebSocketHub hub, AprsWebSocketHubOptions options, string? callsign)
    {
        _socket = socket;
        _hub = hub;
        _options = options;
        _callsign = string.IsNullOrWhiteSpace(callsign) ? null : callsign.Trim().ToUpperInvariant();
        _queue = Channel.CreateBounded<ReadOnlyMemory<byte>>(
            new BoundedChannelOptions(options.ClientQueueLength) { SingleReader = true }
        );
    }

    public bool Overflowed { get; private set; }

    public bool Faulted { get; private set; }

    public string EndReason =>
        Faulted ? "error"
        : Overflowed ? "overflow"
        : _tooLong ? "invalid_message"
        : "closed";

    public async Task RunAsync(string? filter, CancellationToken stopping)
    {
        using var closing = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _closing = closing;
        try
        {
            var server = _hub.Server;
            var reply = string.IsNullOrWhiteSpace(filter) ? null : ApplyFilter(filter.Trim());
            await SendAsync(
                    Json(w =>
                    {
                        w.WriteString("server", server.Name);
                        w.WriteString("software", $"{server.SoftwareName} {server.SoftwareVersion}");
                        if (_filter.IsEmpty)
                        {
                            w.WriteNull("filter");
                        }
                        else
                        {
                            w.WriteString("filter", _filter.ToString());
                        }
                    })
                )
                .ConfigureAwait(false);
            if (reply is not null && _filter.IsEmpty)
            {
                await SendAsync(reply.Value).ConfigureAwait(false);
            }

            // Subscribed after the greeting, so the history follows it.
            server.Subscribe(this);

            // Cancelling a WebSocket send or receive aborts the socket, so they take no token: the writer stops
            // between messages, and the reader when the client answers the close.
            var writing = WriteQueuedAsync(closing.Token);
            var reading = ReadMessagesAsync();
            await Task.WhenAny(writing, reading).ConfigureAwait(false);
            await closing.CancelAsync().ConfigureAwait(false);
            if (Overflowed)
            {
                // It isn't reading, so a close would wait behind everything already queued in the network.
                _socket.Abort();
                return;
            }

            if (!await FinishesAsync(writing).ConfigureAwait(false))
            {
                _socket.Abort();
                return;
            }

            await CloseOutputAsync(stopping).ConfigureAwait(false);
            if (!await FinishesAsync(reading).ConfigureAwait(false))
            {
                _socket.Abort();
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException)
        {
            // The client left, or the hub is stopping.
            _socket.Abort();
        }
        catch (Exception)
        {
            Faulted = true;
            _socket.Abort();
        }
        finally
        {
            _closing = null;
        }
    }

    // The status the session closes with, and a short reason.
    private async Task CloseOutputAsync(CancellationToken stopping)
    {
        var (status, reason) =
            _tooLong ? (WebSocketCloseStatus.MessageTooBig, "Message too long")
            : stopping.IsCancellationRequested ? (WebSocketCloseStatus.EndpointUnavailable, "Server stopping")
            : (WebSocketCloseStatus.NormalClosure, "");
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            var closed = _socket.CloseOutputAsync(status, reason, CancellationToken.None);
            if (!await FinishesAsync(closed).ConfigureAwait(false))
            {
                _socket.Abort();
            }
        }
    }

    // Whether the task ends within a couple of seconds of real time; a stuck network gets no longer.
    private static async Task<bool> FinishesAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2), TimeProvider.System).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException)
        {
            return true;
        }
    }

    public void Offer(PublishedPacket published)
    {
        var packet = published.Packet;
        var addressedToClient =
            _callsign is not null
            && packet.Payload is MessagePayload message
            && message.Addressee.Equals(_callsign, StringComparison.OrdinalIgnoreCase);
        if (!addressedToClient && !_filter.Matches(packet, Context()))
        {
            return;
        }

        if (_queue.Writer.TryWrite(published.Json))
        {
            GatewayMetrics.Queued(_hub.Server.Name, GatewayMetrics.WebSocket);
        }
        else
        {
            Drop();
        }
    }

    // As for APRS-IS clients: m/ measures from where the client's callsign was last heard.
    private AprsFilterContext? Context()
    {
        if (_hub.Server.Stations is not { } stations)
        {
            return null;
        }

        var mine = _callsign is null ? null : stations.LastPosition(_callsign);
        if (_context is null || _context.MyPosition != mine)
        {
            _context = new AprsFilterContext { MyPosition = mine, LastPosition = stations.LastPosition };
        }

        return _context;
    }

    private ReadOnlyMemory<byte>? ApplyFilter(string text)
    {
        if (!AprsFilter.TryParse(text, out var filter, out var error))
        {
            return Json(w =>
            {
                w.WriteString("filter", text);
                w.WriteString("error", error);
            });
        }

        _filter = filter;
        return Json(w =>
        {
            w.WriteString("filter", filter.ToString());
            w.WriteBoolean("active", true);
        });
    }

    private async Task ReadMessagesAsync()
    {
        var buffer = new byte[_options.MaxMessageBytes];
        while (true)
        {
            var length = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                if (length == buffer.Length)
                {
                    _tooLong = true;
                    return;
                }

                result = await _socket
                    .ReceiveAsync(buffer.AsMemory(length), CancellationToken.None)
                    .ConfigureAwait(false);
                length += result.Count;
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            Enqueue(Command(buffer.AsSpan(0, length)));
        }
    }

    private ReadOnlyMemory<byte> Command(ReadOnlySpan<byte> message)
    {
        try
        {
            using var document = JsonDocument.Parse(message.ToArray());
            if (
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("filter", out var filter)
                && filter.ValueKind == JsonValueKind.String
            )
            {
                return ApplyFilter(filter.GetString()!.Trim())!.Value;
            }
        }
        catch (JsonException)
        {
            // Answered below.
        }

        return Json(w =>
            w.WriteString("error", "Send {\"filter\":\"...\"} to set a filter; nothing else is accepted.")
        );
    }

    private async Task WriteQueuedAsync(CancellationToken token)
    {
        var reader = _queue.Reader;
        while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
        {
            while (!token.IsCancellationRequested && reader.TryRead(out var message))
            {
                await SendAsync(message).ConfigureAwait(false);
            }
        }
    }

    private Task SendAsync(ReadOnlyMemory<byte> message) =>
        _socket.SendAsync(message, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None).AsTask();

    private void Enqueue(ReadOnlyMemory<byte> message)
    {
        if (!_queue.Writer.TryWrite(message))
        {
            Drop();
        }
    }

    private void Drop()
    {
        Overflowed = true;
        try
        {
            _closing?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Publish raced the end of the session; it is already closed.
        }
    }

    private static ReadOnlyMemory<byte> Json(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, AprsPacketJson.WriterOptions))
        {
            json.WriteStartObject();
            write(json);
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }
}
