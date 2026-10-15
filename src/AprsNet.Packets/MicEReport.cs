namespace AprsNet.Packets;

// Custom messages are computed as Custom0 + n from the message bits, so some members look unused to inspectors.
// ReSharper disable UnusedMember.Global

/// <summary>
/// The message a Mic-E station sends in its destination (APRS101 ch. 10): one of seven standard or seven custom
/// messages, or Emergency. A mix of standard and custom bits is <see cref="Unknown"/>.
/// </summary>
public enum MicEMessage
{
    /// <summary>M0: Off duty.</summary>
    OffDuty,

    /// <summary>M1: En route.</summary>
    EnRoute,

    /// <summary>M2: In service.</summary>
    InService,

    /// <summary>M3: Returning.</summary>
    Returning,

    /// <summary>M4: Committed.</summary>
    Committed,

    /// <summary>M5: Special.</summary>
    Special,

    /// <summary>M6: Priority.</summary>
    Priority,

    /// <summary>C0: Custom-0.</summary>
    Custom0,

    /// <summary>C1: Custom-1.</summary>
    Custom1,

    /// <summary>C2: Custom-2.</summary>
    Custom2,

    /// <summary>C3: Custom-3.</summary>
    Custom3,

    /// <summary>C4: Custom-4.</summary>
    Custom4,

    /// <summary>C5: Custom-5.</summary>
    Custom5,

    /// <summary>C6: Custom-6.</summary>
    Custom6,

    /// <summary>Emergency: all three message bits clear.</summary>
    Emergency,

    /// <summary>The message bits mix standard and custom encodings.</summary>
    Unknown,
}

// ReSharper restore UnusedMember.Global

/// <summary>
/// A Mic-E report (<c>`</c>, <c>'</c>, APRS101 ch. 10): a compact position whose latitude and message travel in the
/// destination callsign, used by Kenwood and Yaesu radios and many trackers. It shares the position fields of
/// <see cref="PositionedPayload"/>; <see cref="PositionedPayload.Comment"/> is the status text.
/// </summary>
public sealed class MicEReport : PositionedPayload
{
    /// <summary>
    /// Creates a Mic-E report to send with <see cref="AprsPacket.Create"/>: a position, symbol, message, course and
    /// speed, an altitude and status text. The latitude and message go in the packet's destination.
    /// </summary>
    public MicEReport() { }

    /// <summary>
    /// The message from the destination's message bits. The default, <see cref="MicEMessage.OffDuty"/>, is what most
    /// trackers send; <see cref="MicEMessage.Unknown"/> cannot be sent.
    /// </summary>
    public MicEMessage Message { get; init; }

    /// <summary>
    /// Whether the data type identifier marks a current GPS fix (<c>`</c>, or 0x1C in rev. 0 radios). Kenwood
    /// TM-D700 radios send <c>'</c> for current data, so <c>']</c> also counts as current (APRS 1.2). Set it for a
    /// position from a live GPS fix.
    /// </summary>
    public bool IsCurrentFix { get; init; }

    /// <summary>
    /// Whether the type byte says the radio can receive messages (<c>&gt;</c>, <c>]</c> or <c>`</c>). A new report is
    /// sent with <c>`</c> when set and a space when not, without a device code.
    /// </summary>
    public bool IsMessagingCapable { get; init; }

    /// <summary>
    /// The radio or tracker, from the type byte and the trailing manufacturer code (APRS 1.2), such as <c>TH-D74</c>
    /// or <c>FTM-400DR</c>; <see langword="null"/> when not recognised.
    /// </summary>
    public string? Device { get; internal init; }

    /// <summary>A Maidenhead locator that opens the status text, such as <c>IO91SX</c>, in upper case.</summary>
    public string? MaidenheadLocator { get; internal init; }
}
