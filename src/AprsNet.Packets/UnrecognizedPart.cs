namespace AprsNet.Packets;

/// <summary>
/// A part of a packet that parsed but wasn't understood. The packet is still decoded, and the part is usually kept in
/// its comment. Unlike a <see cref="ParseIssue"/>, nothing is wrong with the packet as far as APRS goes; these are the
/// parts worth a closer look, for example to add to a test corpus.
/// </summary>
/// <param name="Code">What the part is.</param>
/// <param name="Offset">Where it starts in <see cref="AprsPacket.Raw"/>.</param>
/// <param name="Length">How many bytes of <see cref="AprsPacket.Raw"/> it covers.</param>
public readonly record struct UnrecognizedPart(UnrecognizedPartCode Code, int Offset, int Length);
