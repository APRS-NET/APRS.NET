using AprsNet.Packets;

namespace AprsNet.Filters;

/// <summary>
/// Where stations are, for the filter terms that measure from a station rather than a fixed point: <c>m/km</c> uses
/// <see cref="MyPosition"/>, and <c>f/call/km</c> and <c>t/types/call/km</c> use <see cref="LastPosition"/>.
/// </summary>
public sealed class AprsFilterContext
{
    /// <summary>The position of the client the filter belongs to, or <see langword="null"/> when it is not known.</summary>
    public AprsPosition? MyPosition { get; init; }

    /// <summary>
    /// Looks up a station's last known position by callsign (with its SSID, as written in the filter), returning
    /// <see langword="null"/> when it is not known. Callers decide whether the lookup ignores case.
    /// </summary>
    public Func<string, AprsPosition?>? LastPosition { get; init; }
}
