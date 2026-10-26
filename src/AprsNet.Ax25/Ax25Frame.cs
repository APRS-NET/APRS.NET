using System.Diagnostics.CodeAnalysis;
using System.Text;
using AprsNet.Packets;

namespace AprsNet.Ax25;

/// <summary>Why <see cref="Ax25Frame.TryDecode"/> could not read a frame.</summary>
public enum Ax25FrameError
{
    /// <summary>Shorter than a destination, a source and a control byte.</summary>
    TooShort,

    /// <summary>An address is not a valid AX.25 callsign and SSID.</summary>
    InvalidAddress,

    /// <summary>The addresses do not end within two plus eight digipeaters, or the frame ends inside one.</summary>
    AddressNotTerminated,
}

/// <summary>
/// An AX.25 frame as a KISS TNC passes it on, without flags or checksum: destination, source, up to eight
/// digipeaters, the control byte, the protocol identifier for information frames, and the information field. APRS
/// uses unnumbered information (UI) frames with no layer 3 protocol (<see cref="IsAprs"/>).
/// </summary>
/// <remarks>
/// <para>
/// The frame converts to an <see cref="AprsPacket"/> through its TNC2 text (<see cref="ToTnc2"/>), so a packet that
/// arrived by radio parses exactly like one from APRS-IS, and its <see cref="AprsPacket.Raw"/> is always the TNC2
/// line.
/// </para>
/// <para>
/// TNC2 text marks only the last digipeater that has repeated a frame (<c>WIDE1-1*</c>), while AX.25 sets the bit on
/// each one. Converting a packet to a frame sets it on every digipeater up to the marked one.
/// </para>
/// </remarks>
public sealed class Ax25Frame
{
    /// <summary>The control byte of an unnumbered information (UI) frame, with the poll/final bit clear.</summary>
    public const byte UiControl = 0x03;

    /// <summary>The protocol identifier for "no layer 3 protocol", which APRS uses.</summary>
    public const byte NoLayer3Protocol = 0xF0;

    /// <summary>The most digipeaters an AX.25 frame can carry.</summary>
    public const int MaxDigipeaters = 8;

    private readonly byte[] _raw;

    private Ax25Frame(
        byte[] raw,
        Ax25Address destination,
        Ax25Address source,
        IReadOnlyList<Ax25Address> digipeaters,
        byte control,
        byte? protocol,
        ReadOnlyMemory<byte> information
    )
    {
        _raw = raw;
        Destination = destination;
        Source = source;
        Digipeaters = digipeaters;
        Control = control;
        Protocol = protocol;
        Information = information;
    }

    /// <summary>The destination address: for APRS, usually a software identifier, or Mic-E's latitude.</summary>
    public Ax25Address Destination { get; }

    /// <summary>The station that sent the frame.</summary>
    public Ax25Address Source { get; }

    /// <summary>The digipeaters in order, each with its has-been-repeated bit; empty when there are none.</summary>
    public IReadOnlyList<Ax25Address> Digipeaters { get; }

    /// <summary>The control byte: <see cref="UiControl"/> for APRS.</summary>
    public byte Control { get; }

    /// <summary>
    /// The protocol identifier, for information frames (I and UI); <see langword="null"/> for frames that have none.
    /// </summary>
    public byte? Protocol { get; }

    /// <summary>Everything after the protocol identifier: for APRS, the information field of the packet.</summary>
    public ReadOnlyMemory<byte> Information { get; }

    /// <summary>The frame's bytes, exactly as decoded or encoded.</summary>
    public ReadOnlyMemory<byte> Raw => _raw;

    /// <summary>Whether this is an APRS frame: a UI frame (poll/final bit either way) with no layer 3 protocol.</summary>
    public bool IsAprs => (Control & ~0x10) == UiControl && Protocol == NoLayer3Protocol;

