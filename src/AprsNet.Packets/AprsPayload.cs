namespace AprsNet.Packets;

/// <summary>
/// The decoded information field of an <see cref="AprsPacket"/>. Each packet type this library decodes has a sealed
/// subclass; switch on the subclass to read it.
/// </summary>
public abstract class AprsPayload
{
    private protected AprsPayload() { }
}
