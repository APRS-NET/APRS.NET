using System.Text;
using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.Kiss;

/// <summary>Which way a <see cref="KissTrafficEventArgs"/> entry went.</summary>
public enum KissTrafficDirection
{
    /// <summary>From the TNC.</summary>
    Received,

    /// <summary>To the TNC.</summary>
    Sent,
}

/// <summary>
/// What a <see cref="KissTrafficEventArgs"/> entry is. A stable vocabulary: members may be added but are never renamed
/// or reused. <see cref="KissTrafficKindExtensions.Describe"/> gives a sentence for people.
/// </summary>
public enum KissTrafficKind
{
    /// <summary>An APRS frame from the TNC's radio port; <see cref="KissTrafficEventArgs.Packet"/> holds it parsed.</summary>
    Frame,

    /// <summary>
    /// A data frame from the TNC's radio port that isn't APRS: another protocol, or not a valid AX.25 frame
    /// (<see cref="KissTrafficEventArgs.Frame"/> is then null). Skipped by the packet streams.
    /// </summary>
    NotAprs,

    /// <summary>A frame for another radio port of a multi-port TNC; skipped by this connection.</summary>
    OtherPort,

    /// <summary>A KISS command other than data, such as a TNC's reply to a hardware command; skipped.</summary>
    Command,

    /// <summary>A frame dropped for an invalid escape or for being too long; see <see cref="KissTrafficEventArgs.DropReason"/>.</summary>
    DroppedFrame,

    /// <summary>
    /// Bytes outside any frame, a line at a time: what a TNC that isn't in KISS mode prints, or line noise. See
    /// <see cref="KissTrafficEventArgs.LooksLikeText"/> and <see cref="KissTrafficEventArgs.EndsLine"/>.
    /// </summary>
    Unframed,

    /// <summary>A frame sent for the TNC to transmit.</summary>
    Sent,

    /// <summary>Bytes a <see cref="TncSetup"/> script sent to the TNC's command line.</summary>
    SetupSent,

    /// <summary>What the TNC answered while a <see cref="TncSetup"/> script ran.</summary>
    SetupReceived,

    /// <summary>A <see cref="TncSetup"/> script step that failed: an answer that didn't come in time.</summary>
    SetupFailed,

    /// <summary>
    /// Bytes sent to the TNC by hand, outside any frame (<see cref="KissConnection.SendRawAsync"/>): a command typed
    /// at its command line.
    /// </summary>
    RawSent,
}

/// <summary>Why a frame was dropped (<see cref="KissTrafficKind.DroppedFrame"/>).</summary>
public enum KissDropReason
{
    /// <summary>An escape byte (<c>DB</c>) followed by anything but <c>DC</c> or <c>DD</c>.</summary>
    InvalidEscape,

    /// <summary>Longer than the decoder's <see cref="KissDecoder.MaxFrameLength"/>.</summary>
    TooLong,
}

/// <summary>Text for <see cref="KissTrafficKind"/> values.</summary>
public static class KissTrafficKindExtensions
{
    /// <summary>A short sentence describing the kind, for a console or a log.</summary>
    public static string Describe(this KissTrafficKind kind) =>
        kind switch
        {
            KissTrafficKind.Frame => "An APRS frame from the TNC.",
            KissTrafficKind.NotAprs => "A frame that isn't APRS; skipped.",
            KissTrafficKind.OtherPort => "A frame for another radio port; skipped.",
            KissTrafficKind.Command => "A KISS command other than data; skipped.",
            KissTrafficKind.DroppedFrame => "A damaged frame; dropped.",
            KissTrafficKind.Unframed => "Bytes outside any KISS frame.",
            KissTrafficKind.Sent => "A frame sent for the TNC to transmit.",
            KissTrafficKind.SetupSent => "Sent to the TNC's command line by its setup.",
            KissTrafficKind.SetupReceived => "The TNC's answer during its setup.",
            KissTrafficKind.SetupFailed => "The TNC's setup didn't get the answer it expected.",
            KissTrafficKind.RawSent => "Sent to the TNC by hand.",
            _ => kind.ToString(),
        };
}

/// <summary>
/// One entry of a KISS connection's traffic, for a troubleshooting console: a frame, bytes outside any frame, a frame
/// sent, or a step of the TNC's setup. See <see cref="KissConnection.Traffic"/>.
/// </summary>
public sealed class KissTrafficEventArgs : EventArgs
{
    private string? _text;

    internal KissTrafficEventArgs(DateTimeOffset at, KissTrafficKind kind, ReadOnlyMemory<byte> bytes)
    {
        At = at;
        Kind = kind;
        Bytes = bytes;
    }

    /// <summary>When it happened.</summary>
    public DateTimeOffset At { get; }

    /// <summary>Which way it went.</summary>
    public KissTrafficDirection Direction =>
        Kind is KissTrafficKind.Sent or KissTrafficKind.SetupSent or KissTrafficKind.RawSent
            ? KissTrafficDirection.Sent
            : KissTrafficDirection.Received;

    /// <summary>What it is.</summary>
    public KissTrafficKind Kind { get; }

    /// <summary>
    /// The bytes as they crossed the link: a frame with its KISS framing and escapes, a line of text with its line
    /// break, a setup command. A copy, safe to keep.
    /// </summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary><see cref="Bytes"/> as Latin-1 text, one character per byte.</summary>
    public string Text => _text ??= Encoding.Latin1.GetString(Bytes.Span);

    /// <summary>For a frame, the radio port it was for, 0 to 15.</summary>
    public int? Port { get; init; }

    /// <summary>For a frame, its KISS command.</summary>
    public KissCommand? Command { get; init; }

    /// <summary>For a data frame that is valid AX.25, the frame.</summary>
    public Ax25Frame? Frame { get; init; }

    /// <summary>For an APRS frame, the packet parsed from it.</summary>
    public AprsPacket? Packet { get; init; }

    /// <summary>For <see cref="KissTrafficKind.DroppedFrame"/>, why.</summary>
    public KissDropReason? DropReason { get; init; }

    /// <summary>For <see cref="KissTrafficKind.Unframed"/>, whether the bytes are mostly printable text.</summary>
    public bool LooksLikeText { get; init; }

    /// <summary>
    /// For <see cref="KissTrafficKind.Unframed"/> and <see cref="KissTrafficKind.SetupReceived"/>, whether the bytes end
    /// with a line break. A prompt such as <c>cmd:</c> doesn't; the next entry carries on the same line.
    /// </summary>
    public bool EndsLine { get; init; }

    /// <summary>For a setup entry, the script step it came from.</summary>
    public TncScriptStep? SetupStep { get; init; }
}
