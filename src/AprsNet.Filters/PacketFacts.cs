using AprsNet.Packets;

namespace AprsNet.Filters;

/// <summary>
/// What filter terms ask about a packet. Each fact is worked out on first use, since most filters ask about few.
/// </summary>
internal sealed class PacketFacts(AprsPacket packet)
{
    private bool _pathRead;
    private bool _positionRead;
    private PositionedPayload? _positioned;
    private char? _qConstruct;
    private string? _entryStation;
    private int _digipeaterCount;

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

    /// <summary>The letter of the <c>qA?</c> construct, such as <c>C</c> for <c>qAC</c>.</summary>
    public char? QConstruct
    {
        get
        {
            ReadPath();
            return _qConstruct;
        }
    }

    /// <summary>The station after the q-construct: the IGate or server that brought the packet into APRS-IS.</summary>
    public string? EntryStation
    {
        get
        {
            ReadPath();
            return _entryStation;
        }
    }

    /// <summary>
    /// How many path entries, from the first, repeated the packet. TNC2 marks only the last digipeater that did; every
    /// entry before it was used too.
    /// </summary>
    public int DigipeaterCount
    {
        get
        {
            ReadPath();
            return _digipeaterCount;
        }
    }

    /// <summary>The object or item name, without the padding objects carry.</summary>
    public string? ObjectName =>
        Packet.Payload switch
        {
            ObjectReport o => o.Name.TrimEnd(' '),
            ItemReport i => i.Name,
            _ => null,
        };

    private void ReadPath()
    {
        if (_pathRead)
        {
            return;
        }

        _pathRead = true;
        var path = Packet.Path;
        if (path is null)
        {
            return;
        }

        var radioPath = path.Count;
        for (var i = 0; i < path.Count; i++)
        {
            if (path[i].Kind == PathEntryKind.QConstruct)
            {
                _qConstruct = path[i].Station.Value[^1];
                _entryStation = i + 1 < path.Count ? path[i + 1].Station.Value : null;
                radioPath = i;
                break;
            }
        }

        for (var i = 0; i < radioPath; i++)
        {
            if (path[i].HasBeenRepeated)
            {
                _digipeaterCount = i + 1;
            }
        }
    }
}
