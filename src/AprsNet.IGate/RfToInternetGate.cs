using System.Diagnostics.CodeAnalysis;
using System.Text;
using AprsNet.Ax25;
using AprsNet.Packets;
using AprsNet.Packets.Is2;

namespace AprsNet.IGate;

/// <summary>
/// Decides which packets heard on RF an IGate passes to APRS-IS, following the aprs-is.net IGate rules, and builds the
/// line to send: the packet as heard, with <c>,qAO,IGATE</c> (or <c>,qAR,IGATE</c>) after its path. Every copy heard
/// goes up, direct and through each digipeater. Safe to use from several threads.
/// </summary>
/// <remarks>
/// <para>
/// Every packet is gated except (aprs-is.net, "IGate Details"): frames that aren't APRS (a UI frame with no layer 3
/// protocol); packets with <c>TCPIP</c>, <c>TCPXX</c>, <c>NOGATE</c> or <c>RFONLY</c> in the path; third-party traffic
/// whose inner header has <c>TCPIP</c> or <c>TCPXX</c>, which came from APRS-IS already; and general queries
/// (<c>?APRS?</c>), which are meant for stations in radio range. Other third-party traffic goes up as the packet it
/// carries, without the RF header, and is checked again by the same rules.
/// </para>
/// <para>
/// Beyond those rules it also refuses what APRS-IS can't take or would only drop: a header that can't be read, a path
/// that already holds a q-construct, an empty information field, and a line longer than APRS-IS carries. Packets
/// whose payload has errors are gated as heard: APRS-IS carries what stations send, and an IGate doesn't judge it.
/// </para>
/// <para>
/// Copies of a packet are gated too, as aprsc's IGate hints ask: an IGate sends everything it hears, and the APRS-IS
/// servers drop the duplicates themselves, after using each copy to see which IGates heard which stations. Duplicate
/// filtering belongs to digipeating and gating to RF. Set <see cref="RfToInternetGateOptions.DuplicateWindow"/> to
/// refuse copies anyway.
/// </para>
/// <para>
/// The path is never changed, as the rules require. The information field is cut at its first CR or LF, which would
/// end the line on APRS-IS; many TNCs end a packet with a CR. Every other byte goes up as heard, NUL and 0x1C
/// included: an IGate that drops or cuts at them sends a copy that differs from other IGates' copies.
/// </para>
/// </remarks>
public sealed class RfToInternetGate
{
    /// <summary>The longest line APRS-IS carries, without its CR LF.</summary>
    private const int MaxLineLength = 510;

    private const int MaxLoginLength = 9;

    private readonly DuplicateFilter? _duplicates;
    private readonly string _suffix;

    /// <summary>Creates the rules for the IGate logged in to APRS-IS as <paramref name="igate"/>.</summary>
    /// <param name="igate">The IGate's callsign, as it logs in to APRS-IS: at most 9 characters.</param>
    /// <param name="options">The q-construct, duplicate window and clock; the defaults suit a receive-only IGate.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="igate"/> is longer than an APRS-IS login, or a setting in <paramref name="options"/> is unusable.
    /// </exception>
    public RfToInternetGate(Callsign igate, RfToInternetGateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(igate);
        if (igate.Value.Length > MaxLoginLength)
        {
            throw new ArgumentException($"An APRS-IS login is at most {MaxLoginLength} characters.", nameof(igate));
        }

        options ??= new RfToInternetGateOptions();
        options.Validate();
        IGate = igate;
        ReceiveOnly = options.ReceiveOnly;
        _duplicates =
            options.DuplicateWindow > TimeSpan.Zero
                ? new DuplicateFilter(options.DuplicateWindow, timeProvider: options.TimeProvider)
                : null;
        _suffix = $",{(ReceiveOnly ? "qAO" : "qAR")},{igate.Value}";
    }

    /// <summary>The IGate's callsign, added to every gated packet after the q-construct.</summary>
    public Callsign IGate { get; }

    /// <summary>
    /// Whether the IGate is receive-only: it adds <c>qAO</c>, which tells APRS-IS it cannot pass messages back to RF,
    /// rather than <c>qAR</c>.
    /// </summary>
    public bool ReceiveOnly { get; }

    /// <summary>
    /// Decides whether a frame from the TNC goes to APRS-IS. Frames that aren't APRS (<see cref="Ax25Frame.IsAprs"/>)
    /// are refused; the rest are decided as their packet.
    /// </summary>
    public GateDecision Gate(Ax25Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var packet = frame.ToAprsPacket();
        return frame.IsAprs ? Gate(packet) : Refuse(packet, GateRefusal.NotAprs);
    }

