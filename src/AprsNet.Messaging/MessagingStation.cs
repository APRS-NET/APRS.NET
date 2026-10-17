using System.Threading.Channels;
using AprsNet.Packets;

namespace AprsNet.Messaging;

/// <summary>
/// One station's APRS message conversations: message IDs, retries until an ack or rej, reply-acks, and acks for the
/// messages it receives, each delivered once. It does no I/O: the app hands it every packet it receives, from any
/// source, and sends what <see cref="ReadOutgoingAsync"/> hands back, so it works the same over APRS-IS, a KISS TNC or
/// both. See <c>docs/design/messaging.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// One message at a time goes to each station: a second waits until the first is delivered, rejected or failed, as
/// APRS radios expect. Messages to different stations go out side by side. Every message ID is sent in the APRS 1.1
/// reply-ack form <c>{MM}AA</c>, carrying the ack owed to that station.
/// </para>
/// <para>
/// Events fire on the thread that caused them: <see cref="Send"/>, <see cref="Receive"/>, or the timer of
/// <see cref="MessagingStationOptions.TimeProvider"/>. The methods are thread-safe.
/// </para>
/// </remarks>
public sealed class MessagingStation : IDisposable
{
    private static readonly TimeSpan AckRepeatGap = RetryProfile.MinimumGap;

    // Where stations were last heard, so a message to one goes back the same way. Bounded: past this, the half heard
    // longest ago is forgotten.
    private const int MaxHeard = 10_000;

    private readonly MessagingStationOptions _options;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly Channel<OutgoingPacket> _outgoing = Channel.CreateUnbounded<OutgoingPacket>(
        new UnboundedChannelOptions { SingleReader = true }
    );
    private readonly Dictionary<string, Correspondent> _correspondents = new(StringComparer.Ordinal);
    private readonly List<(DateTimeOffset Due, OutgoingPacket Packet)> _repeats = [];
    private readonly Dictionary<string, (string? Source, DateTimeOffset At)> _heard = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly string _key;
    private bool _disposed;

    /// <summary>Creates the conversations of the station <paramref name="station"/>.</summary>
    /// <param name="station">The station's callsign, with its SSID: messages to it are this station's.</param>
    /// <param name="options">Settings; the defaults if none.</param>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public MessagingStation(Callsign station, MessagingStationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(station);
        options ??= new MessagingStationOptions();
        options.Validate();
        Station = station;
        _options = options;
        _time = options.TimeProvider;
        _key = Key(station.Value);
        LastMessageId = options.LastMessageId;
        _timer = _time.CreateTimer(_ => Run(null), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The station's callsign.</summary>
    public Callsign Station { get; }

    /// <summary>
    /// The last message ID used, to save and pass back as <see cref="MessagingStationOptions.LastMessageId"/> after a
    /// restart. <see cref="LastMessageIdChanged"/> fires when it changes.
    /// </summary>
    public string? LastMessageId { get; private set; }

    /// <summary>Fires when <see cref="LastMessageId"/> changes, so the app can save it.</summary>
    public event EventHandler? LastMessageIdChanged;

    /// <summary>Fires once for each message addressed to the station.</summary>
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// Sends <paramref name="text"/> to <paramref name="to"/>: at once, or after the messages already waiting for that
    /// station. It is retried on <paramref name="retryProfile"/> until an ack or rej comes.
    /// </summary>
    /// <param name="to">The station or service to send to.</param>
    /// <param name="text">At most 67 characters, without <c>|</c>, <c>~</c> or <c>{</c>.</param>
    /// <param name="retryProfile">The retry schedule; <see cref="MessagingStationOptions.RetryProfile"/> if none.</param>
    /// <param name="withId">
    /// <see langword="false"/> sends the message once without an ID, so it is never acked or retried, as auto-replies
    /// must be (APRS 1.1, A11-AA-02).
    /// </param>
    /// <returns>The message, to follow its <see cref="OutgoingMessage.State"/>.</returns>
    /// <exception cref="ArgumentException">The text can't be sent, or <paramref name="to"/> is longer than 9 characters.</exception>
    /// <exception cref="ObjectDisposedException">The station has been disposed.</exception>
    public OutgoingMessage Send(Callsign to, string text, RetryProfile? retryProfile = null, bool withId = true)
    {
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 67)
        {
            throw new ArgumentException("A message is at most 67 characters (APRS101 chapter 14).", nameof(text));
        }

        if (text.AsSpan().IndexOfAny("|~{") >= 0 || text.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Message text must not contain '|', '~', '{' or control characters.",
                nameof(text)
            );
        }

