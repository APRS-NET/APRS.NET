using System.Globalization;
using System.Text;

namespace AprsNet.Packets;

/// <summary>
/// Writes position reports (APRS101 ch. 8 and 9), and the position body that objects and items share with them:
/// the position and symbol, one data extension, an altitude and the comment. Values are rounded to what the format
/// carries (hundredths of a minute, whole knots, PHG codes), so the packet <see cref="AprsPacket.Create"/> returns
/// shows exactly what was sent.
/// </summary>
internal static class PositionEncoder
{
    private const double CompressedLatitudeScale = 380926;
    private const double CompressedLongitudeScale = 190463;

    // Blanked for ambiguity, last first: the same offsets the decoder reads.
    private static readonly int[] _latitudeBlanks = [6, 5, 3, 2];
    private static readonly int[] _longitudeBlanks = [7, 6, 4, 3];

    public static byte[] Encode(PositionReport report)
    {
        var info = new List<byte>(64);
        if (report.Timestamp is { } timestamp)
        {
            if (timestamp.Format == AprsTimestampFormat.MonthDayHourMinute)
            {
                throw new ArgumentException(
                    "A position report timestamp is DDHHMMz, DDHHMM/ or HHMMSSh, not MMDDHHMM."
                );
            }

            info.Add(report.IsMessagingCapable ? (byte)'@' : (byte)'/');
            info.AddRange(Encoding.ASCII.GetBytes(timestamp.ToString()));
        }
        else
        {
            info.Add(report.IsMessagingCapable ? (byte)'=' : (byte)'!');
        }

        WriteBody(info, report);
        return [.. info];
    }

    /// <summary>
    /// Writes an object (APRS101 ch. 11): <c>;</c>, the name padded to 9, <c>*</c> (live) or <c>_</c> (killed), the
    /// timestamp, then the position body.
    /// </summary>
    public static byte[] Encode(ObjectReport report)
    {
        var name = report.Name;
        if (name.Length is < 1 or > 9 || name[^1] == ' ' || !IsName(name))
        {
            throw new ArgumentException(
                "An object name must be 1 to 9 printable ASCII characters without trailing spaces."
            );
        }

        if (report.Timestamp is not { } timestamp)
        {
            throw new ArgumentException("An object needs a timestamp, for example AprsTimestamp.FromDateTimeOffset.");
        }

        if (timestamp.Format == AprsTimestampFormat.MonthDayHourMinute)
        {
            throw new ArgumentException("An object timestamp is DDHHMMz, DDHHMM/ or HHMMSSh, not MMDDHHMM.");
        }

        var info = new List<byte>(64) { (byte)';' };
        info.AddRange(Encoding.Latin1.GetBytes(name.PadRight(9)));
        info.Add(report.IsLive ? (byte)'*' : (byte)'_');
        info.AddRange(Encoding.ASCII.GetBytes(timestamp.ToString()));
        WriteBody(info, report);
        return [.. info];
    }

    /// <summary>
    /// Writes an item (APRS101 ch. 11): <c>)</c>, the 3–9 character name, <c>!</c> (live) or <c>_</c> (killed), then
    /// the position body. Items have no timestamp.
    /// </summary>
    public static byte[] Encode(ItemReport report)
    {
        var name = report.Name;
        if (name.Length is < 3 or > 9 || name.AsSpan().IndexOfAny('!', '_') >= 0 || !IsName(name))
        {
            throw new ArgumentException("An item name must be 3 to 9 printable ASCII characters, without '!' or '_'.");
        }

        if (report.Timestamp is not null)
        {
            throw new ArgumentException(
                "An item has no timestamp; send an object for something that changes over time."
            );
        }

        var info = new List<byte>(64) { (byte)')' };
        info.AddRange(Encoding.Latin1.GetBytes(name));
        info.Add(report.IsLive ? (byte)'!' : (byte)'_');
        WriteBody(info, report);
        return [.. info];
    }

    private static bool IsName(string name) => name.All(c => c is >= ' ' and <= '~');

