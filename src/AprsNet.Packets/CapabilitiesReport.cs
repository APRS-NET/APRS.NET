using System.Globalization;

namespace AprsNet.Packets;

/// <summary>
/// Station capabilities (<c>&lt;</c>, APRS101 ch. 15): a comma-separated list of <c>TOKEN</c> or
/// <c>TOKEN=VALUE</c> entries. IGates send <c>&lt;IGATE,MSG_CNT=n,LOC_CNT=n</c>, often with more counters.
/// </summary>
public sealed class CapabilitiesReport : AprsPayload
{
    /// <summary>
    /// Creates a capabilities report to encode with <see cref="AprsPacket.Create"/>. Set
    /// <see cref="Capabilities"/>, or use <see cref="ForIGate"/>.
    /// </summary>
    public CapabilitiesReport() { }

    /// <summary>
    /// Every capability in the order sent: the token, and its value or <see langword="null"/> for a bare token
    /// (<c>IGATE</c>). Spaces around tokens and values are removed; tokens keep their case. To encode, tokens and values
    /// must not contain commas, tokens no <c>=</c>, and neither may start or end with a space.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Capabilities { get; init; } = [];

    /// <summary>The text after <c>&lt;</c> as sent, so a parsed report is rebuilt with its original spelling.</summary>
    internal ReadOnlyMemory<byte> TextBytes { get; init; }

    /// <summary>
    /// The capabilities an IGate announces (APRS101 ch. 15): <c>&lt;IGATE,MSG_CNT=n,LOC_CNT=n</c>, with the counts
    /// that are known.
    /// </summary>
    /// <param name="messageCount">Messages passed from APRS-IS to RF, or <see langword="null"/> to leave it out.</param>
    /// <param name="localStationCount">Stations heard locally that messages go to, or <see langword="null"/>.</param>
    public static CapabilitiesReport ForIGate(int? messageCount, int? localStationCount)
    {
        var capabilities = new List<KeyValuePair<string, string?>> { new("IGATE", null) };
        if (messageCount is { } messages)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(messages, nameof(messageCount));
            capabilities.Add(new("MSG_CNT", messages.ToString(CultureInfo.InvariantCulture)));
        }

        if (localStationCount is { } stations)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(stations, nameof(localStationCount));
            capabilities.Add(new("LOC_CNT", stations.ToString(CultureInfo.InvariantCulture)));
        }

        return new CapabilitiesReport { Capabilities = capabilities };
    }

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
