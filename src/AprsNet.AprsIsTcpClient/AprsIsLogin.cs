using System.Globalization;

namespace AprsNet.AprsIsTcpClient;

/// <summary>
/// Logins for people who only listen: a generated listener ID such as <c>SWL482913</c>, so someone without a licence
/// never types a made-up or borrowed callsign. Set <see cref="AprsIsClientOptions.ListenOnly"/> to use one.
/// </summary>
/// <remarks>
/// A listener ID is <c>SWL</c> and six random digits: nine characters, the most an APRS-IS login takes. An amateur
/// callsign ends in a letter, so it can never be one, and the random digits keep two listeners from sharing a login. It
/// always logs in read-only (passcode <c>-1</c>), which servers accept for receiving. It is too long for an AX.25
/// address, so it can never be a packet's source.
/// </remarks>
public static class AprsIsLogin
{
    /// <summary>What every listener ID starts with: short-wave listener.</summary>
    public const string ListenerPrefix = "SWL";

    /// <summary>Makes a new listener ID, such as <c>SWL482913</c>.</summary>
    /// <param name="random">The random source; <see cref="Random.Shared"/> by default. Tests can pass a seeded one.</param>
    public static string NewListenerId(Random? random = null) =>
        ListenerPrefix + (random ?? Random.Shared).Next(1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether <paramref name="login"/> is a listener ID (<c>SWL</c> and six digits, in any case) rather than a
    /// callsign, so an app can say "listening as SWL482913" and refuse to set up transmitting.
    /// </summary>
    public static bool IsListenerId(string? login) =>
        login is { Length: 9 }
        && login.StartsWith(ListenerPrefix, StringComparison.OrdinalIgnoreCase)
        && login.AsSpan(3).IndexOfAnyExceptInRange('0', '9') < 0;
}