    /// <summary>The position, symbol, data extension, altitude and comment.</summary>
    public static void WriteBody(List<byte> info, PositionedPayload payload)
    {
        var position = payload.Position;
        if (position.Latitude is < -90 or > 90 || double.IsNaN(position.Latitude))
        {
            throw new ArgumentException("Latitude must be from -90 to 90 degrees.");
        }

        if (position.Longitude is < -180 or > 180 || double.IsNaN(position.Longitude))
        {
            throw new ArgumentException("Longitude must be from -180 to 180 degrees.");
        }

        if (payload.Symbol == default)
        {
            throw new ArgumentException("A position needs a symbol.");
        }

        var altitudeInComment = payload.IsCompressed
            ? WriteCompressed(info, payload)
            : WriteUncompressed(info, payload);
        var comment = CommentBytes(payload);
        if (altitudeInComment && payload.AltitudeFeet is { } feet && !HasAltitude(comment))
        {
            info.AddRange(Encoding.ASCII.GetBytes(AltitudeToken(feet)));
        }

        info.AddRange(comment);
    }

    /// <summary>
    /// The comment text a receiver will decode: the caller's comment, after an altitude token the encoder adds in
    /// front of it. Used to check the result.
    /// </summary>
    public static string ExpectedComment(PositionedPayload payload)
    {
        var altitudeInComment = !payload.IsCompressed || CompressedCarries(payload) != CompressedData.Altitude;
        return
            altitudeInComment
            && payload.AltitudeFeet is { } feet
            && payload.CommentBytes.IsEmpty
            && !payload.Comment.Contains("/A=", StringComparison.Ordinal)
            ? AltitudeToken(feet) + payload.Comment
            : payload.Comment;
    }

    private static bool WriteUncompressed(List<byte> info, PositionedPayload payload)
    {
        var position = payload.Position;
        if (position.Ambiguity is < 0 or > 4)
        {
            throw new ArgumentException("Position ambiguity must be 0 to 4.");
        }

        // A !DAO! token in the comment adds precision below a hundredth of a minute, so its position is truncated.
        var truncate = payload.Datum is not null;
        var latitude = Coordinate(position.Latitude, 2, position.Latitude < 0 ? 'S' : 'N', truncate);
        var longitude = Coordinate(position.Longitude, 3, position.Longitude < 0 ? 'W' : 'E', truncate);
        for (var i = 0; i < position.Ambiguity; i++)
        {
            latitude[_latitudeBlanks[i]] = ' ';
            longitude[_longitudeBlanks[i]] = ' ';
        }

        info.AddRange(Encoding.ASCII.GetBytes(latitude));
        info.Add((byte)payload.Symbol.Table);
        info.AddRange(Encoding.ASCII.GetBytes(longitude));
        info.Add((byte)payload.Symbol.Code);
        if (payload.ExtensionBytes.IsEmpty)
        {
            info.AddRange(Encoding.ASCII.GetBytes(Extension(payload)));
        }
        else
        {
            info.AddRange(payload.ExtensionBytes.Span); // parsed: the spelling it arrived with
        }

        return true;
    }

