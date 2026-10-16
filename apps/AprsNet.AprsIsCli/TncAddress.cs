using System.Globalization;
using AprsNet.Kiss;

namespace AprsNet.AprsIsCli;

/// <summary>
/// Where a TNC is, as <c>--tnc</c> gives it: <c>host[:port]</c> for KISS over TCP, or <c>serial:PORT[@BAUD]</c> for a
/// TNC on a serial port, such as <c>serial:/dev/ttyUSB0</c> or <c>serial:COM3@57600</c>.
/// </summary>
internal sealed record TncAddress
{
    /// <summary>The forms <c>--tnc</c> accepts, for help and error messages.</summary>
    public const string Forms = "host[:port] or serial:PORT[@BAUD]";

    /// <summary>An example of each form, for error messages.</summary>
    public const string Examples = "localhost:8001 or serial:/dev/ttyUSB0";

    private const string SerialPrefix = "serial:";

    private TncAddress() { }

    /// <summary>The host, for TCP.</summary>
    public string Host { get; private init; } = "";

    /// <summary>The TCP port, for TCP.</summary>
    public int Port { get; private init; }

    /// <summary>The serial port's name, for a serial TNC.</summary>
    public string? SerialPort { get; private init; }

    /// <summary>The serial port's speed, for a serial TNC.</summary>
    public int BaudRate { get; private init; }

    /// <summary>Whether the TNC is on a serial port rather than TCP.</summary>
    public bool IsSerial => SerialPort is not null;

    /// <summary>The TNC as messages name it: <c>host:port</c>, or the serial port.</summary>
    public override string ToString() => SerialPort ?? $"{Host}:{Port}";

    /// <summary>Reads <paramref name="text"/> in either form.</summary>
    public static bool TryParse(string text, out TncAddress address)
    {
        address = null!;
        text = text.Trim();
        if (text.StartsWith(SerialPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = text[SerialPrefix.Length..];
            var at = rest.LastIndexOf('@');
            var baud = KissSerialOptions.DefaultBaudRate;
            if (
                at >= 0
                && (
                    !int.TryParse(rest[(at + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out baud)
                    || baud <= 0
                )
            )
            {
                return false;
            }

            var name = (at < 0 ? rest : rest[..at]).Trim();
            if (name.Length == 0)
            {
                return false;
            }

            address = new TncAddress { SerialPort = name, BaudRate = baud };
            return true;
        }

        if (!TncCommand.TryParseAddress(text, out var host, out var port))
        {
            return false;
        }

        address = new TncAddress { Host = host, Port = port };
        return true;
    }
}
