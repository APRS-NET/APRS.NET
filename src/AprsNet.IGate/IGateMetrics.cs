using System.Diagnostics.Metrics;
using AprsNet.Packets;

namespace AprsNet.IGate;

/// <summary>
/// Instruments for IGates, on the <c>AprsNet.IGate</c> meter. Every measurement carries the IGate's callsign as an
/// <c>igate</c> tag, one value per IGate; the refusal reason comes from a fixed enum, so cardinality stays bounded.
/// </summary>
internal static class IGateMetrics
{
    public const string MeterName = "AprsNet.IGate";

    private static readonly Meter _meter = new(MeterName);

    private static readonly Counter<long> _gated = _meter.CreateCounter<long>(
        "aprsnet.igate.packets.gated",
        "{packet}",
        "Packets heard on RF and passed to APRS-IS."
    );

    private static readonly Counter<long> _refused = _meter.CreateCounter<long>(
        "aprsnet.igate.packets.refused",
        "{packet}",
        "Packets heard on RF and not passed to APRS-IS, by reason."
    );

    public static void Gated(Callsign igate)
    {
        _gated.Add(1, new KeyValuePair<string, object?>("igate", igate.Value));
    }

    public static void Refused(Callsign igate, GateRefusal reason)
    {
        _refused.Add(
            1,
            new KeyValuePair<string, object?>("igate", igate.Value),
            new KeyValuePair<string, object?>("reason", Reason(reason))
        );
    }

    /// <summary>The <c>reason</c> tag: <see cref="GateRefusal.FromInternet"/> → <c>from_internet</c>.</summary>
    internal static string Reason(GateRefusal reason) =>
        reason switch
        {
            GateRefusal.NotAprs => "not_aprs",
            GateRefusal.Unreadable => "unreadable",
            GateRefusal.FromInternet => "from_internet",
            GateRefusal.NoGate => "no_gate",
            GateRefusal.AlreadyGated => "already_gated",
            GateRefusal.GeneralQuery => "general_query",
            GateRefusal.Empty => "empty",
            GateRefusal.TooLong => "too_long",
            GateRefusal.Duplicate => "duplicate",
            _ => "other",
        };
}
