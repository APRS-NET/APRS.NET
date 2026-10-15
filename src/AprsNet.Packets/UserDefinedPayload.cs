namespace AprsNet.Packets;

/// <summary>
/// User-defined data (<c>{</c>, APRS101 ch. 18): a one-character user ID issued to a program author, a
/// one-character packet type, and data in a format only that program knows. A user ID of <c>{</c> marks an
/// experimental packet that anyone may send. The data is not interpreted.
/// </summary>
public sealed class UserDefinedPayload : AprsPayload
{
    /// <summary>
    /// Creates user-defined data to encode with <see cref="AprsPacket.Create"/>. Set <see cref="UserId"/> (<c>{</c>
    /// for an experimental packet), <see cref="UserPacketType"/> and <see cref="Data"/>.
    /// </summary>
    public UserDefinedPayload() { }

    /// <summary>The user ID: the character after <c>{</c>. Printable ASCII when encoding.</summary>
    public char UserId { get; init; }

    /// <summary>The user-defined packet type: the character after the user ID. Printable ASCII when encoding.</summary>
    public char UserPacketType { get; init; }

    /// <summary>Whether this is an experimental packet (<c>{{</c>), which needs no issued user ID.</summary>
    public bool IsExperimental => UserId == '{';

    /// <summary>The data after the packet type, exactly as sent.</summary>
    public ReadOnlyMemory<byte> DataBytes { get; internal init; }

    /// <summary>
    /// The data after the packet type: decoded as UTF-8 when the bytes are valid UTF-8, otherwise one character per
    /// byte (Latin-1). APRS101 recommends printable ASCII but does not require it.
    /// </summary>
    public string Data { get; init; } = "";
}
