using AprsNet.Ax25;

namespace AprsNet.Kiss;

/// <summary>What a KISS frame tells the TNC (KISS protocol, Chepponis and Karn 1987): the low nibble of its first byte.</summary>
public enum KissCommand
{
    /// <summary>The rest of the frame is an AX.25 frame to send, or one the TNC received.</summary>
    Data = 0,

    /// <summary>Transmitter keyup delay, in 10 ms units.</summary>
    TxDelay = 1,

    /// <summary>Persistence parameter for CSMA, 0 to 255.</summary>
    Persistence = 2,

    /// <summary>Slot interval, in 10 ms units.</summary>
    SlotTime = 3,

    /// <summary>Time to hold the transmitter after the frame, in 10 ms units (obsolete).</summary>
    TxTail = 4,

    /// <summary>0 for half duplex, anything else for full duplex.</summary>
    FullDuplex = 5,

    /// <summary>A TNC-specific setting.</summary>
    SetHardware = 6,

    /// <summary>Leave KISS mode (the whole command byte is <c>FF</c>, on no port).</summary>
    Return = 15,
}

/// <summary>
/// One KISS frame: the TNC port (0 to 15, for multi-port TNCs), the command, and its data, unescaped. For
/// <see cref="KissCommand.Data"/> the data is an AX.25 frame without flags or checksum.
/// </summary>
/// <param name="Port">The TNC port, 0 to 15.</param>
/// <param name="Command">What the frame tells the TNC.</param>
/// <param name="Payload">The data after the command byte, unescaped.</param>
public readonly record struct KissFrame(int Port, KissCommand Command, ReadOnlyMemory<byte> Payload)
{
    /// <summary>Frame end: starts and ends every frame.</summary>
    public const byte FrameEnd = 0xC0;

    /// <summary>Frame escape: the next byte stands for <see cref="FrameEnd"/> or <see cref="FrameEscape"/>.</summary>
    public const byte FrameEscape = 0xDB;

    /// <summary>After <see cref="FrameEscape"/>, a <see cref="FrameEnd"/> byte in the data.</summary>
    public const byte TransposedFrameEnd = 0xDC;

    /// <summary>After <see cref="FrameEscape"/>, a <see cref="FrameEscape"/> byte in the data.</summary>
    public const byte TransposedFrameEscape = 0xDD;

    /// <summary>A data frame carrying <paramref name="frame"/> to <paramref name="port"/>.</summary>
    public static KissFrame ForAx25(Ax25Frame frame, int port = 0)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return new KissFrame(port, KissCommand.Data, frame.Raw);
    }

    /// <summary>
    /// The frame on the wire: <c>FEND</c>, the command byte (port in the high nibble), the escaped data, <c>FEND</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The port is not 0 to 15.</exception>
    public byte[] Encode()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Port, nameof(Port));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, 15, nameof(Port));
        var data = Payload.Span;
        var escapes = data.Count(FrameEnd) + data.Count(FrameEscape);
        var bytes = new byte[data.Length + escapes + 3];
        var at = 0;
        bytes[at++] = FrameEnd;
        bytes[at++] = Command == KissCommand.Return ? (byte)0xFF : (byte)((Port << 4) | (int)Command);
        foreach (var b in data)
        {
            switch (b)
            {
                case FrameEnd:
                    bytes[at++] = FrameEscape;
                    bytes[at++] = TransposedFrameEnd;
                    break;
                case FrameEscape:
                    bytes[at++] = FrameEscape;
                    bytes[at++] = TransposedFrameEscape;
                    break;
                default:
                    bytes[at++] = b;
                    break;
            }
        }

        bytes[at] = FrameEnd;
        return bytes;
    }

    /// <summary>
    /// Decodes the payload of a <see cref="KissCommand.Data"/> frame as AX.25. Returns <see langword="false"/> for other
    /// commands and for data that is not a valid frame.
    /// </summary>
    public bool TryGetAx25(out Ax25Frame? frame)
    {
        frame = null;
        return Command == KissCommand.Data && Ax25Frame.TryDecode(Payload.Span, out frame, out _);
    }
}
