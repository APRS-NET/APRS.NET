namespace AprsNet.Packets;

/// <summary>
/// An object (<c>;</c>, APRS101 ch. 11): something a station places on the map on behalf of something else, such as a
/// hurricane, a repeater or an event. It has a 9-character name, is live or killed, and always has a timestamp.
/// </summary>
public sealed class ObjectReport : PositionedPayload
{
    /// <summary>
    /// Creates an object to send with <see cref="AprsPacket.Create"/>: a name, a timestamp, and the position body that
    /// position reports have. It is live unless <see cref="IsLive"/> is set to <see langword="false"/>.
    /// </summary>
    public ObjectReport() { }

    internal ObjectReport(PositionFields fields)
        : base(fields) { }

    /// <summary>
    /// The object's name without its padding: <c>LEADER</c> from <c>LEADER   </c>. A new name is 1 to 9 printable ASCII
    /// characters without trailing spaces; it is padded to 9 when sent.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Whether the object is live (<c>*</c>); a killed object (<c>_</c>) should be removed from the map. To kill an
    /// object, send it again with this set to <see langword="false"/>. New objects are live.
    /// </summary>
    public bool IsLive { get; init; } = true;
}
