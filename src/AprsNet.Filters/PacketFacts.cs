using AprsNet.Packets;

namespace AprsNet.Filters;

/// <summary>
/// What filter terms ask about a packet. Each fact is worked out on first use, since most filters ask about few.
/// </summary>
internal sealed class PacketFacts(AprsPacket packet)
{
    private bool _positionRead;
    private PositionedPayload? _positioned;
    private PathAnalysis? _path;

    public AprsPacket Packet { get; } = packet;

    /// <summary>The positioned payload, unless the packet has none or the sender marked its position unknown.</summary>
    public PositionedPayload? Positioned
    {
        get
        {
            if (!_positionRead)
            {
                _positionRead = true;
                _positioned = Packet.Payload is PositionedPayload { IsPositionUnknown: false } p ? p : null;
            }

            return _positioned;
        }
    }

    /// <summary>How the packet travelled, from its path.</summary>
    public PathAnalysis Path => _path ??= PathAnalysis.FromPacket(Packet);

    /// <summary>The letter of the <c>qA?</c> construct, such as <c>C</c> for <c>qAC</c>.</summary>
    public char? QConstruct => Path.QConstruct is { } q ? q[^1] : null;

    /// <summary>The station after the q-construct: the IGate or server that brought the packet into APRS-IS.</summary>
    public string? EntryStation => Path.EntryStation?.Value;

    /// <summary>The object or item name, without the padding objects carry.</summary>
    public string? ObjectName =>
        Packet.Payload switch
        {
            ObjectReport o => o.Name.TrimEnd(' '),
            ItemReport i => i.Name,
            _ => null,
        };
}
