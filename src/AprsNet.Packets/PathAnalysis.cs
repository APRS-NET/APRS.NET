namespace AprsNet.Packets;

/// <summary>
/// What a packet's path says about how it travelled: the digipeaters that repeated it, the hops it still asks for, and
/// how it entered APRS-IS.
/// </summary>
/// <remarks>
/// <para>
/// The path splits at the q-construct (aprs-is.net, "q Construct"). Before it is the radio path: under the New-N
/// paradigm each digipeater inserts its callsign and marks it with <c>*</c>, and TNC2 marks only the last entry that
/// repeated the packet, so every entry up to that one was used. After the q-construct come the IGate or server that
/// passed the packet to APRS-IS and, for <c>qAI</c>, every server it went through.
/// </para>
/// <para>
/// Odd paths never throw: a path without a q-construct is all radio path, a q-construct with nothing after it has no
/// <see cref="EntryStation"/>, and only the first q-construct counts.
/// </para>
/// </remarks>
public sealed class PathAnalysis
{
    // A packet with no path at all was heard directly; one whose header could not be read says nothing.
    private static readonly PathAnalysis _noPath = new([], null, null, []);
    private static readonly PathAnalysis _unknown = new([], null, null, [], known: false);

    private PathAnalysis(
        IReadOnlyList<PathEntry> radioPath,
        string? qConstruct,
        Callsign? entryStation,
        IReadOnlyList<PathEntry> aprsIsPath,
        bool known = true
    )
    {
        QConstruct = qConstruct;
        EntryStation = entryStation;
        AprsIsPath = aprsIsPath;

        var used = 0;
        var remaining = 0;
        var internet = false;
        for (var i = 0; i < radioPath.Count; i++)
        {
            var entry = radioPath[i];
            used = entry.HasBeenRepeated ? i + 1 : used;
            var name = entry.Station.Value;
            internet |= IsMarker(name, "TCPIP") || IsMarker(name, "TCPXX");
            IsNoGate |= IsMarker(name, "NOGATE") || IsMarker(name, "RFONLY");
        }

        for (var i = used; i < radioPath.Count; i++)
        {
            remaining += RemainingHops(radioPath[i]);
        }

        UsedHops = radioPath.Take(used).ToArray();
        Digipeaters = UsedHops.Where(e => e.Kind == PathEntryKind.Station).Select(e => e.Station).ToArray();
        RemainingHopCount = remaining;
        IsFromInternet = internet || qConstruct is "qAC" or "qAX" or "qAU";
        IsGatedFromRadio = !internet && qConstruct is "qAR" or "qAr" or "qAO" or "qAo";
        IsHeardDirectly = known && !IsFromInternet && used == 0 && (IsGatedFromRadio || qConstruct is null);
    }

    /// <summary>
    /// The q-construct as written, such as <c>qAR</c>, or <see langword="null"/> when the packet has none (as from a
    /// TNC). <c>qAR</c> and <c>qAr</c> mean a bidirectional IGate gated it from radio, <c>qAO</c> and <c>qAo</c> a
    /// receive-only one; <c>qAC</c>, <c>qAX</c> and <c>qAU</c> mean the sender connected to a server itself (verified,
    /// unverified, or over UDP); <c>qAS</c> that a server passed it on for another; <c>qAI</c> that it is a trace.
    /// </summary>
    public string? QConstruct { get; }

    /// <summary>
    /// The station after the q-construct: the IGate that gated the packet, or the server the sender connected to
    /// (<c>qAC</c>, <c>qAX</c>, <c>qAU</c>), or the server or client that passed it on (<c>qAS</c>).
    /// </summary>
    public Callsign? EntryStation { get; }

    /// <summary>Every entry after the q-construct; for <c>qAI</c>, each server the packet went through, in order.</summary>
    public IReadOnlyList<PathEntry> AprsIsPath { get; }

