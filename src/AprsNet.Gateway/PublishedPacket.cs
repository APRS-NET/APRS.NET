using AprsNet.Packets;
using AprsNet.Packets.Json;

namespace AprsNet.Gateway;

/// <summary>
/// A packet as the server published it, with its forms for each kind of client, each made once however many clients
/// receive it.
/// </summary>
internal sealed class PublishedPacket(AprsPacket packet, DateTimeOffset received)
{
    private byte[]? _line;
    private byte[]? _json;

    public AprsPacket Packet { get; } = packet;

    public DateTimeOffset Received { get; } = received;

    /// <summary>The raw line and CRLF, for APRS-IS clients.</summary>
    public byte[] Line => _line ??= MakeLine(Packet);

    /// <summary>The packet as UTF-8 JSON (<see cref="AprsPacketJson"/>), for WebSocket clients.</summary>
    public ReadOnlyMemory<byte> Json =>
        _json ??= System.Text.Encoding.UTF8.GetBytes(AprsPacketJson.Serialize(Packet, Received));

    private static byte[] MakeLine(AprsPacket packet)
    {
        var raw = packet.Raw.Span;
        var line = new byte[raw.Length + 2];
        raw.CopyTo(line);
        "\r\n"u8.CopyTo(line.AsSpan(raw.Length));
        return line;
    }
}

/// <summary>Something besides an APRS-IS client that receives what an <see cref="AprsIsServer"/> publishes.</summary>
internal interface IPacketSubscriber
{
    /// <summary>Called for each packet, under the server's publishing lock: queue it and return at once.</summary>
    void Offer(PublishedPacket packet);
}
