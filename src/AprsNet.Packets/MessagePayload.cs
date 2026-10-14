namespace AprsNet.Packets;

/// <summary>Which kind of message a <see cref="MessagePayload"/> is (APRS101 ch. 14 and 15).</summary>
public enum MessageKind
{
    /// <summary>A text message to a station or service (such as <c>EMAIL</c>).</summary>
    Message,

    /// <summary>An acknowledgement of a message: <c>ack</c> and its message ID.</summary>
    Acknowledgement,

    /// <summary>A rejection of a message: <c>rej</c> and its message ID.</summary>
    Rejection,

    /// <summary>A bulletin to everyone: addressee <c>BLN0</c> to <c>BLN9</c>, or a group bulletin such as <c>BLN4WX</c>.</summary>
    Bulletin,

    /// <summary>
    /// An announcement: addressee <c>BLNA</c> to <c>BLNZ</c>, or a longer name such as <c>BLNALUX</c>, read as
    /// announcement <c>A</c> to group <c>LUX</c>.
    /// </summary>
    Announcement,

    /// <summary>A National Weather Service bulletin: addressee <c>NWS-…</c>.</summary>
    NwsBulletin,

    /// <summary>A directed query such as <c>?APRSD</c> or <c>?PING?</c> (APRS101 ch. 15).</summary>
    Query,
}

/// <summary>
/// A message (<c>:ADDRESSEE:text{id</c>, APRS101 ch. 14): a text message, an acknowledgement or rejection, a
/// bulletin, announcement or NWS bulletin, or a directed query. Telemetry definitions, which also use the message
/// format, are a <see cref="TelemetryDefinition"/>.
/// </summary>
public sealed class MessagePayload : AprsPayload
{
    /// <summary>
    /// Creates a message to encode with <see cref="AprsPacket.Create"/>. Set <see cref="Addressee"/>, and
    /// <see cref="Text"/> and an optional <see cref="MessageId"/> for a text message or bulletin, or
    /// <see cref="Kind"/> and the <see cref="MessageId"/> being answered for an acknowledgement or rejection.
    /// </summary>
    public MessagePayload() { }

    /// <summary>The addressee without its padding, for example <c>WU2Z</c>, <c>BLN3</c> or <c>NWS-WARN</c>.</summary>
    public string Addressee { get; init; } = "";

    /// <summary>What kind of message this is.</summary>
    public MessageKind Kind { get; init; }

    /// <summary>
    /// The message text without the message ID: decoded as UTF-8 when the bytes are valid UTF-8 (APRS 1.2),
    /// otherwise one character per byte. Empty for acknowledgements and rejections.
    /// </summary>
    public string Text { get; init; } = "";

    /// <summary>The text exactly as sent, without the message ID.</summary>
    public ReadOnlyMemory<byte> TextBytes { get; internal init; }

    /// <summary>
    /// The message ID (<c>{003</c> gives <c>003</c>), or for an acknowledgement or rejection the ID it answers.
    /// With an APRS 1.1 reply-ack (<c>{MM}AA</c>) this is the <c>MM</c> part.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    /// The APRS 1.1 reply-ack part (<c>AA</c> in <c>{MM}AA</c>): the ID of a message this one also acknowledges.
    /// Empty when the sender only advertises reply-ack support (<c>{MM}</c>), <see langword="null"/> without it.
    /// </summary>
    public string? ReplyAck { get; init; }

    /// <summary>Whether the sender supports APRS 1.1 reply-acks (its message ID ends in <c>}</c>).</summary>
    public bool IsReplyAckCapable => ReplyAck is not null;

    /// <summary>Whether the receiver should acknowledge: a text message with a message ID.</summary>
    public bool IsAckRequired => Kind == MessageKind.Message && MessageId is not null;

    /// <summary>The bulletin or announcement identifier: <c>3</c> for <c>BLN3</c>, <c>Q</c> for <c>BLNQ</c>.</summary>
    public string? BulletinId { get; internal init; }

    /// <summary>The group of a group bulletin or announcement: <c>WX</c> for <c>BLN4WX</c>, <c>LUX</c> for <c>BLNALUX</c>.</summary>
    public string? BulletinGroup { get; internal init; }

    /// <summary>The query of a directed query: <c>APRSD</c>, <c>APRSH</c>, <c>PING</c> and so on.</summary>
    public string? QueryType { get; internal init; }

    /// <summary>What follows the query type, such as the callsign in <c>?APRSH N0CALL</c>.</summary>
    public string? QueryArgument { get; internal init; }
}
