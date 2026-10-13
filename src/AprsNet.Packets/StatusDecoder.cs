namespace AprsNet.Packets;

/// <summary>Decodes status reports (APRS101 ch. 16). A status is text, so decoding never fails.</summary>
internal static class StatusDecoder
{
    /// <summary>Decodes a status report; <paramref name="offset"/> is where the information field starts in Raw.</summary>
    public static StatusReport Decode(ReadOnlyMemory<byte> information, int offset, List<ParseIssue> issues)
    {
        var body = information[1..];
        AprsTimestamp? timestamp = null;
        string? locator = null;
        AprsSymbol? symbol = null;

        if (LooksLikeTimestamp(body.Span))
        {
            if (AprsTimestamp.TryParse(body.Span, out var parsed))
            {
                timestamp = parsed;
                body = body[7..];
            }
            else
            {
                // Timestamp-shaped but impossible (day 32, hour 25): keep it as text and say so.
                issues.Add(new ParseIssue(ParseIssueCode.InvalidTimestamp, ParseIssueSeverity.Warning, offset + 1));
            }
        }
        else if (TryLocator(body.Span, out var length) is { } found)
        {
            (locator, symbol) = found;
            body = body[length..];
        }

        var text = body.Span;
        int? heading = null;
        int? erp = null;
        if (
            text.Length >= 3
            && text[^3] == '^'
            && TryHeading(text[^2], out var degrees)
            && text[^1] is >= (byte)'1' and <= (byte)'K'
        )
        {
            heading = degrees;
            erp = 10 * (text[^1] - '0') * (text[^1] - '0');
        }

        return new StatusReport
        {
            Timestamp = timestamp,
            MaidenheadLocator = locator,
            Symbol = symbol,
            BeamHeading = heading,
            EffectiveRadiatedPowerWatts = erp,
            TextBytes = body,
            Text = AprsText.Decode(text),
        };
    }

    /// <summary>Six digits and a <c>z</c>: status reports only use the zulu day-hour-minute timestamp.</summary>
    private static bool LooksLikeTimestamp(ReadOnlySpan<byte> body) =>
        body.Length >= 7 && body[6] == 'z' && body[..6].IndexOfAnyExceptInRange((byte)'0', (byte)'9') < 0;

    /// <summary>
    /// A Maidenhead locator (two letters A–R, two digits, optionally two letters A–X) followed by a symbol table and
    /// code, then the end of the report or a space before the text.
    /// </summary>
    internal static (string Locator, AprsSymbol Symbol)? TryLocator(ReadOnlySpan<byte> body, out int length)
    {
        foreach (var size in (ReadOnlySpan<int>)[6, 4])
        {
            length = size + 2;
            if (
                body.Length >= length
                && (body.Length == length || body[length] == ' ')
                && IsLocator(body[..size])
                && AprsSymbol.TryCreate((char)body[size], (char)body[size + 1], out var symbol)
            )
            {
                return (System.Text.Encoding.ASCII.GetString(body[..size]).ToUpperInvariant(), symbol);
            }
        }

        length = 0;
        return null;
    }

    private static bool IsLocator(ReadOnlySpan<byte> locator) =>
        Field(locator[0])
        && Field(locator[1])
        && char.IsAsciiDigit((char)locator[2])
        && char.IsAsciiDigit((char)locator[3])
        && (locator.Length == 4 || (Subsquare(locator[4]) && Subsquare(locator[5])));

    private static bool Field(byte b) => (b | 0x20) is >= (byte)'a' and <= (byte)'r';

    private static bool Subsquare(byte b) => (b | 0x20) is >= (byte)'a' and <= (byte)'x';

    /// <summary>Beam heading / 10: <c>0</c>–<c>9</c> for 0–90°, <c>A</c>–<c>Z</c> for 100–350°.</summary>
    private static bool TryHeading(byte code, out int degrees)
    {
        degrees = code switch
        {
            >= (byte)'0' and <= (byte)'9' => (code - '0') * 10,
            >= (byte)'A' and <= (byte)'Z' => (code - 'A' + 10) * 10,
            _ => -1,
        };
        return degrees >= 0;
    }
}
