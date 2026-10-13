namespace AprsNet.Packets;

/// <summary>
/// Decodes position reports (APRS101 ch. 8 and 9, APRS 1.2 <c>!DAO!</c>). Parsing helpers here are shared by the
/// payloads that carry a position.
/// </summary>
internal static class PositionDecoder
{
    // Positions within the 8-byte latitude and 9-byte longitude that may be blanked for ambiguity, last first.
    private static readonly int[] _latitudeBlanks = [6, 5, 3, 2];
    private static readonly int[] _longitudeBlanks = [7, 6, 4, 3];

    /// <summary>Decodes a position report; <paramref name="offset"/> is where the information field starts in Raw.</summary>
    public static PositionReport? Decode(ReadOnlyMemory<byte> information, int offset, List<ParseIssue> issues)
    {
        var info = information.Span;
        var at = 1;
        var report = new PositionReport { IsMessagingCapable = info[0] is (byte)'=' or (byte)'@' };
        var badTimestamp = false;
        if (info[0] is (byte)'/' or (byte)'@')
        {
            badTimestamp = !AprsTimestamp.TryParse(info[1..], out var parsed);
            report.Timestamp = badTimestamp ? null : parsed;
            at = 8;
        }

        var decoded = TryDecodePosition(information, at, offset, issues, report);
        if (badTimestamp)
        {
            // A broken timestamp in front of a good position (seen from some firmware) only loses the time.
            var severity = decoded ? ParseIssueSeverity.Warning : ParseIssueSeverity.Error;
            issues.Insert(0, new ParseIssue(ParseIssueCode.InvalidTimestamp, severity, offset + 1));
        }

        return decoded ? report : null;
    }

    /// <summary>
    /// Decodes the position, symbol, data extension and comment that start at <paramref name="at"/> into
    /// <paramref name="target"/>. Returns <see langword="false"/>, with an error issue, when there is no valid position.
    /// </summary>
    internal static bool TryDecodePosition(
        ReadOnlyMemory<byte> information,
        int at,
        int offset,
        List<ParseIssue> issues,
        PositionedPayload target
    )
    {
        var info = information.Span;
        var decoded =
            info.Length > at && (char.IsAsciiDigit((char)info[at]) || info[at] == ' ')
                ? DecodeUncompressed(information, at, offset, issues, target)
                : DecodeCompressed(information, at, offset, issues, target);
        if (decoded && target.Symbol.WithoutOverlay == new AprsSymbol('\\', 'm'))
        {
            target.Signpost = Braces(target.CommentBytes.Span, digitsOnly: false);
        }

        return decoded;
    }

