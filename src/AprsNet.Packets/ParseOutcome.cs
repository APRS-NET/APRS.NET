namespace AprsNet.Packets;

/// <summary>
/// How far parsing got. A stable vocabulary, also used as metric tag values: members may be added but are never
/// renamed or reused.
/// </summary>
public enum ParseOutcome
{
    /// <summary>The header and the payload were decoded. <see cref="AprsPacket.Issues"/> may still hold warnings.</summary>
    Parsed,

    /// <summary>The header was decoded, but this library does not decode this packet type yet.</summary>
    Unsupported,

    /// <summary>The header, or a payload this library decodes, has an error.</summary>
    Malformed,
}
