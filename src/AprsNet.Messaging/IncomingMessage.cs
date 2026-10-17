using AprsNet.Packets;

namespace AprsNet.Messaging;

/// <summary>A message addressed to a <see cref="MessagingStation"/>, from <see cref="MessagingStation.MessageReceived"/>.</summary>
/// <param name="From">The station that sent it.</param>
/// <param name="Text">The text.</param>
/// <param name="MessageId">
/// Its message ID without any reply-ack part, or <see langword="null"/> when it has none and so isn't acked.
/// </param>
/// <param name="ReceivedAt">When the first copy arrived.</param>
/// <param name="Source">The source it arrived on, as named in <see cref="MessagingStation.Receive"/>.</param>
/// <param name="Packet">The packet it arrived in.</param>
public sealed record IncomingMessage(
    Callsign From,
    string Text,
    string? MessageId,
    DateTimeOffset ReceivedAt,
    string? Source,
    AprsPacket Packet
);

/// <summary>A message received by a <see cref="MessagingStation"/>.</summary>
public sealed class MessageReceivedEventArgs : EventArgs
{
    internal MessageReceivedEventArgs(IncomingMessage message) => Message = message;

    /// <summary>The message.</summary>
    public IncomingMessage Message { get; }
}
