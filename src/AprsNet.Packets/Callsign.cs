using System.Diagnostics.CodeAnalysis;
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

    /// <summary>
    /// Creates a callsign for building a packet: 1 to 9 printable ASCII characters, without the header's separators
    /// (<c>&gt;</c>, <c>:</c>, <c>,</c>, <c>*</c>) or spaces. Like parsed callsigns it is not required to fit AX.25;
    /// check <see cref="IsAx25"/> for packets that go on the air.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a usable header entry.</exception>
    /// <exception cref="AprsValidationException">
    /// <paramref name="value"/> is not a usable header entry: <see cref="AprsValidationErrorCode.CallsignInvalidLength"/>
    /// or <see cref="AprsValidationErrorCode.CallsignInvalidCharacter"/>.
    /// </exception>
    public static Callsign Parse(string value) =>
        Check(value, nameof(value), pathEntry: false) is { } error ? throw error : new Callsign(value);

    /// <summary>Creates a callsign as <see cref="Parse"/> does, or returns <see langword="false"/>.</summary>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out Callsign? callsign)
    {
        callsign = Check(value, nameof(value), pathEntry: false) is null ? new Callsign(value!) : null;
        return callsign is not null;
    }

    /// <summary>
    /// Why <paramref name="value"/> is not a usable header entry (1 to 9 printable ASCII characters without spaces,
    /// <c>&gt;</c>, <c>:</c>, <c>,</c> or <c>*</c>), or <see langword="null"/> when it is.
    /// </summary>
    internal static AprsValidationException? Check(string? value, string paramName, bool pathEntry)
    {
        const string message =
            "A callsign must be 1 to 9 printable ASCII characters without spaces, '>', ':', ',' or '*'.";
        if (value is not { Length: >= 1 and <= 9 })
        {
            return new AprsValidationException(
                pathEntry
                    ? AprsValidationErrorCode.PathEntryInvalidLength
                    : AprsValidationErrorCode.CallsignInvalidLength,
                message,
                paramName,
                value
            );
        }

        var bad = value.AsSpan().IndexOfAnyExcept(_headerCharacters);
        return bad < 0
            ? null
            : new AprsValidationException(
                pathEntry
                    ? AprsValidationErrorCode.PathEntryInvalidCharacter
                    : AprsValidationErrorCode.CallsignInvalidCharacter,
                message,
                paramName,
                value,
                bad
            );
    }

    private static readonly System.Buffers.SearchValues<char> _headerCharacters = System.Buffers.SearchValues.Create(
        "!\"#$%&'()+-./0123456789;<=?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~"
    );

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
