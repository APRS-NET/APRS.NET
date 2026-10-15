namespace AprsNet.Packets;

/// <summary>
/// Third-party traffic (<c>}</c>, APRS101 ch. 17): a whole packet carried inside another, as a gateway sends what it
/// heard from another network, <c>}W1AW&gt;APRS,TCPIP,N0CALL*:&gt;Status</c>. The inner packet is parsed like any
/// other line, so its own <see cref="AprsPacket.Payload"/> and <see cref="AprsPacket.Issues"/> describe it.
/// </summary>
public sealed class ThirdPartyPayload : AprsPayload
{
    /// <summary>Wraps <paramref name="packet"/> as third-party traffic, to encode with <see cref="AprsPacket.Create"/>.</summary>
    /// <param name="packet">The packet to carry; it needs a <c>SOURCE&gt;DESTINATION</c> header.</param>
    public ThirdPartyPayload(AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        Packet = packet;
    }

    /// <summary>The carried packet: its header (with the path the gateway gave it) and information field.</summary>
    public AprsPacket Packet { get; }
}
