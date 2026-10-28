using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace AprsNet.Kiss;

/// <summary>
/// One AGWPE frame: a 36-byte header, then its data. The header holds the radio port (0 for the first), the data kind
/// (a letter), the PID, the calls from and to (ten bytes each, zero-padded) and the data's length, all integers
/// little-endian. See the AGWPE TCP/IP API tutorial (SV2AGW, 2000), as Dire Wolf and UZ7HO SoundModem implement it.
/// </summary>
internal readonly record struct AgwpeFrame(
    int Port,
    char Kind,
    ReadOnlyMemory<byte> Data,
    string From = "",
    string To = ""
)
{
    public const int HeaderLength = 36;

    /// <summary>The most data a frame may carry; anything longer means the stream has lost its place.</summary>
    public const int MaxDataLength = 64 * 1024;

    /// <summary>Asks for the server's version: answered with an <c>R</c> frame.</summary>
    public const char Version = 'R';

    /// <summary>Asks for the radio ports: answered with a <c>G</c> frame.</summary>
    public const char PortInformation = 'G';

    /// <summary>Turns raw frames on or off (the server toggles it), and is the kind of a raw AX.25 frame either way.</summary>
    public const char RawToggle = 'k';

    /// <summary>A raw AX.25 frame, heard or to send: a KISS-style command byte (0), then the frame.</summary>
    public const char Raw = 'K';

    public byte[] Encode()
    {
        var bytes = new byte[HeaderLength + Data.Length];
        bytes[0] = (byte)Port;
        bytes[4] = (byte)Kind;
        Encoding.ASCII.GetBytes(From.AsSpan(0, Math.Min(From.Length, 10)), bytes.AsSpan(8, 10));
        Encoding.ASCII.GetBytes(To.AsSpan(0, Math.Min(To.Length, 10)), bytes.AsSpan(18, 10));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28, 4), Data.Length);
        Data.Span.CopyTo(bytes.AsSpan(HeaderLength));
        return bytes;
    }

    /// <summary>A request with no data, such as <see cref="Version"/>.</summary>
    public static byte[] Request(char kind) => new AgwpeFrame(0, kind, ReadOnlyMemory<byte>.Empty).Encode();

    /// <summary>A raw frame to send on <paramref name="port"/>.</summary>
    public static byte[] ForAx25(int port, ReadOnlySpan<byte> ax25)
    {
        var data = new byte[ax25.Length + 1]; // the command byte, 0: data
        ax25.CopyTo(data.AsSpan(1));
        return new AgwpeFrame(port, Raw, data).Encode();
    }

    /// <summary>
    /// The data's length from a header, or why it can't be used: a length that's negative or over
    /// <see cref="MaxDataLength"/> means the stream isn't AGWPE, or has lost its place.
    /// </summary>
    public static int DataLength(ReadOnlySpan<byte> header) => BinaryPrimitives.ReadInt32LittleEndian(header[28..32]);

    public static AgwpeFrame Decode(ReadOnlySpan<byte> header, ReadOnlyMemory<byte> data) =>
        new(header[0], (char)header[4], data, Call(header.Slice(8, 10)), Call(header.Slice(18, 10)));

    /// <summary>A <c>G</c> frame's ports: <c>2;Port1 first soundcard;Port2 second;</c>, numbered from 0 here.</summary>
    public static IReadOnlyList<AgwpePort> Ports(ReadOnlySpan<byte> data)
    {
        var parts = Encoding.Latin1.GetString(data).TrimEnd('\0').Split(';');
        var ports = new List<AgwpePort>();
        for (var i = 1; i < parts.Length; i++)
        {
            var text = parts[i].Trim();
            if (text.Length == 0)
            {
                continue;
            }

            // "Port1 first soundcard mono": the name is the description after the number; keep the order given.
            var space = text.IndexOf(' ', StringComparison.Ordinal);
            var number = 0;
            var named =
                text.StartsWith("Port", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(
                    text.AsSpan(4, (space < 0 ? text.Length : space) - 4),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out number
                )
                && number >= 1;
            ports.Add(
                named
                    ? new AgwpePort(number - 1, space < 0 ? "" : text[(space + 1)..].Trim())
                    : new AgwpePort(ports.Count, text)
            );
        }

        return ports;
    }

    /// <summary>An <c>R</c> frame's version: two little-endian integers, major and minor, as <c>2005.127</c>.</summary>
    public static string? VersionOf(ReadOnlySpan<byte> data) =>
        data.Length < 8
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{BinaryPrimitives.ReadInt32LittleEndian(data[..4])}.{BinaryPrimitives.ReadInt32LittleEndian(data[4..8])}"
            );

    private static string Call(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? field : field[..end]).Trim();
    }
}
