using System.Collections.Concurrent;
using System.Globalization;
using AprsNet.Packets;

namespace AprsNet.AprsIsTcpClient;

/// <summary>How a message sent with <see cref="AprsMessenger.SendAsync"/> ended.</summary>
public enum MessageDeliveryStatus
{
    /// <summary>The recipient acknowledged the message (<c>ack</c>, or a reply-ack in its own message).</summary>
    Acknowledged,

    /// <summary>The recipient rejected the message (<c>rej</c>).</summary>
    Rejected,

    /// <summary>Every attempt was sent and none was answered.</summary>
    NotAcknowledged,
}

/// <summary>The outcome of sending one message.</summary>
/// <param name="MessageId">The message ID the messenger gave it.</param>
/// <param name="Status">How it ended.</param>
/// <param name="Attempts">How many times it was sent.</param>
public sealed record MessageDelivery(string MessageId, MessageDeliveryStatus Status, int Attempts);

/// <summary>
/// Sends APRS messages the way APRS101 ch. 14 asks: each gets a message ID and is sent again until the recipient
/// acknowledges or rejects it, waiting longer each time; and incoming messages addressed to this station are
/// acknowledged, with repeats of one already seen acknowledged again but reported once.
/// </summary>
/// <remarks>
/// The messenger does not read the connection itself: pass every received packet to <see cref="HandleAsync"/>, which
/// matches acknowledgements and answers incoming messages. Packets go out through the send function given to the
/// constructor, usually <see cref="AprsIsClient.SendAsync"/>, so the client's rate limit applies to retries too.
/// </remarks>
public sealed class AprsMessenger : IDisposable
{
    private readonly PacketAddress _address;
    private readonly Func<AprsPacket, CancellationToken, ValueTask> _send;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _disposed = new();
    private readonly ConcurrentDictionary<
        (string Station, string Id),
        TaskCompletionSource<MessageDeliveryStatus>
    > _pending = new();
    private readonly Queue<(string Station, string Id)> _seenOrder = new();
    private readonly HashSet<(string Station, string Id)> _seen = [];
    private int _nextId;

