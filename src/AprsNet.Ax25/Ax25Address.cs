namespace AprsNet.Ax25;

/// <summary>
/// One address in an AX.25 frame: a callsign of up to six upper-case letters and digits, an SSID from 0 to 15, and
/// the flag in the top bit of the SSID byte. On the air each character is shifted left one bit and the callsign is
/// padded with spaces to six; the SSID byte carries the flag, two reserved bits, the SSID and the end-of-address bit.
/// </summary>
public readonly record struct Ax25Address
{
    /// <summary>Creates an address.</summary>
    /// <param name="callsign">One to six upper-case letters and digits.</param>
    /// <param name="ssid">0 to 15.</param>
    /// <param name="flag">The top bit of the SSID byte; see <see cref="Flag"/>.</param>
    /// <exception cref="ArgumentException">The callsign or SSID cannot be sent in AX.25.</exception>
    public Ax25Address(string callsign, int ssid, bool flag)
    {
        ArgumentNullException.ThrowIfNull(callsign);
        if (!IsCallsign(callsign))
        {
            throw new ArgumentException(
                $"'{callsign}' is not an AX.25 callsign: one to six upper-case letters and digits.",
                nameof(callsign)
            );
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ssid);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ssid, 15);
        _callsign = callsign;
        Ssid = ssid;
        Flag = flag;
    }

    private readonly string? _callsign;

    /// <summary>The callsign without padding; empty for <see langword="default"/>.</summary>
    public string Callsign => _callsign ?? "";

    /// <summary>The SSID, 0 to 15.</summary>
    public int Ssid { get; }

    /// <summary>
    /// For a digipeater, the has-been-repeated (H) bit: this digipeater has passed the frame on. For the destination
    /// and source, AX.25's command/response bit, which APRS does not use.
    /// </summary>
    public bool Flag { get; }

    /// <summary>The address as TNC2 text writes it: <c>N0CALL</c>, or <c>N0CALL-9</c> with an SSID.</summary>
    public override string ToString() => Ssid == 0 ? Callsign : $"{Callsign}-{Ssid}";

    /// <summary>
    /// Reads <c>CALL</c> or <c>CALL-SSID</c>, as TNC2 text and APRS.NET's <c>Callsign</c> write them. Returns
    /// <see langword="false"/> for anything AX.25 cannot carry: longer or lower-case callsigns, other characters, or an
    /// SSID above 15.
    /// </summary>
    public static bool TryParse(string? text, out Ax25Address address)
    {
        address = default;
        if (text is null)
        {
            return false;
        }

        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var callsign = dash < 0 ? text : text[..dash];
        var ssid = 0;
        if (
            !IsCallsign(callsign)
            || (
                dash >= 0
                && (
                    text.Length - dash - 1 is < 1 or > 2
                    || !int.TryParse(
                        text.AsSpan(dash + 1),
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out ssid
                    )
                    || ssid > 15
                )
            )
        )
        {
            return false;
        }

        address = new Ax25Address(callsign, ssid, flag: false);
        return true;
    }

    /// <summary>Writes the seven address bytes; <paramref name="last"/> sets the end-of-address bit.</summary>
    internal void WriteTo(Span<byte> field, bool last)
    {
        for (var i = 0; i < 6; i++)
        {
            field[i] = (byte)((i < Callsign.Length ? Callsign[i] : ' ') << 1);
        }

        field[6] = (byte)((Flag ? 0x80 : 0) | 0x60 | (Ssid << 1) | (last ? 1 : 0));
    }

    /// <summary>
    /// Reads seven address bytes. Returns <see langword="false"/> for a callsign AX.25 does not allow, or one with a
    /// character after its padding.
    /// </summary>
    internal static bool TryRead(ReadOnlySpan<byte> field, out Ax25Address address, out bool last)
    {
        address = default;
        last = (field[6] & 1) != 0;
        Span<char> chars = stackalloc char[6];
        var length = 0;
        for (var i = 0; i < 6; i++)
        {
            if ((field[i] & 1) != 0)
            {
                return false; // the end-of-address bit belongs to the SSID byte only
            }

            var c = (char)(field[i] >> 1);
            if (c == ' ')
            {
                continue;
            }

            if (length != i || !(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)))
            {
                return false;
            }

            chars[length++] = c;
        }

        if (length == 0)
        {
            return false;
        }

        address = new Ax25Address(new string(chars[..length]), (field[6] >> 1) & 0x0F, (field[6] & 0x80) != 0);
        return true;
    }

    private static bool IsCallsign(string callsign) =>
        callsign.Length is >= 1 and <= 6 && callsign.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c));
}
