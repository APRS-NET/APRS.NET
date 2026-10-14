namespace AprsNet.AprsIsTcpClient;

/// <summary>
/// Computes the APRS-IS passcode for a callsign, which a verified login (<see cref="AprsIsClientOptions.Passcode"/>)
/// needs before the server accepts packets from it.
/// </summary>
/// <remarks>
/// The passcode is a public 15-bit hash of the base callsign, not a secret: it only stops people from transmitting
/// by mistake. It is computed by every APRS-IS server and client the same way: start from <c>0x73E2</c>, XOR the
/// upper-cased base callsign into it two characters at a time (the first of each pair into the high byte), and keep
/// the low 15 bits. Only issue passcodes to licensed amateur radio operators for their own callsigns.
/// </remarks>
public static class AprsIsPasscode
{
    private const int _seed = 0x73E2;

    /// <summary>
    /// Returns the passcode for <paramref name="callsign"/>, from 0 to 32767. The SSID and letter case are ignored,
    /// so <c>n0call-9</c> and <c>N0CALL</c> give the same passcode.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The base callsign (before any <c>-</c>) is empty, longer than 9 characters, or not all ASCII letters and digits.
    /// </exception>
    public static int Compute(string callsign)
    {
        ArgumentNullException.ThrowIfNull(callsign);

        var dash = callsign.IndexOf('-', StringComparison.Ordinal);
        var baseCall = dash < 0 ? callsign : callsign[..dash];
        if (baseCall.Length is < 1 or > 9 || !baseCall.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException(
                "Callsign must start with 1 to 9 letters or digits, optionally followed by -SSID.",
                nameof(callsign)
            );
        }

        var hash = _seed;
        for (var i = 0; i < baseCall.Length; i += 2)
        {
            hash ^= char.ToUpperInvariant(baseCall[i]) << 8;
            if (i + 1 < baseCall.Length)
            {
                hash ^= char.ToUpperInvariant(baseCall[i + 1]);
            }
        }

        return hash & 0x7FFF;
    }
}
