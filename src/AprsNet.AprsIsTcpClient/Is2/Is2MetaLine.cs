using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using AprsNet.Packets.Is2;

namespace AprsNet.AprsIsTcpClient.Is2;

/// <summary>
/// The <c>#is2-meta</c> line a plaintext APRS-IS client sends before a packet to give IS2 servers its reception
/// metadata (aprsc's <c>doc/IS2PLAIN.md</c>). Every detail of the format is here, so an upstream change is made in one
/// place.
/// </summary>
[Experimental(Is2Diagnostics.Id)]
internal static class Is2MetaLine
{
    private const string Prefix = "#is2-meta";

    /// <summary>The highest modem number a server passes on.</summary>
    private const int MaxModem = 1024;

    /// <summary>
    /// The line for <paramref name="reception"/>, or <see langword="null"/> when no value is usable. A value the server
    /// would ignore is left out: one not finite, a bit rate or frequency of zero or below, an undefined modem, or a
    /// modem number out of range. Numbers use the invariant culture, so a decimal comma never reaches the server.
    /// </summary>
    public static string? Format(ReceptionMetadata? reception)
    {
        if (reception is null)
        {
            return null;
        }

        var line = new StringBuilder(Prefix);
        var empty = line.Length;
        if (reception.RssiDbm is { } rssi && double.IsFinite(rssi))
        {
            Append(line, "rx_rssi_dbm", rssi);
        }

        if (reception.SnrDb is { } snr && double.IsFinite(snr))
        {
            Append(line, "rx_snr_db", snr);
        }

        if ((int)reception.Modem is > 0 and <= MaxModem)
        {
            Append(line, "rx_modem", (int)reception.Modem);
        }

        if (reception.BitRate is > 0 and { } bitRate)
        {
            Append(line, "rx_bitrate", bitRate);
        }

        if (reception.FrequencyHz is { } frequency && double.IsFinite(frequency) && frequency > 0)
        {
            Append(line, "rx_frequency", frequency);
        }

        return line.Length == empty ? null : line.ToString();
    }

    private static void Append(StringBuilder line, string key, double value) =>
        line.Append(' ')
            .Append(key)
            .Append('=')
            .Append(value.ToString("0.###############", CultureInfo.InvariantCulture));

    private static void Append(StringBuilder line, string key, int value) =>
        line.Append(' ').Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture));
}
