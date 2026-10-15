using System.Text;

namespace AprsNet.Packets;

/// <summary>
/// What makes two packets the same packet under the APRS-IS duplicate rule: the source, the destination and the
/// information field, ignoring the path. A packet heard on RF by several IGates, or from both a TNC and APRS-IS, has
/// one identity. A Mic-E destination carries part of the position, so it counts like any other.
/// </summary>
public readonly record struct PacketIdentity
{
    private readonly string? _value;

    private PacketIdentity(string value) => _value = value;

    /// <summary>
    /// The identity as text: <c>SOURCE&gt;DESTINATION:information</c>, or the whole line when it has no header.
    /// </summary>
    public string Value => _value ?? "";

    /// <summary>The identity of <paramref name="packet"/>. Callsigns compare exactly, case included.</summary>
    public static PacketIdentity Of(AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return new PacketIdentity(
            packet is { Source: { } source, Destination: { } destination }
                ? $"{source.Value}>{destination.Value}:{Encoding.Latin1.GetString(packet.Information.Span)}"
                : Encoding.Latin1.GetString(packet.Raw.Span)
        );
    }

    /// <inheritdoc cref="Value"/>
    public override string ToString() => Value;
}
