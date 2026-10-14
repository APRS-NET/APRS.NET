using System.Globalization;

namespace AprsNet.Packets;

/// <summary>
/// Station capabilities (<c>&lt;</c>, APRS101 ch. 15): a comma-separated list of <c>TOKEN</c> or
/// <c>TOKEN=VALUE</c> entries. IGates send <c>&lt;IGATE,MSG_CNT=n,LOC_CNT=n</c>, often with more counters.
/// </summary>
public sealed class CapabilitiesReport : AprsPayload
{
    internal CapabilitiesReport() { }

    /// <summary>
    /// Every capability in the order sent: the token, and its value or <see langword="null"/> for a bare token
    /// (<c>IGATE</c>). Spaces around tokens and values are removed; tokens keep their case.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Capabilities { get; internal init; } = [];

    /// <summary>Whether the station announces itself as an IGate (an <c>IGATE</c> token).</summary>
    public bool IsIGate => Capabilities.Any(c => c.Key.Equals("IGATE", StringComparison.OrdinalIgnoreCase));

    /// <summary>Messages the IGate has passed from APRS-IS to RF (<c>MSG_CNT</c>), if sent as a number.</summary>
    public int? MessageCount => Number("MSG_CNT");

    /// <summary>Local stations the IGate will pass messages to on RF (<c>LOC_CNT</c>), if sent as a number.</summary>
    public int? LocalStationCount => Number("LOC_CNT");

    /// <summary>The value of the first <paramref name="token"/>, or <see langword="null"/> if it was not sent.</summary>
    public string? GetValue(string token) =>
        Capabilities.FirstOrDefault(c => c.Key.Equals(token, StringComparison.OrdinalIgnoreCase)).Value;

    private int? Number(string token) =>
        int.TryParse(GetValue(token), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
}