    /// <summary>
    /// The radio path entries that repeated the packet, in order: every entry before the q-construct up to the last one
    /// marked <c>*</c>. Includes aliases such as <c>WIDE1*</c> from digipeaters that did not insert their callsign.
    /// </summary>
    public IReadOnlyList<PathEntry> UsedHops { get; }

    /// <summary>The stations among <see cref="UsedHops"/>: the digipeaters that named themselves, in order.</summary>
    public IReadOnlyList<Callsign> Digipeaters { get; }

    /// <summary>
    /// How many more hops the packet asks for: the sum of <c>N</c> over the unused <c>WIDEn-N</c>, <c>TRACEn-N</c> and
    /// regional <c>XXn-N</c> entries. <c>WIDE1-1,WIDE2-1</c> after one digipeater leaves 1.
    /// </summary>
    public int RemainingHopCount { get; }

    /// <summary>
    /// Whether an IGate heard the packet on radio and gated it to APRS-IS: a <c>qAR</c>, <c>qAr</c>, <c>qAO</c> or
    /// <c>qAo</c> construct, with no <c>TCPIP</c> or <c>TCPXX</c> in the path (an IGate's own beacon sent over its
    /// internet connection also carries <c>qAR</c>).
    /// </summary>
    public bool IsGatedFromRadio { get; }

    /// <summary>
    /// Whether the packet came from the internet rather than radio: <c>TCPIP</c> or <c>TCPXX</c> in the path, or a
    /// <c>qAC</c>, <c>qAX</c> or <c>qAU</c> construct. When neither this nor <see cref="IsGatedFromRadio"/> is true
    /// (<c>qAS</c>, <c>qAI</c>, or no q-construct) the path does not say.
    /// </summary>
    public bool IsFromInternet { get; }

    /// <summary>
    /// Whether the packet was heard on radio straight from the sender, with no digipeater: gated from radio, or with no
    /// q-construct at all (as from a TNC), and no used hops.
    /// </summary>
    public bool IsHeardDirectly { get; }

    /// <summary>Whether the sender asked IGates not to gate the packet: <c>NOGATE</c> or <c>RFONLY</c> in the path.</summary>
    public bool IsNoGate { get; }

    /// <summary>Analyses a packet's path; a packet whose header could not be read has an empty one.</summary>
    public static PathAnalysis FromPacket(AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return FromPath(packet.Path);
    }

    /// <summary>
    /// Analyses a path, as <see cref="AprsPacket.Path"/> holds it: <see langword="null"/> when the header could not be
    /// read, which says nothing, or empty when the packet had no path, which means it was heard directly.
    /// </summary>
    public static PathAnalysis FromPath(IReadOnlyList<PathEntry>? path)
    {
        if (path is null)
        {
            return _unknown;
        }

        if (path.Count == 0)
        {
            return _noPath;
        }

        for (var i = 0; i < path.Count; i++)
        {
            if (path[i].Kind == PathEntryKind.QConstruct)
            {
                return new PathAnalysis(
                    path.Take(i).ToArray(),
                    path[i].Station.Value,
                    i + 1 < path.Count ? path[i + 1].Station : null,
                    path.Skip(i + 1).ToArray()
                );
            }
        }

        return new PathAnalysis(path.ToArray(), null, null, []);
    }

    private static bool IsMarker(string name, string marker) => name.Equals(marker, StringComparison.OrdinalIgnoreCase);

    /// <summary>The <c>N</c> of an unused <c>WIDEn-N</c>-style alias, or 0.</summary>
    private static int RemainingHops(PathEntry entry)
    {
        var name = entry.Station.Value;
        var dash = name.Length - 2;
        return
            entry.Kind == PathEntryKind.Alias
            && dash > 1
            && name[dash] == '-'
            && char.IsAsciiDigit(name[dash - 1])
            && name[^1] is >= '0' and <= '7'
            ? name[^1] - '0'
            : 0;
    }
}