    /// <summary>Creates a messenger that sends as <paramref name="address"/>.</summary>
    /// <param name="address">The station's address; messages and acknowledgements are sent from it.</param>
    /// <param name="send">Sends one packet, for example <c>client.SendAsync</c>.</param>
    /// <param name="timeProvider">The clock for retry delays; tests can pass a fake one.</param>
    public AprsMessenger(
        PacketAddress address,
        Func<AprsPacket, CancellationToken, ValueTask> send,
        TimeProvider? timeProvider = null
    )
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(send);
        _address = address;
        _send = send;
        _timeProvider = timeProvider ?? TimeProvider.System;
        // Start somewhere other than 1, so IDs after a restart are unlikely to repeat ones a recipient just saw.
        _nextId = Random.Shared.Next(0, MaxId);
    }

    /// <summary>
    /// How long to wait for an acknowledgement after each attempt; the message is sent once per entry. Defaults to
    /// 30 s, 60 s, 120 s, 240 s and 480 s: five attempts over about 15 minutes, doubling as APRS101 suggests.
    /// </summary>
    public IReadOnlyList<TimeSpan> RetryDelays
    {
        get;
        init =>
            field =
                value is { Count: > 0 } && value.All(d => d > TimeSpan.Zero)
                    ? [.. value]
                    : throw new ArgumentException(
                        "Give at least one retry delay, each longer than zero.",
                        nameof(value)
                    );
    } =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120),
        TimeSpan.FromSeconds(240),
        TimeSpan.FromSeconds(480),
    ];

    /// <summary>
    /// Sends <paramref name="text"/> to <paramref name="addressee"/> and resends it until it is acknowledged or
    /// rejected, or every <see cref="RetryDelays"/> entry has passed.
    /// </summary>
    /// <exception cref="ArgumentException">The addressee or text cannot be sent (see <see cref="AprsPacket.Create"/>).</exception>
    /// <exception cref="OperationCanceledException">Cancelled, or the messenger was disposed.</exception>
    public async Task<MessageDelivery> SendAsync(
        string addressee,
        string text,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(addressee);
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);

        var id = NextId();
        var packet = _address.Create(
            new MessagePayload
            {
                Addressee = addressee,
                Text = text,
                MessageId = id,
            }
        ); // throws here, before anything is sent, if the message cannot be encoded
        var key = (Key(addressee), id);
        var answer = new TaskCompletionSource<MessageDeliveryStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _pending[key] = answer;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposed.Token);
        try
        {
            var attempts = 0;
            foreach (var delay in RetryDelays)
            {
                await _send(packet, stop.Token).ConfigureAwait(false);
                attempts++;
                var timeout = Task.Delay(delay, _timeProvider, stop.Token);
                if (await Task.WhenAny(answer.Task, timeout).ConfigureAwait(false) == answer.Task)
                {
                    return new MessageDelivery(id, await answer.Task.ConfigureAwait(false), attempts);
                }

                await timeout.ConfigureAwait(false); // throws if it ended by cancellation
            }

            return new MessageDelivery(id, MessageDeliveryStatus.NotAcknowledged, attempts);
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Handles a received packet: completes a pending <see cref="SendAsync"/> when it is the acknowledgement or
    /// rejection, and acknowledges a message addressed to this station. Pass every received packet.
    /// </summary>
    /// <returns>
    /// The message when <paramref name="packet"/> is a new message to this station, so the app can show it; otherwise
    /// <see langword="null"/>, including for a repeat of a message already returned.
    /// </returns>
    public async ValueTask<MessagePayload?> HandleAsync(
        AprsPacket packet,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (
            packet is not { Source: { } source, Payload: MessagePayload message }
            || !string.Equals(message.Addressee, _address.Source.Value, StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        switch (message.Kind)
        {
            case MessageKind.Acknowledgement when message.MessageId is { } acked:
                Complete(source, acked, MessageDeliveryStatus.Acknowledged);
                return null;
            case MessageKind.Rejection when message.MessageId is { } rejected:
                Complete(source, rejected, MessageDeliveryStatus.Rejected);
                return null;
            case MessageKind.Message:
                if (message.ReplyAck is { Length: > 0 } replyAck)
                {
                    Complete(source, replyAck, MessageDeliveryStatus.Acknowledged); // APRS 1.1 reply-ack
                }

                if (message.MessageId is not { } id)
                {
                    return message; // no ID: the sender does not want an acknowledgement
                }

                await _send(
                        _address.Create(
                            new MessagePayload
                            {
                                Addressee = source.Value,
                                Kind = MessageKind.Acknowledgement,
                                MessageId = id,
                            }
                        ),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                return FirstTime((Key(source.Value), id)) ? message : null;
            default:
                return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed.Cancel(); // not disposed: SendAsync calls already running still link to its token
    }

    private const int MaxId = 100_000; // IDs are 1 to 5 characters; numbers keep them simple

    private string NextId() =>
        ((Interlocked.Increment(ref _nextId) % (MaxId - 1)) + 1).ToString(CultureInfo.InvariantCulture);

    private static string Key(string station) => station.Trim().ToUpperInvariant();

    private void Complete(Callsign from, string id, MessageDeliveryStatus status)
    {
        if (_pending.TryGetValue((Key(from.Value), id), out var answer))
        {
            answer.TrySetResult(status);
        }
    }

    /// <summary>Remembers the last 1,000 incoming messages, so retries of one are reported once.</summary>
    private bool FirstTime((string Station, string Id) key)
    {
        lock (_seen)
        {
            if (!_seen.Add(key))
            {
                return false;
            }

            _seenOrder.Enqueue(key);
            if (_seenOrder.Count > 1000)
            {
                _seen.Remove(_seenOrder.Dequeue());
            }

            return true;
        }
    }
}
