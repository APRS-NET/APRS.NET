namespace AprsNet.Packets;

/// <summary>How serious a <see cref="ParseIssue"/> is.</summary>
public enum ParseIssueSeverity
{
    /// <summary>Off-spec, but the packet was still decoded.</summary>
    Warning,

    /// <summary>The packet, or the part of it this issue is about, could not be decoded.</summary>
    Error,
}