    /// <summary><c>DDMM.hhN</c> or <c>DDDMM.hhE</c>, rounded (or truncated) to hundredths of a minute.</summary>
    private static char[] Coordinate(double degrees, int degreeDigits, char hemisphere, bool truncate)
    {
        var hundredths = Math.Abs(degrees) * 6000;
        var total = (long)(
            truncate ? Math.Floor(hundredths + 1e-6) : Math.Round(hundredths, MidpointRounding.AwayFromZero)
        );
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{(total / 6000).ToString(new string('0', degreeDigits), CultureInfo.InvariantCulture)}{total % 6000 / 100:00}.{total % 100:00}{hemisphere}"
        );
        return text.ToCharArray();
    }

    /// <summary>The one data extension after an uncompressed position, or nothing.</summary>
    private static string Extension(PositionedPayload payload)
    {
        var courseSpeed = payload.Course is not null || payload.SpeedKnots is not null;
        var count =
            (courseSpeed ? 1 : 0)
            + (payload.PowerHeightGain is null ? 0 : 1)
            + (payload.RangeMiles is null ? 0 : 1)
            + (payload.DirectionFindingStrength is null ? 0 : 1)
            + (payload.Area is null ? 0 : 1);
        if (count > 1)
        {
            throw new ArgumentException(
                "A position carries one data extension: course and speed, PHG, range or DF signal strength."
            );
        }

        if (payload.Area is { } area)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{area.Type}{area.LatitudeCode:00}{(area.Color >= 10 ? '1' : '/')}{area.Color % 10}{area.LongitudeCode:00}"
            );
        }

        if (courseSpeed)
        {
            var course = payload.Course switch
            {
                null => "...",
                < 0 or > 360 => throw new ArgumentException("Course must be 0 to 360 degrees."),
                { } c => c.ToString("000", CultureInfo.InvariantCulture),
            };
            var speed = payload.SpeedKnots switch
            {
                null => "...",
                { } s when s < 0 || Math.Round(s) > 999 => throw new ArgumentException("Speed must be 0 to 999 knots."),
                { } s => Math.Round(s, MidpointRounding.AwayFromZero).ToString("000", CultureInfo.InvariantCulture),
            };
            var bearing = payload.DirectionFinding is { } df
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"/{df.Bearing:000}/{df.Hits}{Log2(df.RangeMiles)}{df.Quality}"
                )
                : "";
            return $"{course}/{speed}{bearing}";
        }

        if (payload.PowerHeightGain is { } phg)
        {
            if (phg.PowerWatts is < 0 or > 81)
            {
                throw new ArgumentException("PHG power must be 0 to 81 watts.");
            }

            return "PHG"
                + Phg((int)Math.Round(Math.Sqrt(phg.PowerWatts)), phg.HeightFeet, phg.GainDbi, phg.Directivity);
        }

        if (payload.RangeMiles is { } range)
        {
            return range is < 0 or > 9999.5
                ? throw new ArgumentException("Range must be 0 to 9999 miles.")
                : "RNG"
                    + Math.Round(range, MidpointRounding.AwayFromZero).ToString("0000", CultureInfo.InvariantCulture);
        }

        if (payload.DirectionFindingStrength is { } dfs)
        {
            return dfs.Strength is < 0 or > 9
                ? throw new ArgumentException("DF signal strength must be 0 to 9.")
                : "DFS" + Phg(dfs.Strength, dfs.HeightFeet, dfs.GainDbi, dfs.Directivity);
        }

        return "";
    }

    /// <summary>The four digits after PHG or DFS: first value, height code (10 × 2^h feet), gain, directivity.</summary>
    private static string Phg(int first, int heightFeet, int gainDbi, int? directivity)
    {
        if (heightFeet < 10)
        {
            throw new ArgumentException("Antenna height must be at least 10 feet (the smallest code).");
        }

        var height = (int)Math.Round(Math.Log2(heightFeet / 10.0));
        if (height > 16)
        {
            throw new ArgumentException("Antenna height is too large for a height code.");
        }

        if (gainDbi is < 0 or > 9)
        {
            throw new ArgumentException("Antenna gain must be 0 to 9 dBi.");
        }

        var direction = directivity switch
        {
            null => 0,
            { } d when d % 45 == 0 && d is >= 45 and <= 360 => d / 45,
            _ => throw new ArgumentException("Directivity must be omni (null) or 45 to 360 degrees in steps of 45."),
        };
        return string.Concat(
            ((char)('0' + first)).ToString(),
            ((char)('0' + height)).ToString(),
            ((char)('0' + gainDbi)).ToString(),
            ((char)('0' + direction)).ToString()
        );
    }

    private enum CompressedData
    {
        None,
        CourseSpeed,
        Range,
        Altitude,
    }

    /// <summary>
    /// What the compressed position's <c>cs</c> bytes carry: course and speed, else range, else altitude. An altitude
    /// that does not fit goes into the comment as <c>/A=</c>.
    /// </summary>
    private static CompressedData CompressedCarries(PositionedPayload payload) =>
        payload.Course is not null || payload.SpeedKnots is not null ? CompressedData.CourseSpeed
        : payload.RangeMiles is not null ? CompressedData.Range
        : payload.AltitudeFeet is >= 1 && !CommentHasAltitude(payload) ? CompressedData.Altitude
        : CompressedData.None;

    /// <summary>Writes a compressed position; returns whether an altitude still needs the comment.</summary>
    private static bool WriteCompressed(List<byte> info, PositionedPayload payload)
    {
        if (payload.Position.Ambiguity != 0)
        {
            throw new ArgumentException("A compressed position has no ambiguity.");
        }

        if (payload.PowerHeightGain is not null || payload.DirectionFindingStrength is not null)
        {
            throw new ArgumentException("A compressed position carries no PHG or DFS extension.");
        }

        var symbol = payload.Symbol;
        info.Add(
            symbol.Overlay is { } overlay && char.IsAsciiDigit(overlay)
                ? (byte)('a' + overlay - '0')
                : (byte)symbol.Table
        );
        Base91(info, (int)Math.Round(CompressedLatitudeScale * (90 - payload.Position.Latitude)));
        Base91(info, (int)Math.Round(CompressedLongitudeScale * (180 + payload.Position.Longitude)));
        info.Add((byte)symbol.Code);

        var carries = CompressedCarries(payload);
        if (!payload.ExtensionBytes.IsEmpty)
        {
            info.AddRange(payload.ExtensionBytes.Span); // parsed: the csT bytes it arrived with
            return carries != CompressedData.Altitude
                || payload.CompressionType?.NmeaSource != CompressedNmeaSource.Gga;
        }

        var type =
            payload.CompressionType
            ?? new CompressionType(CompressedGpsFix.Current, CompressedNmeaSource.Other, CompressedOrigin.Software);
        switch (carries)
        {
            case CompressedData.CourseSpeed:
                var course = payload.Course ?? 0;
                if (course is < 0 or > 360)
                {
                    throw new ArgumentException("Course must be 0 to 360 degrees.");
                }

                info.Add((byte)(33 + (course % 360 / 4)));
                info.Add(CompressedSpeed(Math.Log((payload.SpeedKnots ?? 0) + 1, 1.08)));
                info.Add(
                    TypeByte(
                        type with
                        {
                            NmeaSource =
                                type.NmeaSource == CompressedNmeaSource.Gga
                                    ? CompressedNmeaSource.Other
                                    : type.NmeaSource,
                        }
                    )
                );
                return true;
            case CompressedData.Range:
                if (payload.RangeMiles is < 2)
                {
                    throw new ArgumentException("A compressed position's range is at least 2 miles.");
                }

                info.Add((byte)'{');
                info.Add(CompressedSpeed(Math.Log(payload.RangeMiles!.Value / 2, 1.08)));
                info.Add(
                    TypeByte(
                        type with
                        {
                            NmeaSource =
                                type.NmeaSource == CompressedNmeaSource.Gga
                                    ? CompressedNmeaSource.Other
                                    : type.NmeaSource,
                        }
                    )
                );
                return true;
            case CompressedData.Altitude:
                var value = (int)Math.Round(Math.Log(payload.AltitudeFeet!.Value, 1.002));
                if (value >= 91 * 91)
                {
                    throw new ArgumentException("Altitude is too large for a compressed position.");
                }

                info.Add((byte)(33 + (value / 91)));
                info.Add((byte)(33 + (value % 91)));
                info.Add(TypeByte(type with { NmeaSource = CompressedNmeaSource.Gga }));
                return false;
            default:
                info.AddRange("   "u8); // no course, speed, range or altitude: c is a space and cs and T are ignored
                return true;
        }
    }

    private static byte CompressedSpeed(double code)
    {
        var rounded = (int)Math.Round(code);
        return rounded is < 0 or > 90
            ? throw new ArgumentException("Speed or range is too large for a compressed position.")
            : (byte)(33 + rounded);
    }

    private static byte TypeByte(CompressionType type) =>
        (byte)(
            33
            + (type.GpsFix == CompressedGpsFix.Current ? 0b100000 : 0)
            + ((int)type.NmeaSource << 3)
            + (int)type.Origin
        );

    private static void Base91(List<byte> info, int value)
    {
        Span<byte> digits = stackalloc byte[4];
        for (var i = 3; i >= 0; i--)
        {
            digits[i] = (byte)(33 + (value % 91));
            value /= 91;
        }

        info.AddRange(digits);
    }

    private static int Log2(int miles) =>
        miles > 0 && (miles & (miles - 1)) == 0 && miles <= 512
            ? (int)Math.Log2(miles)
            : throw new ArgumentException("A DF report's range is a power of two from 1 to 512 miles.");

    private static string AltitudeToken(double feet)
    {
        var rounded = (int)Math.Round(feet, MidpointRounding.AwayFromZero);
        return rounded switch
        {
            < -99999 or > 999999 => throw new ArgumentException("Altitude must be -99999 to 999999 feet."),
            < 0 => "/A=-" + (-rounded).ToString("00000", CultureInfo.InvariantCulture),
            _ => "/A=" + rounded.ToString("000000", CultureInfo.InvariantCulture),
        };
    }

    private static bool HasAltitude(ReadOnlySpan<byte> comment) => comment.IndexOf("/A="u8) >= 0;

    private static bool CommentHasAltitude(PositionedPayload payload) =>
        payload.CommentBytes.IsEmpty
            ? payload.Comment.Contains("/A=", StringComparison.Ordinal)
            : HasAltitude(payload.CommentBytes.Span);

    private static byte[] CommentBytes(PositionedPayload payload)
    {
        if (!payload.CommentBytes.IsEmpty)
        {
            return payload.CommentBytes.ToArray();
        }

        return payload.Comment.Any(char.IsControl)
            ? throw new ArgumentException("The comment must not contain control characters such as line breaks.")
            : Encoding.UTF8.GetBytes(payload.Comment);
    }
}
