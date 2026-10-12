namespace AprsNet.Packets;

/// <summary>A problem found while parsing a packet.</summary>
/// <param name="Code">What the problem is.</param>
/// <param name="Severity">Whether it stopped the packet from being decoded.</param>
/// <param name="Offset">The byte position in <see cref="AprsPacket.Raw"/> where it was found.</param>
public readonly record struct ParseIssue(ParseIssueCode Code, ParseIssueSeverity Severity, int Offset);
