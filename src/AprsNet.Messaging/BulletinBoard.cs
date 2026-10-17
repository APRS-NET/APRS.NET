using AprsNet.Packets;

namespace AprsNet.Messaging;

/// <summary>A bulletin, announcement or NWS bulletin on a <see cref="BulletinBoard"/>.</summary>
/// <param name="From">The station that sent it.</param>
/// <param name="Kind">
/// <see cref="MessageKind.Bulletin"/> (<c>BLN0</c>–<c>BLN9</c>), <see cref="MessageKind.Announcement"/>
/// (<c>BLNA</c>–<c>BLNZ</c>) or <see cref="MessageKind.NwsBulletin"/> (<c>NWS-…</c> or <c>NWS_…</c>).
/// </param>
/// <param name="Addressee">The addressee as sent, for example <c>BLN3</c>, <c>BLN4WX</c> or <c>NWS-WARN</c>.</param>
/// <param name="Id">
/// The bulletin or announcement identifier (<c>3</c> for <c>BLN3</c>, <c>Q</c> for <c>BLNQ</c>); for an NWS bulletin,
/// its message ID, or <see langword="null"/> without one.
/// </param>
/// <param name="Group">The group of a group bulletin or announcement, such as <c>WX</c> in <c>BLN4WX</c>.</param>
/// <param name="Text">The text.</param>
/// <param name="FirstHeard">When this text was first heard.</param>
/// <param name="LastHeard">When it was last heard, by any source.</param>
/// <param name="Source">The source it was last heard on, as named in <see cref="BulletinBoard.Receive"/>.</param>
public sealed record Bulletin(
    Callsign From,
    MessageKind Kind,
    string Addressee,
    string? Id,
    string? Group,
    string Text,
    DateTimeOffset FirstHeard,
    DateTimeOffset LastHeard,
    string? Source
);

/// <summary>What happened to a bulletin on a <see cref="BulletinBoard"/>.</summary>
public enum BulletinChange
{
    /// <summary>A bulletin not on the board was heard.</summary>
    Added,

    /// <summary>Its sender sent the same bulletin with different text, which replaced the old text.</summary>
    Replaced,

    /// <summary>
    /// It was not heard for <see cref="BulletinBoardOptions.Expiry"/>, or was dropped to stay within
    /// <see cref="BulletinBoardOptions.MaxBulletins"/>.
    /// </summary>
    Removed,
}

/// <summary>A change on a <see cref="BulletinBoard"/>.</summary>
public sealed class BulletinChangedEventArgs : EventArgs
{
    internal BulletinChangedEventArgs(BulletinChange change, Bulletin bulletin, Bulletin? previous)
    {
        Change = change;
        Bulletin = bulletin;
        Previous = previous;
    }

    /// <summary>What happened.</summary>
    public BulletinChange Change { get; }

    /// <summary>The bulletin as it is now, or as it was when removed.</summary>
    public Bulletin Bulletin { get; }

    /// <summary>For <see cref="BulletinChange.Replaced"/>, the bulletin it replaced.</summary>
    public Bulletin? Previous { get; }
}

/// <summary>Settings for a <see cref="BulletinBoard"/>.</summary>
public sealed class BulletinBoardOptions
{
    /// <summary>How long a bulletin stays without being heard again. A day by default.</summary>
    public TimeSpan Expiry { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// The most bulletins kept; past it, the one heard longest ago is removed. 1,000 by default, plenty for a full
    /// APRS-IS feed.
    /// </summary>
    public int MaxBulletins { get; set; } = 1000;

    /// <summary>The clock for expiry. The system clock by default; tests can pass a fake one.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Checks that the settings can be used.</summary>
    /// <exception cref="ArgumentException">A setting is unusable; the message names it.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Expiry, TimeSpan.Zero, nameof(Expiry));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxBulletins, 1, nameof(MaxBulletins));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
    }
}

/// <summary>
/// The bulletins, announcements and NWS bulletins heard, from anyone, kept up to date: a sender's new text for the same
/// bulletin replaces the old, repeats change nothing, and a bulletin not heard for
/// <see cref="BulletinBoardOptions.Expiry"/> is removed. It does no I/O; hand it every packet received.
/// </summary>
/// <remarks>
/// Bulletins and announcements are kept per sender, kind, identifier and group, so <c>BLN1</c> from two stations are two
/// bulletins. NWS bulletins are kept per sender, addressee and message ID (or text, without an ID), since one sender
/// sends many different warnings to <c>NWS-WARN</c>. <see cref="Changed"/> fires on the thread that caused the
/// change: <see cref="Receive"/>, or the expiry timer. The methods are thread-safe.
/// </remarks>
public sealed class BulletinBoard : IDisposable
{
    private readonly BulletinBoardOptions _options;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly Dictionary<(string From, MessageKind Kind, string Addressee, string Key), Bulletin> _bulletins =
    [];
    private readonly Lock _lock = new();
    private bool _disposed;

