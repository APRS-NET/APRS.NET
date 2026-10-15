using AprsNet.Packets;

namespace AprsNet.Filters;

/// <summary>What filter terms ask about a packet, worked out once per packet.</summary>
internal sealed class PacketFacts
{
    public PacketFacts(AprsPacket packet)
    {
        Packet = packet;
        if (packet.Payload is PositionedPayload { IsPositionUnknown: false } positioned)
        {
            Position = positioned.Position;
            Symbol = positioned.Symbol;
        }

        var path = packet.Path ?? [];
        var qIndex = -1;
        for (var i = 0; i < path.Count; i++)
        {
            if (path[i].Kind == PathEntryKind.QConstruct)
            {
                qIndex = i;
                break;
            }
        }

        if (qIndex >= 0)
        {
            var value = path[qIndex].Station.Value;
            QConstruct = value[^1];
            EntryStation = qIndex + 1 < path.Count ? path[qIndex + 1].Station.Value : null;
        }

        // TNC2 marks only the last digipeater that repeated the packet; every entry before it was used too.
        var radioPath = qIndex >= 0 ? qIndex : path.Count;
        var lastRepeated = -1;
        for (var i = 0; i < radioPath; i++)
        {
            if (path[i].HasBeenRepeated)
            {
                lastRepeated = i;
            }
        }

        Digipeaters = [.. path.Take(lastRepeated + 1).Select(entry => entry.Station.Value)];
    }

    public AprsPacket Packet { get; }

    /// <summary>The position the packet reports, unless it has none or the sender marked it unknown.</summary>
    public AprsPosition? Position { get; }

    public AprsSymbol? Symbol { get; }

    /// <summary>The letter of the <c>qA?</c> construct, such as <c>C</c> for <c>qAC</c>.</summary>
    public char? QConstruct { get; }

    /// <summary>The station after the q-construct: the IGate or server that brought the packet into APRS-IS.</summary>
    public string? EntryStation { get; }

    /// <summary>The digipeaters that repeated the packet, in order.</summary>
    public IReadOnlyList<string> Digipeaters { get; }

    /// <summary>The object or item name, without the padding objects carry.</summary>
    public string? ObjectName =>
        Packet.Payload switch
        {
            ObjectReport o => o.Name.TrimEnd(' '),
            ItemReport i => i.Name,
            _ => null,
        };
}
