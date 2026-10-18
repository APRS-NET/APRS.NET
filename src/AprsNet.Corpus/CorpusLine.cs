using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace AprsNet.Corpus;

/// <summary>
/// One line of a corpus file: the UTC time a packet was received, a space, and the raw packet, such as
/// <c>2026-10-12T03:21:18.012Z N0CALL&gt;APRS,TCPIP*:&gt;Hello</c>. The packet is kept byte for byte as Latin-1 text,
/// one character per byte, so data that isn't valid UTF-8 (Mic-E) is never altered.
/// </summary>
public readonly record struct CorpusLine
{
    /// <summary>The timestamp's format: UTC to the millisecond, such as <c>2026-10-12T03:21:18.012Z</c>.</summary>
    public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private const int TimestampLength = 24;

    /// <summary>Creates a line from a packet held as Latin-1 text, such as <c>AprsPacket.ToString()</c> gives.</summary>
    /// <param name="receivedAt">When the packet arrived; kept in UTC, to the millisecond.</param>
    /// <param name="packet">The raw packet, one character per byte.</param>
    /// <exception cref="ArgumentException">
    /// The packet holds a line break, or a character above U+00FF that no single byte can carry.
    /// </exception>
    public CorpusLine(DateTimeOffset receivedAt, string packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        foreach (var c in packet)
        {
            if (c is '\n' or '\r' || c > 'ÿ')
            {
                throw new ArgumentException(
                    c > 'ÿ'
                        ? $"The packet holds U+{(int)c:X4}, which is not one byte; pass the raw bytes instead."
                        : "The packet holds a line break, which would split the line.",
                    nameof(packet)
                );
            }
        }

        var utc = receivedAt.ToUniversalTime();
        ReceivedAt = new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
        Packet = packet;
    }

    /// <summary>Creates a line from a packet's raw bytes, such as <c>AprsPacket.Raw</c>.</summary>
    /// <exception cref="ArgumentException">The packet holds a line break.</exception>
    public CorpusLine(DateTimeOffset receivedAt, ReadOnlySpan<byte> packet)
        : this(receivedAt, Encoding.Latin1.GetString(packet)) { }

    /// <summary>When the packet was received, in UTC, to the millisecond.</summary>
    public DateTimeOffset ReceivedAt { get; }

    /// <summary>The raw packet as Latin-1 text, one character per byte.</summary>
    public string Packet { get; }

    /// <summary>The packet's raw bytes.</summary>
    public byte[] GetPacketBytes() => Encoding.Latin1.GetBytes(Packet);

    /// <summary>The line as a corpus file holds it, without the line break.</summary>
    public override string ToString() =>
        ReceivedAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture) + " " + Packet;

    /// <summary>Reads a line in the corpus format.</summary>
    /// <exception cref="FormatException">The line is not a timestamp, a space and a packet.</exception>
    public static CorpusLine Parse(string line) =>
        TryParse(line, out var result)
            ? result
            : throw new FormatException($"Not '<UTC timestamp> <packet>' ({TimestampFormat}): {line}");

    /// <summary>Reads a line in the corpus format, or returns false.</summary>
    public static bool TryParse([NotNullWhen(true)] string? line, out CorpusLine result)
    {
        result = default;
        if (
            line is null
            || line.Length <= TimestampLength + 1
            || line[TimestampLength] != ' '
            || line.AsSpan(TimestampLength + 1).IndexOfAny('\r', '\n') >= 0
            || !DateTimeOffset.TryParseExact(
                line.AsSpan(0, TimestampLength),
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var receivedAt
            )
        )
        {
            return false;
        }

        result = new CorpusLine(receivedAt, line[(TimestampLength + 1)..]);
        return true;
    }
}
