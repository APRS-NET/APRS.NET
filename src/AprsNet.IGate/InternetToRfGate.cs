using System.Text;
using AprsNet.Packets;

namespace AprsNet.IGate;

/// <summary>
/// Decides which packets heard on APRS-IS a two-way IGate sends on RF, following the aprs-is.net IGate rules, and
/// builds the third-party packet to transmit: <c>IGATE&gt;TOCALL,PATH:}SENDER&gt;DEST,TCPIP,IGATE*:…</c>. Only
/// messages (and their acknowledgements) for stations heard on RF go out, so a two-way IGate doesn't flood the channel
/// with internet traffic. Safe to use from several threads.
/// </summary>
/// <remarks>
/// <para>
/// A message, acknowledgement or rejection is gated when (aprs-is.net, "IGate Details"): its addressee is local, heard on
/// RF recently and over few hops (<see cref="HeardStations.IsLocal"/>); its sender isn't heard on RF, since the
/// addressee can hear the sender itself (local by the same rule); and its path has no <c>TCPXX</c>, <c>NOGATE</c> or <c>RFONLY</c>. Bulletins,
/// announcements and queries never go out. Packets gated from RF to APRS-IS (<c>qAR</c>, <c>qAO</c>) never come back.
/// </para>
/// <para>
/// After gating a message, the next position the sender sends on APRS-IS within
/// <see cref="InternetToRfGateOptions.SenderPositionWindow"/> goes out once, so the addressee can see who is writing.
/// </para>
/// <para>
/// Everything sent on RF is limited to <see cref="InternetToRfGateOptions.MaxPerMinute"/> packets a minute and
/// <see cref="InternetToRfGateOptions.MaxPerFiveMinutes"/> in five minutes, as Dire Wolf's <c>IGTXLIMIT</c>; packets
/// over the limit are refused, not delayed. Copies of a packet gated within
/// <see cref="InternetToRfGateOptions.DuplicateWindow"/> are refused.
/// </para>
/// </remarks>
public sealed class InternetToRfGate
{
    private readonly HeardStations _heard;
    private readonly DuplicateFilter _gated;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _senderPositionWindow;
    private readonly int _maxPerMinute;
    private readonly int _maxPerFiveMinutes;
    private readonly Lock _lock = new();
    private readonly Queue<DateTimeOffset> _sent = new();
    private readonly Dictionary<string, DateTimeOffset> _positionsOwed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the rules for the IGate that transmits as <paramref name="igate"/>.</summary>
    /// <param name="igate">
    /// What the IGate transmits as on RF: its callsign, which must fit AX.25, its TOCALL, and the path its packets take,
    /// such as <c>WIDE1-1,WIDE2-1</c>. Set <see cref="HeardStationsOptions.MaxLocalHops"/> to the hops of that path.
    /// </param>
    /// <param name="heard">The stations heard on RF, recorded from everything the TNC hears.</param>
    /// <param name="options">The limits and clock; the defaults follow Dire Wolf's.</param>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public InternetToRfGate(PacketAddress igate, HeardStations heard, InternetToRfGateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(igate);
        ArgumentNullException.ThrowIfNull(heard);
        options ??= new InternetToRfGateOptions();
        options.Validate();
        IGate = igate;
        _heard = heard;
        _timeProvider = options.TimeProvider;
        _senderPositionWindow = options.SenderPositionWindow;
        _maxPerMinute = options.MaxPerMinute;
        _maxPerFiveMinutes = options.MaxPerFiveMinutes;
        _gated = new DuplicateFilter(options.DuplicateWindow, timeProvider: options.TimeProvider);
    }

    /// <summary>What the IGate transmits as: its callsign, TOCALL and path.</summary>
    public PacketAddress IGate { get; }

    /// <summary>
    /// Decides whether a packet heard on APRS-IS goes out on RF. A message that is gated makes its sender's next
    /// position owed, and the packet is remembered, so its copies within the duplicate window are refused.
    /// </summary>
    /// <param name="fromInternet">The packet as APRS-IS sent it, with its q-construct.</param>
    public RfGateDecision Gate(AprsPacket fromInternet)
    {
        ArgumentNullException.ThrowIfNull(fromInternet);
        if (fromInternet.Source is not { } sender || fromInternet.Destination is null || fromInternet.Path is null)
        {
            return Refuse(fromInternet, RfGateRefusal.Unreadable);
        }

        var path = PathAnalysis.FromPacket(fromInternet);
        if (path.IsGatedFromRadio)
        {
            return Refuse(fromInternet, RfGateRefusal.FromRadio);
        }

        if (
            path.IsNoGate
            || fromInternet.Path.Any(e => e.Station.Value.Equals("TCPXX", StringComparison.OrdinalIgnoreCase))
        )
        {
            return Refuse(fromInternet, RfGateRefusal.NoGate);
        }

        if (fromInternet.Payload is MessagePayload message)
        {
            if (message.Kind is not (MessageKind.Message or MessageKind.Acknowledgement or MessageKind.Rejection))
            {
                return Refuse(fromInternet, RfGateRefusal.NotForAStation);
            }

            if (!Callsign.TryParse(message.Addressee, out var addressee) || !_heard.IsLocal(addressee))
            {
                return Refuse(fromInternet, RfGateRefusal.NotLocal);
            }

            if (_heard.IsLocal(sender))
            {
                return Refuse(fromInternet, RfGateRefusal.SenderOnRf);
            }

            return Send(fromInternet, sender, owesPosition: true);
        }

        if (fromInternet.Type is PacketType.Position or PacketType.MicE && TakePositionOwed(sender))
        {
            return Send(fromInternet, sender, owesPosition: false);
        }

        return Refuse(fromInternet, RfGateRefusal.NotForAStation);
    }

    private RfGateDecision Send(AprsPacket fromInternet, Callsign sender, bool owesPosition)
    {
        if (_gated.IsDuplicate(fromInternet))
        {
            return Refuse(fromInternet, RfGateRefusal.Duplicate);
        }

        var now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            while (_sent.TryPeek(out var oldest) && now - oldest >= TimeSpan.FromMinutes(5))
            {
                _sent.Dequeue();
            }

            if (
                _sent.Count >= _maxPerFiveMinutes
                || _sent.Count(t => now - t < TimeSpan.FromMinutes(1)) >= _maxPerMinute
            )
            {
                return Refuse(fromInternet, RfGateRefusal.RateLimited);
            }

            _sent.Enqueue(now);
            if (owesPosition)
            {
                _positionsOwed[sender.Value] = now + _senderPositionWindow;
            }
        }

        _gated.Check(fromInternet);
        return new RfGateDecision(fromInternet, ThirdParty(fromInternet), null);
    }

    private bool TakePositionOwed(Callsign sender)
    {
        lock (_lock)
        {
            if (!_positionsOwed.Remove(sender.Value, out var until))
            {
                return false;
            }

            return _timeProvider.GetUtcNow() <= until;
        }
    }

    // SENDER>DEST,TCPIP,IGATE*:info inside IGATE>TOCALL,PATH:}, as the APRS-IS rules write it.
    private AprsPacket ThirdParty(AprsPacket fromInternet)
    {
        var header = Encoding.ASCII.GetBytes(
            $"{fromInternet.Source!.Value}>{fromInternet.Destination!.Value},TCPIP,{IGate.Source.Value}*:"
        );
        var information = fromInternet.Information.Span;
        var end = information.IndexOfAny("\r\n\0"u8);
        var inner = AprsPacket.Parse([.. header, .. end < 0 ? information : information[..end]]);
        return IGate.Create(new ThirdPartyPayload(inner));
    }

    private static RfGateDecision Refuse(AprsPacket fromInternet, RfGateRefusal refusal) =>
        new(fromInternet, null, refusal);
}