    private static bool DecodeUncompressed(
        ReadOnlyMemory<byte> information,
        int at,
        int offset,
        List<ParseIssue> issues,
        PositionedPayload target
    )
    {
        var info = information.Span;
        if (info.Length < at + 19)
        {
            issues.Add(Error(ParseIssueCode.PositionTooShort, offset + at));
            return false;
        }

        if (!TryLatitude(info.Slice(at, 8), out var latitude, out var ambiguity, out var lowerLatitude))
        {
            issues.Add(Error(ParseIssueCode.InvalidLatitude, offset + at));
            return false;
        }

        if (!TryLongitude(info.Slice(at + 9, 9), ambiguity, out var longitude, out var lowerLongitude))
        {
            issues.Add(Error(ParseIssueCode.InvalidLongitude, offset + at + 9));
            return false;
        }

        if (lowerLatitude || lowerLongitude)
        {
            issues.Add(
                new ParseIssue(
                    ParseIssueCode.LowercaseHemisphere,
                    ParseIssueSeverity.Warning,
                    offset + at + (lowerLatitude ? 7 : 17)
                )
            );
        }

        if (!TrySymbol(info[at + 8], info[at + 18], offset + at + 8, issues, out var symbol))
        {
            return false;
        }

        // Data extension: course/speed (with a DF bearing report on the DF symbol), PHG, RNG or DFS.
        var rest = info[(at + 19)..];
        double? speed = null;
        PowerHeightGain? phg = null;
        double? range = null;
        DirectionFindingStrength? dfs = null;
        DirectionFindingReport? df = null;
        AreaShape? area = null;
        int? course = null;
        var used = 0;
        if (symbol.WithoutOverlay == new AprsSymbol('\\', 'l') && TryArea(rest, out area))
        {
            used = 7;
        }
        else if (TryCourseSpeed(rest, out course, out var speedKnots))
        {
            speed = speedKnots;
            used = 7;
            if (symbol == new AprsSymbol('/', '\\') && TryBearing(rest[7..], course == 0, out df))
            {
                used = 15;
            }
        }
        else if (TryPhg(rest, "PHG"u8, out var p, out var h, out var g, out var d))
        {
            phg = new PowerHeightGain(p * p, h, g, d);
            used = 7;
        }
        else if (rest.Length >= 7 && rest.StartsWith("RNG"u8) && TryDigits(rest.Slice(3, 4), out var miles))
        {
            range = miles;
            used = 7;
        }
        else if (TryPhg(rest, "DFS"u8, out var s, out h, out g, out d))
        {
            dfs = new DirectionFindingStrength(s, h, g, d);
            used = 7;
        }

        var commentStart = at + 19 + used;
        var comment = information[commentStart..];
        var (position, datum, text) = ApplyDao(new AprsPosition(latitude, longitude, ambiguity), comment.Span);
        if (
            area is not null
            && area.Kind == AreaShapeKind.Line
            && Braces(comment.Span, digitsOnly: true) is { } corridor
        )
        {
            area = area with { CorridorMiles = int.Parse(corridor, System.Globalization.CultureInfo.InvariantCulture) };
        }

        target.Position = position;
        target.Symbol = symbol;
        target.Course = course;
        target.SpeedKnots = speed;
        target.AltitudeFeet = CommentAltitude(comment.Span);
        target.PowerHeightGain = phg;
        target.RangeMiles = range;
        target.DirectionFindingStrength = dfs;
        target.DirectionFinding = df;
        target.Area = area;
        target.Datum = datum;
        target.CommentBytes = comment;
        target.Comment = text;
        return true;
    }

    private static bool DecodeCompressed(
        ReadOnlyMemory<byte> information,
        int at,
        int offset,
        List<ParseIssue> issues,
        PositionedPayload target
    )
    {
        var info = information.Span;
        if (info.Length < at + 13)
        {
            issues.Add(Error(ParseIssueCode.PositionTooShort, offset + at));
            return false;
        }

        var table = info[at] is >= (byte)'a' and <= (byte)'j' ? (byte)('0' + info[at] - 'a') : info[at];
        if (!TryBase91(info.Slice(at + 1, 4), out var y) || y > 380926 * 180)
        {
            issues.Add(Error(ParseIssueCode.InvalidLatitude, offset + at + 1));
            return false;
        }

        if (!TryBase91(info.Slice(at + 5, 4), out var x) || x > 190463 * 360)
        {
            issues.Add(Error(ParseIssueCode.InvalidLongitude, offset + at + 5));
            return false;
        }

        if (!TrySymbol(table, info[at + 9], offset + at, issues, out var symbol))
        {
            return false;
        }

        int? course = null;
        double? speed = null;
        double? range = null;
        double? altitude = null;
        CompressionType? type = null;
        var (c, s, t) = (info[at + 10], info[at + 11], info[at + 12]);
        if (c != ' ' && t is >= 33 and <= 123 && s is >= 33 and <= 123)
        {
            var bits = t - 33;
            type = new CompressionType(
                (bits & 0b100000) != 0 ? CompressedGpsFix.Current : CompressedGpsFix.Old,
                (CompressedNmeaSource)((bits >> 3) & 0b11),
                (CompressedOrigin)(bits & 0b111)
            );
            if (type.Value.NmeaSource == CompressedNmeaSource.Gga && c is >= 33 and <= 123)
            {
                altitude = Math.Pow(1.002, ((c - 33) * 91) + (s - 33));
            }
            else if (c == '{')
            {
                range = 2 * Math.Pow(1.08, s - 33);
            }
            else if (c is >= 33 and <= (byte)'z')
            {
                course = (c - 33) * 4;
                speed = Math.Pow(1.08, s - 33) - 1;
            }
        }

        var comment = information[(at + 13)..];
        target.Position = new AprsPosition(90 - (y / 380926.0), -180 + (x / 190463.0));
        target.Symbol = symbol;
        target.IsCompressed = true;
        target.Course = course;
        target.SpeedKnots = speed;
        target.RangeMiles = range;
        target.AltitudeFeet = altitude ?? CommentAltitude(comment.Span);
        target.CompressionType = type;
        target.CommentBytes = comment;
        target.Comment = Text(comment.Span);
        return true;
    }

