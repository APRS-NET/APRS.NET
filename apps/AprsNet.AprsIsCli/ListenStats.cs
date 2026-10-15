using System.Globalization;
using System.Text;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli;

/// <summary>
/// Counts for <c>aprscli listen --stats</c>: packets by type, how many could not be decoded, and messages addressed
/// to the listening callsign.
/// </summary>
internal sealed class ListenStats(string callsign)
{
    private readonly Dictionary<PacketType, int> _byType = [];

    public int Packets { get; private set; }

    public int Malformed { get; private set; }

    public int Unsupported { get; private set; }

    /// <summary>Messages, acknowledgements and rejections addressed to the callsign (any case), not bulletins.</summary>
    public int ToCallsign { get; private set; }

    public void Add(AprsPacket packet)
    {
        Packets++;
        _byType[packet.Type] = _byType.GetValueOrDefault(packet.Type) + 1;
        Malformed += packet.Outcome == ParseOutcome.Malformed ? 1 : 0;
        Unsupported += packet.Outcome == ParseOutcome.Unsupported ? 1 : 0;
        if (
            packet.Payload is MessagePayload message
            && message.Kind is MessageKind.Message or MessageKind.Acknowledgement or MessageKind.Rejection
            && message.Addressee.Equals(callsign, StringComparison.OrdinalIgnoreCase)
        )
        {
            ToCallsign++;
        }
    }

    /// <summary>
    /// One line: <c>10m: 4,512 packets (12 malformed, 3 unsupported, 2 to N0CALL); position 2,100, micE 900, …</c>,
    /// types by count, largest first.
    /// </summary>
    public string Summary(TimeSpan elapsed)
    {
        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"{Elapsed(elapsed)}: {Packets:N0} packets");
        line.Append(
            CultureInfo.InvariantCulture,
            $" ({Malformed:N0} malformed, {Unsupported:N0} unsupported, {ToCallsign:N0} to {callsign})"
        );
        if (_byType.Count > 0)
        {
            line.Append("; ")
                .AppendJoin(
                    ", ",
                    _byType
                        .OrderByDescending(entry => entry.Value)
                        .ThenBy(entry => entry.Key)
                        .Select(entry =>
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"{PacketFormatter.TypeName(entry.Key)} {entry.Value:N0}"
                            )
                        )
                );
        }

        return line.ToString();
    }

    /// <summary>Whole seconds, minutes or hours as they add up: <c>45s</c>, <c>12m 30s</c>, <c>2h 5m</c>.</summary>
    private static string Elapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
        : elapsed.TotalMinutes >= 1 ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s"
        : $"{(int)elapsed.TotalSeconds}s";
}
