namespace AprsNet.Packets;

/// <summary>
/// An object (<c>;</c>, APRS101 ch. 11): something a station places on the map on behalf of something else, such as a
/// hurricane, a repeater or an event. It has a 9-character name, is live or killed, and always has a timestamp.
/// </summary>
public sealed class ObjectReport : PositionedPayload
{
    internal ObjectReport() { }

    internal ObjectReport(PositionFields fields)
        : base(fields) { }

    /// <summary>The object's name without its padding: <c>LEADER</c> from <c>LEADER   </c>.</summary>
    public string Name { get; internal init; } = "";

    /// <summary>Whether the object is live (<c>*</c>); a killed object (<c>_</c>) should be removed from the map.</summary>
    public bool IsLive { get; internal init; }
}
