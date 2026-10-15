using System.Text.RegularExpressions;
using AprsNet.AprsIsTcpClient;

namespace AprsNet.Gateway;

/// <summary>
/// A client's login: <c>user CALL [pass NNNNN] [vers NAME VERSION] [filter TERMS…]</c>. Words are case-insensitive;
/// anything after <c>filter</c> is the filter.
/// </summary>
internal sealed partial record LoginLine(string Callsign, bool IsVerified, string Software, string Filter)
{
    public static LoginLine? TryParse(string line)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || !Is(words[0], "user") || !LoginCallsign().IsMatch(words[1]))
        {
            return null;
        }

        var callsign = words[1];
        var verified = false;
        var software = "";
        var filter = "";
        for (var i = 2; i < words.Length; i++)
        {
            if (Is(words[i], "pass") && i + 1 < words.Length)
            {
                verified = int.TryParse(words[++i], out var passcode) && passcode == AprsIsPasscode.Compute(callsign);
            }
            else if (Is(words[i], "vers"))
            {
                software = string.Join(' ', words.Skip(i + 1).Take(2));
                i += 2;
            }
            else if (Is(words[i], "filter"))
            {
                filter = string.Join(' ', words.Skip(i + 1));
                break;
            }
        }

        return new LoginLine(callsign, verified, software, filter);
    }

    private static bool Is(string word, string keyword) => word.Equals(keyword, StringComparison.OrdinalIgnoreCase);

    // A base call of 1 to 9 letters and digits, so the passcode can be computed, and an optional SSID.
    [GeneratedRegex("^[A-Za-z0-9]{1,9}(-[A-Za-z0-9]{1,2})?$")]
    private static partial Regex LoginCallsign();
}