    /// <summary>Latitude <c>DDMM.hhN</c>, where trailing digits may be spaces for ambiguity.</summary>
    private static bool TryLatitude(ReadOnlySpan<byte> field, out double degrees, out int ambiguity, out bool lowercase)
    {
        degrees = 0;
        ambiguity = 0;
        lowercase = field[7] is (byte)'n' or (byte)'s';
        while (ambiguity < 4 && field[_latitudeBlanks[ambiguity]] == ' ')
        {
            ambiguity++;
        }

        Span<byte> digits = stackalloc byte[8];
        field.CopyTo(digits);
        for (var i = 0; i < ambiguity; i++)
        {
            digits[_latitudeBlanks[i]] = (byte)'0';
        }

        if (
            digits[4] != '.'
            || field[7] is not ((byte)'N' or (byte)'S' or (byte)'n' or (byte)'s')
            || !TryDigits(digits[..4], out var degreesMinutes)
            || !TryDigits(digits.Slice(5, 2), out var hundredths)
        )
        {
            return false;
        }

        var wholeDegrees = degreesMinutes / 100; // DDMM: integer division keeps the degrees
        var minutes = (degreesMinutes % 100) + (hundredths / 100.0);
        degrees = wholeDegrees + (minutes / 60);
        if (minutes >= 60 || degrees > 90)
        {
            return false;
        }

        degrees = field[7] is (byte)'S' or (byte)'s' ? -degrees : degrees;
        return true;
    }

    /// <summary>Longitude <c>DDDMM.hhW</c>; the latitude's ambiguity blanks the same number of trailing digits.</summary>
    private static bool TryLongitude(ReadOnlySpan<byte> field, int ambiguity, out double degrees, out bool lowercase)
    {
        degrees = 0;
        lowercase = field[8] is (byte)'e' or (byte)'w';
        Span<byte> digits = stackalloc byte[9];
        field.CopyTo(digits);
        for (var i = 0; i < ambiguity; i++)
        {
            digits[_longitudeBlanks[i]] = (byte)'0';
        }

        if (
            digits[5] != '.'
            || field[8] is not ((byte)'E' or (byte)'W' or (byte)'e' or (byte)'w')
            || !TryDigits(digits[..5], out var degreesMinutes)
            || !TryDigits(digits.Slice(6, 2), out var hundredths)
        )
        {
            return false;
        }

        var wholeDegrees = degreesMinutes / 100; // DDMM: integer division keeps the degrees
        var minutes = (degreesMinutes % 100) + (hundredths / 100.0);
        degrees = wholeDegrees + (minutes / 60);
        if (minutes >= 60 || degrees > 180)
        {
            return false;
        }

        degrees = field[8] is (byte)'W' or (byte)'w' ? -degrees : degrees;
        return true;
    }