        var events = new List<Action>();
        OutgoingMessage message;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var profile = retryProfile ?? _options.RetryProfile;
            string? id = null;
            if (withId)
            {
                id = MessageIds.Next(LastMessageId);
                LastMessageId = id;
                events.Add(() => LastMessageIdChanged?.Invoke(this, EventArgs.Empty));
            }

            message = new OutgoingMessage(to, text, id, withId ? profile : RetryProfile.None);
            var correspondent = Track(to);
            if (!withId)
            {
                // Not acked, so it doesn't wait its turn or hold up the next one.
                Transmit(correspondent, message, _time.GetUtcNow());
                Add(events, message.SetState(OutgoingMessageState.Sent));
                events.Add(message.RaiseAttempted);
            }
            else
            {
                correspondent.Waiting.Enqueue(message);
                Advance(correspondent, _time.GetUtcNow(), events);
            }

            Schedule();
        }

        Raise(events);
        return message;
    }

    /// <summary>
    /// Stops sending <paramref name="message"/>. A queued message is dropped; the next one to that station goes out.
    /// Does nothing to a message that has finished.
    /// </summary>
    public void Cancel(OutgoingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var events = new List<Action>();
        lock (_lock)
        {
            if (
                message.State
                    is OutgoingMessageState.Delivered
                        or OutgoingMessageState.Rejected
                        or OutgoingMessageState.Cancelled
                || !_correspondents.TryGetValue(Key(message.To.Value), out var correspondent)
            )
            {
                return;
            }

            // A failed message is cancelled too, so being heard again doesn't revive it.
            correspondent.Failed.Remove(message);
            message.NextDue = null;
            Add(events, message.SetState(OutgoingMessageState.Cancelled));
            if (correspondent.Active == message)
            {
                correspondent.Active = null;
            }
            else
            {
                correspondent.Waiting = new Queue<OutgoingMessage>(correspondent.Waiting.Where(m => m != message));
            }

            if (!_disposed)
            {
                Advance(correspondent, _time.GetUtcNow(), events);
                Schedule();
            }
        }

        Raise(events);
    }

    /// <summary>
    /// Takes in a packet the app received, from any source. Acks, rejs and reply-acks for the station's messages
    /// settle them; messages to the station are acked and raised once through <see cref="MessageReceived"/>; and a
    /// failed message is tried again when its station is heard. Other packets only note that their sender was heard.
    /// </summary>
    /// <param name="packet">The packet, as received. A third-party packet (<c>}</c>) is read for the packet it carries.</param>
    /// <param name="source">
    /// Where it came from, in the app's own names (for example <c>aprs-is</c> or a TNC's name), handed back in
    /// <see cref="OutgoingPacket.Source"/> so replies can go the same way.
    /// </param>
    /// <exception cref="ObjectDisposedException">The station has been disposed.</exception>
    public void Receive(AprsPacket packet, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Payload is ThirdPartyPayload { Packet: { } inner })
        {
            packet = inner;
        }

        if (!packet.HasHeader)
        {
            return;
        }

        var from = packet.Source;

        var events = new List<Action>();
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = _time.GetUtcNow();
            Heard(Key(from.Value), source, now);
            if (!_correspondents.TryGetValue(Key(from.Value), out var correspondent))
            {
                if (packet.Payload is not MessagePayload toUs || Key(toUs.Addressee) != _key)
                {
                    // A station the station isn't talking to, so nothing to note.
                    return;
                }

                correspondent = Track(from);
            }

            if (packet.Payload is MessagePayload message && Key(message.Addressee) == _key)
            {
                Handle(correspondent, from, packet, message, source, now, events);
            }

            RetryOnHeard(correspondent, now, events);

            Advance(correspondent, now, events);
            Schedule();
        }

        Raise(events);
    }

    /// <summary>Answers <paramref name="message"/> with a rej, for a message the app can't handle.</summary>
    /// <exception cref="ArgumentException"><paramref name="message"/> has no message ID, so it can't be rejected.</exception>
    /// <exception cref="ObjectDisposedException">The station has been disposed.</exception>
    public void Reject(IncomingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.MessageId is null)
        {
            throw new ArgumentException("A message without an ID can't be rejected.", nameof(message));
        }

        var replyAck = (message.Packet.Payload as MessagePayload)?.ReplyAck;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var source = SourceOf(message.From);
            Write(Answer(MessageKind.Rejection, message.From, message.MessageId, replyAck), message.From, source);
        }
    }

    /// <summary>
    /// The messages, acks and rejs to send, as they fall due, until the station is disposed or
    /// <paramref name="cancellationToken"/> is cancelled. Read it with one reader.
    /// </summary>
    public IAsyncEnumerable<OutgoingPacket> ReadOutgoingAsync(CancellationToken cancellationToken = default) =>
        _outgoing.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Takes the next packet to send, if one is waiting, for tests.</summary>
    internal bool TryTakeOutgoing([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out OutgoingPacket? packet) =>
        _outgoing.Reader.TryRead(out packet);

    /// <summary>Stops the timer and ends <see cref="ReadOutgoingAsync"/> once what is already due has been read.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
        _outgoing.Writer.TryComplete();
    }

    private void Handle(
        Correspondent correspondent,
        Callsign from,
        AprsPacket packet,
        MessagePayload message,
        string? source,
        DateTimeOffset now,
        List<Action> events
    )
    {
        switch (message.Kind)
        {
            case MessageKind.Acknowledgement:
                Settle(correspondent, message.MessageId, OutgoingMessageState.Delivered, events);
                return;
            case MessageKind.Rejection:
                Settle(correspondent, message.MessageId, OutgoingMessageState.Rejected, events);
                return;
            case MessageKind.Message:
                break;
            default:
                // Queries and anything else addressed to the station are for the app's own handling.
                return;
        }

        // A reply-ack ({MM}AA) acks the station's message AA (A11-RACK-02).
        if (!string.IsNullOrEmpty(message.ReplyAck))
        {
            Settle(correspondent, message.ReplyAck, OutgoingMessageState.Delivered, events);
        }

        if (message.MessageId is not { } id)
        {
            // No ID: delivered, and not acked (APRS101 chapter 14).
            Deliver(from, packet, message, null, source, now, events);
            return;
        }

        // Every copy is acked, since the sender may have missed the last ack, echoing the whole ID (A11-RACK-04).
        var ack = Answer(MessageKind.Acknowledgement, from, id, message.ReplyAck);
        Write(ack, from, source);
        for (var repeat = 1; repeat < _options.AckRepeats; repeat++)
        {
            _repeats.Add((now + (AckRepeatGap * repeat), new OutgoingPacket(ack, from, source)));
        }

        // Its ID is owed an ack on the station's next message to the sender (A11-RACK-05).
        correspondent.AckOwed = id;

        // Delivered once per sender and ID, with any reply-ack part already stripped (A11-RACK-03).
        Forget(correspondent, now);
        if (correspondent.Received.TryAdd(id, now))
        {
            Deliver(from, packet, message, id, source, now, events);
        }
    }

    private void Deliver(
        Callsign from,
        AprsPacket packet,
        MessagePayload message,
        string? id,
        string? source,
        DateTimeOffset now,
        List<Action> events
    )
    {
        var incoming = new IncomingMessage(from, message.Text, id, now, source, packet);
        events.Add(() => MessageReceived?.Invoke(this, new MessageReceivedEventArgs(incoming)));
    }

    private void Forget(Correspondent correspondent, DateTimeOffset now)
    {
        foreach (var (id, at) in correspondent.Received.ToList())
        {
            if (now - at >= _options.ReceiveWindow)
            {
                correspondent.Received.Remove(id);
            }
        }
    }

    /// <summary>
    /// Settles the message in flight with ID <paramref name="id"/>, or a failed one whose answer came late. IDs match in
    /// any case, since some radios change it.
    /// </summary>
    private static void Settle(Correspondent correspondent, string? id, OutgoingMessageState state, List<Action> events)
    {
        if (correspondent.Active is { } active && Same(active.MessageId, id))
        {
            active.NextDue = null;
            correspondent.Active = null;
            Add(events, active.SetState(state));
            return;
        }

        var failed = correspondent.Failed.Find(m => Same(m.MessageId, id));
        if (failed is not null)
        {
            correspondent.Failed.Remove(failed);
            Add(events, failed.SetState(state));
        }
    }

    private static bool Same(string? a, string? b) =>
        a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gives the failed messages of a station heard again one more try (APRS 1.2, "retry on heard").</summary>
    private void RetryOnHeard(Correspondent correspondent, DateTimeOffset now, List<Action> events)
    {
        var retry = correspondent
            .Failed.Where(m => !m.RetriedOnHeard && now - m.FailedAt < _options.RetryOnHeardWindow)
            .ToList();
        correspondent.Failed.Clear();
        if (retry.Count == 0)
        {
            return;
        }

        // Ahead of anything waiting, oldest first.
        foreach (var message in retry)
        {
            message.RetriedOnHeard = true;
            message.SendsLeft = 1;
            Add(events, message.SetState(OutgoingMessageState.Queued));
        }

        correspondent.Waiting = new Queue<OutgoingMessage>(retry.Concat(correspondent.Waiting));
    }

    /// <summary>Starts the next waiting message to a station when none is in flight.</summary>
    private void Advance(Correspondent correspondent, DateTimeOffset now, List<Action> events)
    {
        if (correspondent.Active is null && correspondent.Waiting.TryDequeue(out var next))
        {
            correspondent.Active = next;
            next.NextDue = now;
        }

        Run(now, events);
    }

    /// <summary>Sends whatever is due, fails what has run out of retries, and sets the timer for the next.</summary>
    private void Run(DateTimeOffset? at, List<Action>? events = null)
    {
        var raise = events is null;
        events ??= [];
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            var now = at ?? _time.GetUtcNow();
            foreach (var correspondent in _correspondents.Values)
            {
                // A message that fails lets the next one start, which may itself be due now.
                while (correspondent.Active is { NextDue: { } due } active && due <= now)
                {
                    if (active.SendsLeft > 0)
                    {
                        Transmit(correspondent, active, now);
                        Add(
                            events,
                            active.SetState(
                                active.Attempts == 1 ? OutgoingMessageState.Sent : OutgoingMessageState.Retrying
                            )
                        );
                        events.Add(active.RaiseAttempted);
                        break;
                    }

                    active.NextDue = null;
                    active.FailedAt = now;
                    correspondent.Active = null;
                    if (!active.RetriedOnHeard && _options.RetryOnHeardWindow > TimeSpan.Zero)
                    {
                        correspondent.Failed.Add(active);
                    }

                    Add(events, active.SetState(OutgoingMessageState.Failed));
                    if (correspondent.Waiting.TryDequeue(out var next))
                    {
                        correspondent.Active = next;
                        next.NextDue = now;
                    }
                }
            }

            for (var i = _repeats.Count - 1; i >= 0; i--)
            {
                if (_repeats[i].Due <= now)
                {
                    _outgoing.Writer.TryWrite(_repeats[i].Packet);
                    _repeats.RemoveAt(i);
                }
            }

            Schedule();
        }

        if (raise)
        {
            Raise(events);
        }
    }

    /// <summary>Sends the message now, with the ack owed to its station, and works out when it is next due.</summary>
    private void Transmit(Correspondent correspondent, OutgoingMessage message, DateTimeOffset now)
    {
        var payload = new MessagePayload
        {
            Addressee = message.To.Value,
            Text = message.Text,
            MessageId = message.MessageId,
            // The reply-ack is added at the moment of sending, so it can change between retries (A11-RACK-01).
            ReplyAck = message.MessageId is null ? null : correspondent.AckOwed ?? "",
        };
        Write(payload, message.To, SourceOf(message.To));
        message.Attempts++;
        message.SendsLeft--;
        message.LastSent = now;
        if (message.MessageId is null)
        {
            message.NextDue = null;
            return;
        }

        var profile = message.RetryProfile;
        message.NextDue =
            now + (message.SendsLeft == 0 ? profile.AckWait : Randomise(profile.RetryDelays[message.Attempts - 1]));
    }

    /// <summary>±10 % (A12-NET-01), but never inside APRS-IS's duplicate window.</summary>
    private static TimeSpan Randomise(TimeSpan delay)
    {
        var factor = 0.9 + (Random.Shared.NextDouble() * 0.2);
        var randomised = delay * factor;
        return randomised < RetryProfile.MinimumGap ? RetryProfile.MinimumGap : randomised;
    }

    private void Schedule()
    {
        if (_disposed)
        {
            return;
        }

        DateTimeOffset? next = null;
        foreach (var correspondent in _correspondents.Values)
        {
            if (correspondent.Active?.NextDue is { } due && (next is null || due < next))
            {
                next = due;
            }
        }

        foreach (var (due, _) in _repeats)
        {
            if (next is null || due < next)
            {
                next = due;
            }
        }

        if (next is null)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var delay = next.Value - _time.GetUtcNow();
        _timer.Change(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);
    }

    private void Heard(string key, string? source, DateTimeOffset now)
    {
        _heard[key] = (source, now);
        if (_heard.Count > MaxHeard)
        {
            foreach (var old in _heard.OrderBy(e => e.Value.At).Take(MaxHeard / 2).Select(e => e.Key).ToList())
            {
                _heard.Remove(old);
            }
        }
    }

    private string? SourceOf(Callsign station) =>
        _heard.TryGetValue(Key(station.Value), out var heard) ? heard.Source : null;

    private void Write(MessagePayload payload, Callsign to, string? source) =>
        _outgoing.Writer.TryWrite(new OutgoingPacket(payload, to, source));

    private static MessagePayload Answer(MessageKind kind, Callsign to, string id, string? replyAck) =>
        new()
        {
            Addressee = to.Value,
            Kind = kind,
            MessageId = id,
            ReplyAck = replyAck,
        };

    private Correspondent Track(Callsign callsign)
    {
        var key = Key(callsign.Value);
        if (!_correspondents.TryGetValue(key, out var correspondent))
        {
            correspondent = new Correspondent();
            _correspondents.Add(key, correspondent);
        }

        return correspondent;
    }

    /// <summary>A callsign as it is matched: in any case, and with <c>-0</c> the same as no SSID.</summary>
    private static string Key(string callsign)
    {
        var key = callsign.Trim().ToUpperInvariant();
        return key.EndsWith("-0", StringComparison.Ordinal) ? key[..^2] : key;
    }

    private static void Add(List<Action> events, Action? action)
    {
        if (action is not null)
        {
            events.Add(action);
        }
    }

    private static void Raise(List<Action> events)
    {
        foreach (var action in events)
        {
            action();
        }
    }

    /// <summary>What the station knows about one station it talks to.</summary>
    private sealed class Correspondent
    {
        public OutgoingMessage? Active { get; set; }

        public Queue<OutgoingMessage> Waiting { get; set; } = new();

        public List<OutgoingMessage> Failed { get; } = [];

        /// <summary>The ID of the last message received from it, owed an ack on the next one sent to it.</summary>
        public string? AckOwed { get; set; }

        /// <summary>The IDs received from it, and when, to deliver each once.</summary>
        public Dictionary<string, DateTimeOffset> Received { get; } = new(StringComparer.Ordinal);
    }
}
