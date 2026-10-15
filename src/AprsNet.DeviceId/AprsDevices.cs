using AprsNet.Packets;

namespace AprsNet.DeviceId;

/// <summary>
/// Identifies the device or program that sent a packet, from the aprs-deviceid list
/// (<see href="https://github.com/aprsorg/aprs-deviceid"/>): by its destination (TOCALL), or for Mic-E by the codes
/// around its comment.
/// </summary>
/// <remarks>
/// The list is built into this package (see <see cref="ListCommit"/> and <see cref="ListDate"/>), so lookups need no
/// file or network access. Its data is licensed under CC BY-SA 2.0 by its maintainers, Heikki Hannikainen, OH7LZB, and
/// contributors; see the package's LICENSE.md. In TOCALL patterns <c>?</c> stands for any character, <c>n</c> for a
/// digit and a final <c>*</c> for any characters; when several match, the one with the most literal characters wins.
/// </remarks>
public static partial class AprsDevices
{
    /// <summary>The aprs-deviceid commit the built-in list was taken from.</summary>
    public static string ListCommit => _listCommit;

    /// <summary>The date of the aprs-deviceid commit the built-in list was taken from.</summary>
    public static DateOnly ListDate => _listDate;

    /// <summary>
    /// The device that sent <paramref name="packet"/>: for Mic-E, the one its device codes name (the destination holds
    /// the latitude there); otherwise the one its destination names.
    /// </summary>
    /// <remarks>
    /// Mic-E devices are read from the raw information field: a legacy Kenwood code is the type byte <c>&gt;</c> or
    /// <c>]</c> and, for some models, a final character; a newer code is the type byte <c>`</c> or <c>'</c> and the
    /// final two characters. <see cref="MicEReport.Device"/> knows only the codes in the APRS 1.2 specification.
    /// </remarks>
    public static AprsDevice? Identify(AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return packet.Type == PacketType.MicE ? FromMicE(packet.Information.Span)
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

    // A Mic-E information field: data type, 6 bytes of longitude, speed and course, the symbol and its table, then the
    // optional type byte and the status text, with any device code at its end.
    private static AprsDevice? FromMicE(ReadOnlySpan<byte> info)
    {
        if (info.Length < 10)
        {
            return null;
        }

        var text = info[10..];
        switch ((char)info[9])
        {
            case '>' or ']':
                var last = text.IsEmpty ? (char?)null : (char)text[^1];
                foreach (var (prefix, suffix, device) in _micELegacy)
                {
                    if (prefix == info[9] && (suffix is null || suffix == last))
                    {
                        return device;
                    }
                }

                return null;
            case '`' or '\'' when text.Length >= 2:
                return _micE.GetValueOrDefault(System.Text.Encoding.Latin1.GetString(text[^2..]));
            default:
                return null;
        }
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