    /// <summary>Creates an empty board.</summary>
    /// <param name="options">Settings; the defaults if none.</param>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public BulletinBoard(BulletinBoardOptions? options = null)
    {
        options ??= new BulletinBoardOptions();
        options.Validate();
        _options = options;
        _time = options.TimeProvider;
        _timer = _time.CreateTimer(_ => Expire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Fires when a bulletin is added, replaced or removed.</summary>
    public event EventHandler<BulletinChangedEventArgs>? Changed;

    /// <summary>
    /// The bulletins now on the board: bulletins, then announcements, then NWS bulletins, each by group, identifier and
    /// sender.
    /// </summary>
    public IReadOnlyList<Bulletin> Bulletins
    {
        get
        {
            lock (_lock)
            {
                return
                [
                    .. _bulletins
                        .Values.OrderBy(b => b.Kind)
                        .ThenBy(b => b.Group, StringComparer.Ordinal)
                        .ThenBy(b => b.Id, StringComparer.Ordinal)
                        .ThenBy(b => b.From.Value, StringComparer.Ordinal)
                        .ThenBy(b => b.Addressee, StringComparer.Ordinal),
                ];
            }
        }
    }

    /// <summary>
    /// Takes in a packet the app received, from any source. Returns whether it was a bulletin, announcement or NWS
    /// bulletin; anything else is ignored.
    /// </summary>
    /// <param name="packet">The packet, as received. A third-party packet (<c>}</c>) is read for the packet it carries.</param>
    /// <param name="source">Where it came from, in the app's own names, kept as <see cref="Bulletin.Source"/>.</param>
    /// <exception cref="ObjectDisposedException">The board has been disposed.</exception>
    public bool Receive(AprsPacket packet, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Payload is ThirdPartyPayload { Packet: { } inner })
        {
            packet = inner;
        }

        if (!packet.HasHeader || packet.Payload is not MessagePayload message || Kind(message) is not { } kind)
        {
            return false;
        }

        var from = packet.Source;
        var id = kind == MessageKind.NwsBulletin ? message.MessageId : message.BulletinId;
        var key = (from.Value.ToUpperInvariant(), kind, message.Addressee, id ?? message.Text);
        var events = new List<BulletinChangedEventArgs>();
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = _time.GetUtcNow();
            if (_bulletins.TryGetValue(key, out var existing))
            {
                if (existing.Text == message.Text)
                {
                    // A repeat: still current, nothing new to show.
                    _bulletins[key] = existing with
                    {
                        LastHeard = now,
                        Source = source,
                    };
                }
                else
                {
                    var replaced = existing with
                    {
                        Text = message.Text,
                        FirstHeard = now,
                        LastHeard = now,
                        Source = source,
                    };
                    _bulletins[key] = replaced;
                    events.Add(new BulletinChangedEventArgs(BulletinChange.Replaced, replaced, existing));
                }
            }
            else
            {
                var added = new Bulletin(
                    from,
                    kind,
                    message.Addressee,
                    id,
                    message.BulletinGroup,
                    message.Text,
                    now,
                    now,
                    source
                );
                _bulletins[key] = added;
                events.Add(new BulletinChangedEventArgs(BulletinChange.Added, added, null));
                while (_bulletins.Count > _options.MaxBulletins)
                {
                    var oldest = _bulletins.MinBy(e => e.Value.LastHeard);
                    _bulletins.Remove(oldest.Key);
                    events.Add(new BulletinChangedEventArgs(BulletinChange.Removed, oldest.Value, null));
                }
            }

            Schedule();
        }

        Raise(events);
        return true;
    }

    /// <summary>Stops the expiry timer. The board keeps what it has, but takes no more packets.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }

        _timer.Dispose();
    }

    /// <summary>Which kind of bulletin a message is, or <see langword="null"/> for any other message.</summary>
    private static MessageKind? Kind(MessagePayload message) =>
        message.Kind switch
        {
            MessageKind.Bulletin or MessageKind.Announcement or MessageKind.NwsBulletin => message.Kind,
            // The decoder knows NWS-; NWS_ is used too, and the gateway's t/n filter takes both.
            MessageKind.Message when message.Addressee.StartsWith("NWS_", StringComparison.Ordinal) =>
                MessageKind.NwsBulletin,
            _ => null,
        };

    private void Expire()
    {
        var events = new List<BulletinChangedEventArgs>();
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            var now = _time.GetUtcNow();
            foreach (var (key, bulletin) in _bulletins.ToList())
            {
                if (now - bulletin.LastHeard >= _options.Expiry)
                {
                    _bulletins.Remove(key);
                    events.Add(new BulletinChangedEventArgs(BulletinChange.Removed, bulletin, null));
                }
            }

            Schedule();
        }

        Raise(events);
    }

    /// <summary>Sets the timer for the next bulletin to expire.</summary>
    private void Schedule()
    {
        if (_bulletins.Count == 0)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var due = _bulletins.Values.Min(b => b.LastHeard) + _options.Expiry - _time.GetUtcNow();
        _timer.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
    }

    private void Raise(List<BulletinChangedEventArgs> events)
    {
        foreach (var change in events)
        {
            Changed?.Invoke(this, change);
        }
    }
}