/// <summary>Settings for an <see cref="InternetToRfGate"/>.</summary>
public sealed class InternetToRfGateOptions
{
    /// <summary>
    /// The most packets sent on RF in any minute: 6 by default, as Dire Wolf's <c>IGTXLIMIT</c>. More are refused.
    /// </summary>
    public int MaxPerMinute { get; set; } = 6;

    /// <summary>The most packets sent on RF in any five minutes: 10 by default, as Dire Wolf's <c>IGTXLIMIT</c>.</summary>
    public int MaxPerFiveMinutes { get; set; } = 10;

    /// <summary>
    /// How long after a message is gated its sender's next position is still sent on RF: 30 minutes by default.
    /// </summary>
    public TimeSpan SenderPositionWindow { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long a gated packet's copies are refused: 30 seconds by default, APRS-IS's own duplicate window. A message
    /// heard from two APRS-IS servers goes out once.
    /// </summary>
    public TimeSpan DuplicateWindow { get; set; } = DuplicateFilter.DefaultWindow;

    /// <summary>The clock for the limits and windows. The system clock by default.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Checks that the settings can be used.</summary>
    /// <exception cref="ArgumentException">A setting is unusable; the message names it.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPerMinute, 1, nameof(MaxPerMinute));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPerFiveMinutes, MaxPerMinute, nameof(MaxPerFiveMinutes));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            SenderPositionWindow,
            TimeSpan.Zero,
            nameof(SenderPositionWindow)
        );
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(DuplicateWindow, TimeSpan.Zero, nameof(DuplicateWindow));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
    }
}

/// <summary>What an <see cref="InternetToRfGate"/> decided for a packet from APRS-IS.</summary>
/// <param name="FromInternet">The packet as APRS-IS sent it.</param>
/// <param name="Packet">The third-party packet to transmit, or <see langword="null"/> when it isn't gated.</param>
/// <param name="Refusal">Why it isn't gated, or <see langword="null"/> when it is.</param>
public sealed record RfGateDecision(AprsPacket FromInternet, AprsPacket? Packet, RfGateRefusal? Refusal)
{
    /// <summary>Whether the packet goes out on RF: <see cref="Packet"/> is the one to transmit.</summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(Packet))]
    public bool IsGated => Packet is not null;
}

/// <summary>
/// Why a packet from APRS-IS did not go out on RF. A stable vocabulary, also used as metric and log values: members may
/// be added but are never renamed or reused.
/// </summary>
public enum RfGateRefusal
{
    /// <summary>The header can't be read.</summary>
    Unreadable,

    /// <summary>
    /// Not a message, acknowledgement or rejection for a station, nor the position owed after one: most traffic.
    /// </summary>
    NotForAStation,

    /// <summary>The addressee hasn't been heard on RF recently over few enough hops.</summary>
    NotLocal,

    /// <summary>The sender is local on RF itself, so the addressee can hear it without the IGate.</summary>
    SenderOnRf,

    /// <summary><c>TCPXX</c>, <c>NOGATE</c> or <c>RFONLY</c> in the path.</summary>
    NoGate,

    /// <summary>It was gated from RF to APRS-IS (<c>qAR</c>, <c>qAO</c>): it is on RF already.</summary>
    FromRadio,

    /// <summary>A copy of a packet gated within the duplicate window.</summary>
    Duplicate,

    /// <summary>Over the limit on packets sent on RF.</summary>
    RateLimited,
}