    /// <summary>
    /// Decides whether a packet heard on RF goes to APRS-IS. With a duplicate window set, a packet that is gated is
    /// remembered, so its copies within the window are refused.
    /// </summary>
    /// <param name="heard">The packet as the TNC heard it, from a frame that is APRS.</param>
    public GateDecision Gate(AprsPacket heard) => Decide(heard);

    /// <summary>
    /// Decides as <see cref="Gate(AprsPacket)"/> does, and keeps how the packet was heard on the air in
    /// <see cref="GateDecision.Reception"/>, for the uplink to send to an IS2 server with the gated packet
    /// (<c>SendWithReceptionAsync</c> in <c>AprsNet.AprsIsTcpClient</c>). The metadata never decides whether a packet
    /// is gated.
    /// </summary>
    /// <remarks>Experimental: IS2 is still a work in progress upstream, and this method may change or go.</remarks>
    /// <param name="heard">The packet as the TNC heard it, from a frame that is APRS.</param>
    /// <param name="reception">How the packet was heard, or <see langword="null"/> when that isn't known.</param>
    [Experimental(Is2Diagnostics.Id)]
    public GateDecision Gate(AprsPacket heard, ReceptionMetadata? reception)
    {
        var decision = Decide(heard);
        decision.Reception = reception;
        return decision;
    }

    private GateDecision Decide(AprsPacket heard)
    {
        ArgumentNullException.ThrowIfNull(heard);
        var packet = heard;
        while (true)
        {
            if (Check(packet) is { } refusal)
            {
                return Refuse(heard, refusal);
            }

            if (packet.Type != PacketType.ThirdParty)
            {
                break;
            }

            // Third-party traffic goes up as the packet it carries, checked again (aprs-is.net: "processed again").
            if (packet.Payload is not ThirdPartyPayload { Packet: var inner })
            {
                return Refuse(heard, GateRefusal.Unreadable);
            }

            packet = inner;
        }

        if (packet.Type == PacketType.Query)
        {
            return Refuse(heard, GateRefusal.GeneralQuery);
        }

        var line = Line(packet);
        if (line is null)
        {
            return Refuse(heard, GateRefusal.Empty);
        }

        if (line.Length > MaxLineLength)
        {
            return Refuse(heard, GateRefusal.TooLong);
        }

        var gated = AprsPacket.Parse(line);
        if (_duplicates?.Check(gated).IsDuplicate == true)
        {
            return Refuse(heard, GateRefusal.Duplicate);
        }

        IGateMetrics.Gated(IGate);
        return new GateDecision(heard, gated, null);
    }

