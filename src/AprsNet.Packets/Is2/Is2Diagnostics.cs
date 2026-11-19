namespace AprsNet.Packets.Is2;

/// <summary>The diagnostic ID that marks the experimental IS2 API.</summary>
internal static class Is2Diagnostics
{
    /// <summary>
    /// Suppress it (<c>&lt;NoWarn&gt;$(NoWarn);APRSNET_IS2&lt;/NoWarn&gt;</c>) to use the IS2 API, knowing it may change.
    /// </summary>
    public const string Id = "APRSNET_IS2";
}