    /// <summary>
    /// Reads a frame without its checksum, as a KISS TNC delivers it. Never throws for malformed data.
    /// </summary>
    public static bool TryDecode(
        ReadOnlySpan<byte> frame,
        [NotNullWhen(true)] out Ax25Frame? result,
        out Ax25FrameError error
    )
    {
        result = null;
        error = Ax25FrameError.TooShort;
        if (frame.Length < 15)
        {
            return false;
        }

        var addresses = new List<Ax25Address>(4);
        var at = 0;
        while (true)
        {
            if (frame.Length < at + 7 || addresses.Count == 2 + MaxDigipeaters)
            {
                error = Ax25FrameError.AddressNotTerminated;
                return false;
            }

            if (!Ax25Address.TryRead(frame.Slice(at, 7), out var address, out var last))
            {
                error = Ax25FrameError.InvalidAddress;
                return false;
            }

            addresses.Add(address);
            at += 7;
            if (last)
            {
                break;
            }
        }

        if (addresses.Count < 2)
        {
            error = Ax25FrameError.AddressNotTerminated;
            return false;
        }

        if (frame.Length <= at)
        {
            error = Ax25FrameError.TooShort;
            return false;
        }

        var raw = frame.ToArray();
        var control = raw[at++];
        // I frames (bit 0 clear) and UI frames carry a protocol identifier; supervisory and other U frames do not.
        byte? protocol =
            (control & 1) == 0 || (control & ~0x10) == UiControl ? (at < raw.Length ? raw[at++] : null) : null;
        if (protocol is null && ((control & 1) == 0 || (control & ~0x10) == UiControl))
        {
            error = Ax25FrameError.TooShort;
            return false;
        }

        result = new Ax25Frame(raw, addresses[0], addresses[1], addresses[2..], control, protocol, raw.AsMemory(at));
        return true;
    }

    /// <summary>
    /// The frame as a TNC2 line, <c>SOURCE&gt;DESTINATION,DIGI*,DIGI:information</c>, Latin-1 so each byte of the
    /// information field is one character. The last digipeater with its has-been-repeated bit set gets the <c>*</c>.
    /// </summary>
    public string ToTnc2()
    {
        var line = new StringBuilder();
        line.Append(Source).Append('>').Append(Destination);
        var lastRepeated = -1;
        for (var i = 0; i < Digipeaters.Count; i++)
        {
            lastRepeated = Digipeaters[i].Flag ? i : lastRepeated;
        }

        for (var i = 0; i < Digipeaters.Count; i++)
        {
            line.Append(',').Append(Digipeaters[i]).Append(i == lastRepeated ? "*" : "");
        }

        return line.Append(':').Append(Encoding.Latin1.GetString(Information.Span)).ToString();
    }

    /// <summary>Parses the frame's TNC2 line (<see cref="ToTnc2"/>) as an APRS packet.</summary>
    public AprsPacket ToAprsPacket() => AprsPacket.Parse(ToTnc2());