    /// <summary>Why the header and path of <paramref name="packet"/> keep it off APRS-IS, or <see langword="null"/>.</summary>
    private static GateRefusal? Check(AprsPacket packet)
    {
        if (!packet.HasHeader || packet.Issues.Any(issue => IsHeaderError(issue.Code)))
        {
            return GateRefusal.Unreadable;
        }

        foreach (var entry in packet.Path)
        {
            if (entry.Kind == PathEntryKind.QConstruct)
            {
                return GateRefusal.AlreadyGated;
            }

            var name = entry.Station.Value;
            if (Is(name, "TCPIP") || Is(name, "TCPXX"))
            {
                return GateRefusal.FromInternet;
            }

            if (Is(name, "NOGATE") || Is(name, "RFONLY"))
            {
                return GateRefusal.NoGate;
            }
        }

        return null;

        static bool Is(string name, string marker) => name.Equals(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHeaderError(ParseIssueCode code) =>
        code
            is ParseIssueCode.MissingHeader
                or ParseIssueCode.EmptySource
                or ParseIssueCode.EmptyDestination
                or ParseIssueCode.EmptyPathEntry
                or ParseIssueCode.TooManyPathEntries
                or ParseIssueCode.InvalidCallsign;

    /// <summary>
    /// The line to send: the header as heard, the q-construct and IGate, then the information field up to its first
    /// CR or LF. <see langword="null"/> when no information is left.
    /// </summary>
    private string? Line(AprsPacket packet)
    {
        var raw = packet.Raw.Span;
        var colon = raw.IndexOf((byte)':');
        var information = raw[(colon + 1)..];
        var end = information.IndexOfAny((byte)'\r', (byte)'\n');
        information = end < 0 ? information : information[..end];
        return information.IsEmpty
            ? null
            : string.Concat(
                Encoding.Latin1.GetString(raw[..colon]),
                _suffix,
                ":",
                Encoding.Latin1.GetString(information)
            );
    }

    private GateDecision Refuse(AprsPacket heard, GateRefusal refusal)
    {
        IGateMetrics.Refused(IGate, refusal);
        return new GateDecision(heard, null, refusal);
    }
}

/// <summary>Settings for a <see cref="RfToInternetGate"/>.</summary>
public sealed class RfToInternetGateOptions
{
    /// <summary>
    /// Whether the IGate is receive-only, so it can't pass messages back to RF: <see langword="true"/> by default, and
    /// gated packets carry <c>qAO</c>. Set it to <see langword="false"/> for an IGate that also gates to RF, which
    /// adds <c>qAR</c>.
    /// </summary>
    public bool ReceiveOnly { get; set; } = true;

    /// <summary>
    /// How long a gated packet's copies are refused: <see cref="TimeSpan.Zero"/> by default, which refuses none. aprsc's
    /// IGate hints ask an IGate to send every copy it hears: the APRS-IS servers drop duplicates themselves, and use
    /// each copy to see which IGates heard which stations. Set it (for example to 30 seconds, APRS-IS's own window)
    /// only to send a packet heard direct and again through each digipeater once.
    /// </summary>
    public TimeSpan DuplicateWindow { get; set; } = TimeSpan.Zero;

    /// <summary>The clock for the duplicate window, when one is set. The system clock by default.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Checks that the settings can be used.</summary>
    /// <exception cref="ArgumentException">A setting is unusable; the message names it.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(DuplicateWindow, TimeSpan.Zero, nameof(DuplicateWindow));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
    }
}

/// <summary>
/// Why a packet heard on RF did not go to APRS-IS. A stable vocabulary, also used as metric tag values: members may be
/// added but are never renamed or reused.
/// </summary>
public enum GateRefusal
{
    /// <summary>The frame isn't APRS: not a UI frame, or a layer 3 protocol other than none.</summary>
    NotAprs,

    /// <summary>The header can't be read, or a third-party packet's inner packet can't.</summary>
    Unreadable,

    /// <summary><c>TCPIP</c> or <c>TCPXX</c> in the path or a third-party header: it came from APRS-IS.</summary>
    FromInternet,

    /// <summary><c>NOGATE</c> or <c>RFONLY</c> in the path: the sender asked IGates not to gate it.</summary>
    NoGate,

    /// <summary>The path already holds an APRS-IS q-construct.</summary>
    AlreadyGated,

    /// <summary>A general query (<c>?APRS?</c>, data type <c>?</c>), meant for stations in radio range.</summary>
    GeneralQuery,

    /// <summary>No information field, or nothing before its first CR or LF.</summary>
    Empty,

    /// <summary>With the q-construct added, longer than the 510 bytes an APRS-IS line holds.</summary>
    TooLong,

    /// <summary>
    /// A copy of a packet gated within <see cref="RfToInternetGateOptions.DuplicateWindow"/>. Never given by default:
    /// the window is off, and every copy goes up for the APRS-IS servers to drop.
    /// </summary>
    Duplicate,
}

/// <summary>What <see cref="RfToInternetGate.Gate(AprsPacket)"/> decided for a packet.</summary>
public sealed class GateDecision
{
    internal GateDecision(AprsPacket heard, AprsPacket? packet, GateRefusal? refusal)
    {
        Heard = heard;
        Packet = packet;
        Refusal = refusal;
    }

    /// <summary>The packet as heard on RF.</summary>
    public AprsPacket Heard { get; }

    /// <summary>
    /// The packet to send to APRS-IS, with the q-construct and IGate in its path; <see langword="null"/> when refused.
    /// </summary>
    public AprsPacket? Packet { get; }

    /// <summary>Why the packet was refused; <see langword="null"/> when it is gated.</summary>
    public GateRefusal? Refusal { get; }

    /// <summary>Whether the packet goes to APRS-IS.</summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(Packet))]
    public bool IsGated => Packet is not null;

    /// <summary>
    /// How the packet was heard on the air, as given to
    /// <see cref="RfToInternetGate.Gate(AprsPacket, ReceptionMetadata?)"/>; <see langword="null"/> from
    /// <see cref="RfToInternetGate.Gate(AprsPacket)"/>. Send it with <see cref="Packet"/> to an IS2 server.
    /// </summary>
    /// <remarks>Experimental: IS2 is still a work in progress upstream, and this property may change or go.</remarks>
    [Experimental(Is2Diagnostics.Id)]
    public ReceptionMetadata? Reception { get; internal set; }
}
