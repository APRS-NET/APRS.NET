namespace AprsNet.Packets;

/// <summary>
/// The kind of packet, from its data type identifier (the first byte of the information field). The names are a
/// stable vocabulary, also used as metric tag values: members may be added but are never renamed or reused.
/// </summary>
public enum PacketType
{
    /// <summary>No header, an empty information field, or a data type identifier this library does not know.</summary>
    Unknown,

    /// <summary>A position report: <c>!</c>, <c>=</c>, <c>/</c> or <c>@</c>.</summary>
    Position,

    /// <summary>A Mic-E report: <c>`</c> or <c>'</c>, or the old 0x1C and 0x1D identifiers.</summary>
    MicE,

    /// <summary>An object: <c>;</c>.</summary>
#pragma warning disable CA1720 // "object" is the APRS101 name for this packet type
    Object,
#pragma warning restore CA1720

    /// <summary>An item: <c>)</c>.</summary>
    Item,

    /// <summary>A message, acknowledgement, bulletin or telemetry definition: <c>:</c>.</summary>
    Message,

    /// <summary>A status report: <c>&gt;</c>.</summary>
    Status,

    /// <summary>A positionless weather report (<c>_</c>), or raw Ultimeter weather data (<c>!!</c>).</summary>
    Weather,

    /// <summary>A telemetry report: <c>T#</c>.</summary>
    Telemetry,

    /// <summary>Third-party traffic: <c>}</c>.</summary>
    ThirdParty,
}