    /// <summary>
    /// Builds the UI frame that carries <paramref name="packet"/> on the air, for a KISS TNC to send.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The packet has no header, a station or path entry AX.25 cannot carry (more than six characters, lower case, an
    /// SSID above 15), more than eight digipeaters, or an APRS-IS q-construct in its path: packets heard on APRS-IS go
    /// to RF as third-party traffic, not with their APRS-IS path.
    /// </exception>
    public static Ax25Frame FromAprsPacket(AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (!packet.HasHeader)
        {
            throw new ArgumentException("The packet has no SOURCE>DESTINATION header.", nameof(packet));
        }

        var source = Address(packet.Source.Value, "source");
        var destination = Address(packet.Destination.Value, "destination");
        if (packet.Path.Any(entry => entry.Kind == PathEntryKind.QConstruct))
        {
            throw new ArgumentException(
                "The path holds an APRS-IS q-construct: send a packet heard on APRS-IS to RF as third-party traffic.",
                nameof(packet)
            );
        }

        if (packet.Path.Count > MaxDigipeaters)
        {
            throw new ArgumentException($"AX.25 carries at most {MaxDigipeaters} digipeaters.", nameof(packet));
        }

        // A '*' marks the last digipeater that repeated the frame, so every one before it has repeated it too.
        var lastRepeated = -1;
        for (var i = 0; i < packet.Path.Count; i++)
        {
            lastRepeated = packet.Path[i].HasBeenRepeated ? i : lastRepeated;
        }

        var digipeaters = new List<Ax25Address>(packet.Path.Count);
        for (var i = 0; i < packet.Path.Count; i++)
        {
            var parsed = Address(packet.Path[i].Station.Value, "path entry");
            digipeaters.Add(new Ax25Address(parsed.Callsign, parsed.Ssid, i <= lastRepeated));
        }

        return Encode(destination, source, digipeaters, packet.Information.Span);

        static Ax25Address Address(string text, string what) =>
            Ax25Address.TryParse(text, out var address)
                ? address
                : throw new ArgumentException(
                    $"The {what} '{text}' cannot be sent in AX.25: use one to six upper-case letters and digits, "
                        + "and an SSID from 0 to 15.",
                    nameof(packet)
                );
    }

    /// <summary>
    /// The same UI frame with another digipeater path, as a digipeater sends it on: the destination, source and
    /// information field are kept, and each address in <paramref name="digipeaters"/> keeps its has-been-repeated flag.
    /// </summary>
    /// <exception cref="ArgumentException">More than eight digipeaters, or an address left as <see langword="default"/>.</exception>
    /// <exception cref="InvalidOperationException">The frame isn't APRS (<see cref="IsAprs"/>).</exception>
    public Ax25Frame WithDigipeaters(IReadOnlyList<Ax25Address> digipeaters)
    {
        ArgumentNullException.ThrowIfNull(digipeaters);
        if (!IsAprs)
        {
            throw new InvalidOperationException("Only an APRS UI frame can be given a new path.");
        }

        if (digipeaters.Count > MaxDigipeaters)
        {
            throw new ArgumentException($"AX.25 carries at most {MaxDigipeaters} digipeaters.", nameof(digipeaters));
        }

        if (digipeaters.Any(d => d.Callsign.Length == 0))
        {
            throw new ArgumentException("A digipeater address is empty.", nameof(digipeaters));
        }

        return Encode(Destination, Source, [.. digipeaters], Information.Span);
    }

    /// <summary>A new array holding <see cref="Raw"/>.</summary>
    public byte[] GetBytes() => (byte[])_raw.Clone();

    /// <inheritdoc cref="ToTnc2"/>
    public override string ToString() => ToTnc2();

    private static Ax25Frame Encode(
        Ax25Address destination,
        Ax25Address source,
        List<Ax25Address> digipeaters,
        ReadOnlySpan<byte> information
    )
    {
        var addresses = 2 + digipeaters.Count;
        var raw = new byte[(addresses * 7) + 2 + information.Length];
        // An APRS frame is a command: the destination's command/response bit set, the source's clear (AX.25 2.0).
        new Ax25Address(destination.Callsign, destination.Ssid, flag: true).WriteTo(raw.AsSpan(0, 7), last: false);
        new Ax25Address(source.Callsign, source.Ssid, flag: false).WriteTo(
            raw.AsSpan(7, 7),
            last: digipeaters.Count == 0
        );
        for (var i = 0; i < digipeaters.Count; i++)
        {
            digipeaters[i].WriteTo(raw.AsSpan((2 + i) * 7, 7), last: i == digipeaters.Count - 1);
        }

        var at = addresses * 7;
        raw[at++] = UiControl;
        raw[at++] = NoLayer3Protocol;
        information.CopyTo(raw.AsSpan(at));
        return TryDecode(raw, out var frame, out _)
            ? frame
            : throw new InvalidOperationException("An encoded frame did not decode."); // cannot happen: addresses were checked
    }
}