    private static bool TrySymbol(byte table, byte code, int offset, List<ParseIssue> issues, out AprsSymbol symbol)
    {
        if (AprsSymbol.TryCreate((char)table, (char)code, out symbol))
        {
            return true;
        }

        var tableValid = AprsSymbol.TryCreate((char)table, '!', out _);
        issues.Add(Error(tableValid ? ParseIssueCode.InvalidSymbolCode : ParseIssueCode.InvalidSymbolTable, offset));
        return false;
    }

    /// <summary>Course and speed <c>CCC/SSS</c>; <c>...</c> or spaces mean not known.</summary>
    private static bool TryCourseSpeed(ReadOnlySpan<byte> rest, out int? course, out double? speed)
    {
        course = null;
        speed = null;
        if (
            rest.Length < 7
            || rest[3] != '/'
            || !TryField(rest[..3], out var c)
            || !TryField(rest.Slice(4, 3), out var s)
        )
        {
            return false;
        }

        if (c > 360)
        {
            return false;
        }

        (course, speed) = (c, s);
        return true;

        static bool TryField(ReadOnlySpan<byte> field, out int? value)
        {
            value = null;
            if (field.SequenceEqual("..."u8) || field.SequenceEqual("   "u8))
            {
                return true;
            }

            var ok = TryDigits(field, out var number);
            value = number;
            return ok;
        }
    }

    /// <summary>
    /// An area extension <c>Tyy/Cxx</c>: type digit, two-digit latitude code, <c>/</c> (or <c>1</c> when the colour is
    /// 10–15), colour digit, two-digit longitude code (APRS101 ch. 11).
    /// </summary>
    private static bool TryArea(ReadOnlySpan<byte> rest, out AreaShape? area)
    {
        area = null;
        if (
            rest.Length < 7
            || !char.IsAsciiDigit((char)rest[0])
            || !TryDigits(rest.Slice(1, 2), out var yy)
            || rest[3] is not ((byte)'/' or (byte)'1')
            || !char.IsAsciiDigit((char)rest[4])
            || !TryDigits(rest.Slice(5, 2), out var xx)
        )
        {
            return false;
        }

        var color = (rest[3] == '1' ? 10 : 0) + (rest[4] - '0');
        if (color > 15)
        {
            return false;
        }

        area = new AreaShape(rest[0] - '0', color, yy, xx, null);
        return true;
    }

    /// <summary>The 1–3 characters in the first <c>{…}</c> of a comment (a signpost or a corridor width).</summary>
    private static string? Braces(ReadOnlySpan<byte> comment, bool digitsOnly)
    {
        var open = comment.IndexOf((byte)'{');
        if (open < 0)
        {
            return null;
        }

        var close = comment[(open + 1)..].IndexOf((byte)'}');
        if (close is < 1 or > 3)
        {
            return null;
        }

        var inside = comment.Slice(open + 1, close);
        return digitsOnly && !TryDigits(inside, out _) ? null : AprsText.Decode(inside);
    }

    /// <summary>A DF bearing report <c>/BRG/NRQ</c> after course and speed.</summary>
    private static bool TryBearing(ReadOnlySpan<byte> rest, bool fixedStation, out DirectionFindingReport? report)
    {
        report = null;
        if (
            rest.Length < 8
            || rest[0] != '/'
            || rest[4] != '/'
            || !TryDigits(rest.Slice(1, 3), out var bearing)
            || !TryDigits(rest.Slice(5, 3), out _)
        )
        {
            return false;
        }

        report = new DirectionFindingReport(bearing, rest[5] - '0', 1 << (rest[6] - '0'), rest[7] - '0', fixedStation);
        return true;
    }

