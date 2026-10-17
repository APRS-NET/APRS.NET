using AprsNet.Packets;

namespace AprsNet.Messaging;

/// <summary>
/// A message, ack or rej for the app to send now. The app builds the packet with its own address
/// (<c>PacketAddress.Create(Payload)</c>), choosing the path for the source it sends on.
/// </summary>
/// <param name="Payload">What to send.</param>
/// <param name="To">The station it is for.</param>
/// <param name="Source">
/// The source <see cref="To"/> was last heard on, as named in <see cref="MessagingStation.Receive"/>, so the app can
/// answer the same way; <see langword="null"/> when it hasn't been heard, and the app sends wherever it can.
/// </param>
public sealed record OutgoingPacket(MessagePayload Payload, Callsign To, string? Source);
