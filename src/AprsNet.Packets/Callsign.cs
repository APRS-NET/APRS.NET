using System.Globalization;

namespace AprsNet.Packets;

/// <summary>
/// A station identifier exactly as it appeared in a packet header. APRS-IS carries identifiers that are not valid
/// AX.25 callsigns (server names such as <c>T2PERTH</c>, SSIDs such as <c>-DS</c> or <c>-002</c>, lower-case
/// q-constructs), so the value is kept as received and never normalized; <see cref="IsAx25"/> says whether it would fit
/// an AX.25 address.
/// </summary>
public sealed record Callsign
{
    internal Callsign(string value) => Value = value;

    /// <summary>The identifier as received, for example <c>N0CALL-9</c>.</summary>
    public string Value { get; }

    /// <summary>The part before the first <c>-</c>, for example <c>N0CALL</c>.</summary>
    public string Base
    {
        get
        {
            var dash = Value.IndexOf('-', StringComparison.Ordinal);
            return dash < 0 ? Value : Value[..dash];
        }
    }

    /// <summary>The part after the first <c>-</c>, for example <c>9</c>, or empty when there is none.</summary>
    public string Ssid
    {
        get
        {
            var dash = Value.IndexOf('-', StringComparison.Ordinal);
            return dash < 0 ? "" : Value[(dash + 1)..];
        }
    }

    /// <summary>
    /// Whether the identifier fits an AX.25 address: one to six upper-case letters or digits, and either no SSID or a
    /// number from 0 to 15 without a leading zero.
    /// </summary>
    public bool IsAx25
    {
        get
        {
            var @base = Base;
            if (@base.Length is < 1 or > 6 || !@base.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c)))
            {
                return false;
            }

            var dash = Value.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0)
            {
                return true;
            }

            var ssid = Ssid;
            return ssid.Length is 1 or 2
                && ssid.All(char.IsAsciiDigit)
                && (ssid.Length == 1 || ssid[0] != '0')
                && int.Parse(ssid, CultureInfo.InvariantCulture) <= 15;
        }
    }

    /// <summary>Returns <see cref="Value"/>.</summary>
    public override string ToString() => Value;
}