    /// <summary><c>PHGphgd</c> and <c>DFSshgd</c>: a digit, a height code (0 and up, 10 × 2^h feet), gain and direction.</summary>
    private static bool TryPhg(
        ReadOnlySpan<byte> rest,
        ReadOnlySpan<byte> prefix,
        out int first,
        out int heightFeet,
        out int gain,
        out int? directivity
    )
    {
        (first, heightFeet, gain, directivity) = (0, 0, 0, null);
        if (
            rest.Length < 7
            || !rest.StartsWith(prefix)
            || !char.IsAsciiDigit((char)rest[3])
            || rest[4] is < (byte)'0' or > (byte)'@'
            || !char.IsAsciiDigit((char)rest[5])
            || rest[6] is < (byte)'0' or > (byte)'8'
        )
        {
            return false;
        }

        first = rest[3] - '0';
        heightFeet = 10 << (rest[4] - '0');
        gain = rest[5] - '0';
        directivity = rest[6] == '0' ? null : (rest[6] - '0') * 45;
        return true;
    }

    /// <summary>Altitude <c>/A=aaaaaa</c> in feet, anywhere in a comment.</summary>
    private static double? CommentAltitude(ReadOnlySpan<byte> comment)
    {
        var at = comment.IndexOf("/A="u8);
        if (at < 0 || comment.Length < at + 9)
        {
            return null;
        }

        var field = comment.Slice(at + 3, 6);
        return field[0] == '-' && TryDigits(field[1..], out var below) ? -below
            : TryDigits(field, out var feet) ? feet
            : null;
    }

    /// <summary>
    /// Applies a <c>!DAO!</c> token (APRS 1.2) to an unambiguous position and returns the comment text without it.
    /// </summary>
    private static (AprsPosition Position, char? Datum, string Comment) ApplyDao(
        AprsPosition position,
        ReadOnlySpan<byte> comment
    )
    {
        for (var at = comment.Length - 5; at >= 0; at--)
        {
            var token = comment.Slice(at, 5);
            if (token[0] != '!' || token[4] != '!' || !char.IsAsciiLetterOrDigit((char)token[1]))
            {
                continue;
            }

            double? extraLatitude;
            double? extraLongitude;
            if (char.IsAsciiLetterLower((char)token[1]))
            {
                extraLatitude = Base91Minutes(token[2]);
                extraLongitude = Base91Minutes(token[3]);
            }
            else
            {
                extraLatitude = DigitMinutes(token[2]);
                extraLongitude = DigitMinutes(token[3]);
            }

            if (extraLatitude is null || extraLongitude is null)
            {
                continue;
            }

            if (position.Ambiguity == 0)
            {
                position = position with
                {
                    Latitude = position.Latitude + (Math.Sign(position.Latitude) * extraLatitude.Value / 60),
                    Longitude = position.Longitude + (Math.Sign(position.Longitude) * extraLongitude.Value / 60),
                };
            }

            var text = Text(comment[..at]) + Text(comment[(at + 5)..]);
            return (position, (char)token[1], text);
        }

        return (position, null, Text(comment));

        static double? DigitMinutes(byte b) =>
            b == ' ' ? 0
            : char.IsAsciiDigit((char)b) ? (b - '0') * 0.001
            : null;

        static double? Base91Minutes(byte b) =>
            b == ' ' ? 0
            : b is >= 33 and <= 123 ? (b - 33) / 91.0 * 0.01
            : null;
    }

    private static string Text(ReadOnlySpan<byte> bytes) => AprsText.Decode(bytes);

    private static bool TryBase91(ReadOnlySpan<byte> bytes, out int value)
    {
        value = 0;
        foreach (var b in bytes)
        {
            if (b is < 33 or > 123)
            {
                return false;
            }

            value = (value * 91) + (b - 33);
        }

        return true;
    }

    private static bool TryDigits(ReadOnlySpan<byte> bytes, out int value)
    {
        value = 0;
        foreach (var b in bytes)
        {
            if (!char.IsAsciiDigit((char)b))
            {
                return false;
            }

            value = (value * 10) + (b - '0');
        }

        return bytes.Length > 0;
    }

    private static ParseIssue Error(ParseIssueCode code, int offset) => new(code, ParseIssueSeverity.Error, offset);
}
