namespace AprsNet.Packets;

/// <summary>
/// Identifies the device or program that sent a packet, from the aprs-deviceid list
/// (<see href="https://github.com/aprsorg/aprs-deviceid"/>): by its destination (TOCALL), or for Mic-E by the codes
/// around its comment.
/// </summary>
/// <remarks>
/// The list is built into the library (see <see cref="ListCommit"/> and <see cref="ListDate"/>), so lookups need no file
/// or network access. Its data is licensed under CC BY-SA 2.0 by its maintainers, Heikki Hannikainen, OH7LZB, and
/// contributors. In TOCALL patterns <c>?</c> stands for any character, <c>n</c> for a digit and a final <c>*</c> for
/// any characters; when several match, the one with the most literal characters wins.
/// </remarks>
public static partial class AprsDevices
{
    /// <summary>The aprs-deviceid commit the built-in list was taken from.</summary>
    public static string ListCommit => _listCommit;

    /// <summary>The date of the aprs-deviceid commit the built-in list was taken from.</summary>
    public static DateOnly ListDate => _listDate;

    /// <summary>
    /// The device that sent <paramref name="packet"/>: for Mic-E, the <see cref="MicEReport.Device"/> its codes name
    /// (the destination holds the position there); otherwise the one its destination names.
    /// </summary>
    public static AprsDevice? Identify(AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return packet.Type == PacketType.MicE ? (packet.Payload as MicEReport)?.Device
            : packet.Destination is { } destination ? FromTocall(destination.Value)
            : null;
    }

    /// <summary>
    /// The device a destination such as <c>APDW17</c> names, or <see langword="null"/> when the list has none. An SSID
    /// (<c>APDW17-1</c>) is ignored.
    /// </summary>
    public static AprsDevice? FromTocall(string destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var dash = destination.IndexOf('-', StringComparison.Ordinal);
        var tocall = dash < 0 ? destination : destination[..dash];
        if (_tocalls.TryGetValue(tocall, out var device))
        {
            return device;
        }

        foreach (var (pattern, candidate) in _tocallPatterns)
        {
            if (Matches(pattern, tocall))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The device a new-style Mic-E code (the two characters ending the comment) names.</summary>
    internal static AprsDevice? FromMicECode(string code) => _micE.GetValueOrDefault(code);

    /// <summary>
    /// The legacy Mic-E device a type byte (<c>&gt;</c> or <c>]</c>) and, if it has one, the last character of the
    /// comment name; <paramref name="suffixUsed"/> says whether that character was part of the code.
    /// </summary>
    internal static AprsDevice? FromMicELegacy(char prefix, char? last, out bool suffixUsed)
    {
        foreach (var (p, suffix, device) in _micELegacy)
        {
            if (p == prefix && (suffix is null || suffix == last))
            {
                suffixUsed = suffix is not null;
                return device;
            }
        }

        suffixUsed = false;
        return null;
    }

    private static bool Matches(string pattern, string tocall)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '*')
            {
                return true;
            }

            if (i == tocall.Length)
            {
                return false;
            }

            var ok = pattern[i] switch
            {
                '?' => true,
                'n' => char.IsAsciiDigit(tocall[i]),
                var literal => literal == tocall[i],
            };
            if (!ok)
            {
                return false;
            }
        }

        return pattern.Length == tocall.Length;
    }
}
