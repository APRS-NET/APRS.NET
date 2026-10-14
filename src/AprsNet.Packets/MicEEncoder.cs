using System.Text;

namespace AprsNet.Packets;

/// <summary>
/// Writes Mic-E reports (APRS101 ch. 10), the reverse of <see cref="MicEDecoder"/>: the latitude, message and longitude
/// flags go in the destination, the longitude, speed, course and symbol in the first nine bytes of the information
/// field, then a type byte, an optional <c>xxx}</c> altitude and the status text.
/// </summary>
internal static class MicEEncoder
{
    /// <summary>Speeds above this decode by the APRS 1.2 rule for fast objects, so they cannot be sent as they are.</summary>
    private const int MaxSpeedKnots = 670;

    /// <summary>The <c>xxx}</c> altitude is base-91 metres above −10 000 m.</summary>
    private const int AltitudeOffsetMeters = 10000;

    /// <summary>
    /// The destination: six characters of latitude digits whose flags carry the message bits, north, the +100°
    /// longitude offset and west, then the SSID of <paramref name="destination"/>, which Mic-E uses as a digipeater
    /// path code.
    /// </summary>
    public static Callsign Destination(MicEReport report, Callsign destination)
    {
        var (custom, bits) = MessageBits(report.Message);
        var latitude = Digits(report.Position.Latitude, 2, report.Datum is not null);
        var (degrees, _, _) = Longitude(report);
        // 0-9° and 100-109° each have two spellings; a parsed report keeps the flag that matches its longitude byte.
        var offset = report.ExtensionBytes.IsEmpty
            ? degrees is < 10 or >= 100
            : Degrees(report.ExtensionBytes.Span[1], offset: false) != degrees;
        var ambiguity = Ambiguity(report);
        Span<char> chars = stackalloc char[6];
        for (var i = 0; i < 6; i++)
        {
            var set = i switch
            {
                < 3 => (bits >> (2 - i) & 1) == 1,
                3 => report.Position.Latitude >= 0,
                4 => offset,
                _ => report.Position.Longitude < 0,
            };
            var high =
                set && i < 3 && custom ? 'A'
                : set ? 'P'
                : '0';
            chars[i] =
                i >= 6 - ambiguity
                    ? (
                        high switch
                        {
                            'A' => 'K',
                            'P' => 'Z',
                            _ => 'L',
                        }
                    )
                    : (char)(high + latitude[i]);
        }

        var ssid = destination.Ssid;
        var value = new string(chars);
        return new Callsign(ssid.Length == 0 ? value : $"{value}-{ssid}");
    }

    public static byte[] Encode(MicEReport report)
    {
        Check(report);
        var info = new List<byte>(64);
        if (report.ExtensionBytes.IsEmpty)
        {
            info.Add(report.IsCurrentFix ? (byte)'`' : (byte)'\'');
            var (degrees, minutes, hundredths) = Longitude(report);
            info.Add(
                (byte)(
                    degrees switch
                    {
                        < 10 => degrees + 118,
                        < 100 => degrees + 28,
                        < 110 => degrees + 8,
                        _ => degrees - 72,
                    }
                )
            );
            info.Add((byte)(minutes < 10 ? minutes + 88 : minutes + 28));
            info.Add((byte)(hundredths + 28));
            SpeedCourse(info, report);
        }
        else
        {
            info.AddRange(report.ExtensionBytes.Span); // parsed: the type, longitude, speed and course bytes as sent
        }

        info.Add((byte)report.Symbol.Code);
        info.Add((byte)report.Symbol.Table);
        if (!report.ExtensionBytes.IsEmpty)
        {
            info.AddRange(report.CommentBytes.Span); // parsed: type byte, altitude, status and device code as sent
            return [.. info];
        }

        if (report.Comment.Any(char.IsControl))
        {
            throw new ArgumentException("The comment must not contain control characters such as line breaks.");
        }

        // ` says the station can receive messages; a space (original Mic-E) says it cannot. Both are followed by no
        // device code, but after ` a receiver still reads the last two bytes as one, which Mismatch reports.
        info.Add(report.IsMessagingCapable ? (byte)'`' : (byte)' ');
        if (report.AltitudeFeet is { } feet)
        {
            var meters = (int)Math.Round((feet * 0.3048) + AltitudeOffsetMeters, MidpointRounding.AwayFromZero);
            if (meters is < 0 or >= 91 * 91 * 91)
            {
                throw new ArgumentException("A Mic-E altitude must be from -32,808 to 2,439,600 feet.");
            }

            info.Add((byte)(33 + (meters / (91 * 91))));
            info.Add((byte)(33 + (meters / 91 % 91)));
            info.Add((byte)(33 + (meters % 91)));
            info.Add((byte)'}');
        }

        info.AddRange(Encoding.UTF8.GetBytes(report.Comment));
        return [.. info];
    }

    /// <summary>
    /// Compares what a receiver decodes with what was given: the position within the format's precision, and the
    /// symbol, message, flags, course, speed, altitude and status text exactly.
    /// </summary>
    public static string? Mismatch(MicEReport e, MicEReport a)
    {
        // A parsed report is rebuilt from its own bytes, so its values must come back exactly, unknown ones included.
        var parsed = !e.ExtensionBytes.IsEmpty;
        var courseSpeedDiffer = parsed
            ? e.Course != a.Course || e.SpeedKnots != a.SpeedKnots
            : (e.Course ?? 0) != a.Course
                || Math.Round(e.SpeedKnots ?? 0, MidpointRounding.AwayFromZero) != a.SpeedKnots;
        return e.Message != a.Message ? "the message does not survive encoding"
            : e.IsCurrentFix != a.IsCurrentFix ? "the current-fix flag does not survive encoding"
            : e.IsMessagingCapable != a.IsMessagingCapable ? "the messaging flag does not survive encoding"
            : e.Symbol != a.Symbol ? "the symbol does not survive encoding"
            : e.Position.Ambiguity != a.Position.Ambiguity ? "the ambiguity does not survive encoding"
            : !Near(e.Position.Latitude, a.Position.Latitude, e) || !Near(e.Position.Longitude, a.Position.Longitude, e)
                ? "the position does not survive encoding"
            : courseSpeedDiffer ? "the course and speed do not survive encoding"
            : (e.AltitudeFeet is null) != (a.AltitudeFeet is null)
            || Math.Abs((e.AltitudeFeet ?? 0) - (a.AltitudeFeet ?? 0)) > 0.5 / 0.3048 + 1e-9
                ? "the altitude does not survive encoding"
            : e.Device != a.Device
                ? parsed ? "the device does not survive encoding"
                    : $"the end of the comment would be read as the {a.Device} device code"
            : e.Comment != a.Comment ? "the comment does not survive encoding"
            : null;
    }

    private static bool Near(double expected, double actual, MicEReport e)
    {
        var step = e.Position.Ambiguity switch
        {
            0 => 0.5 / 6000,
            1 => 0.1 / 60,
            2 => 1.0 / 60,
            3 => 10.0 / 60,
            _ => 1.0,
        };
        var datumSlack = e.Datum is null ? 0 : 0.01 / 60;
        return Math.Abs(expected - actual) <= step + datumSlack + 1e-9;
    }

    private static void Check(MicEReport report)
    {
        var position = report.Position;
        if (position.Latitude is < -90 or > 90 || double.IsNaN(position.Latitude))
        {
            throw new ArgumentException("Latitude must be from -90 to 90 degrees.");
        }

        if (position.Longitude is < -180 or > 180 || double.IsNaN(position.Longitude))
        {
            throw new ArgumentException("Longitude must be from -180 to 180 degrees.");
        }

        if (report.Symbol == default)
        {
            throw new ArgumentException("A position needs a symbol.");
        }

        if (report.Message == MicEMessage.Unknown)
        {
            throw new ArgumentException("A Mic-E message must be a standard or custom message, or Emergency.");
        }

        if (report.Timestamp is not null || report.IsCompressed)
        {
            throw new ArgumentException("A Mic-E report has no timestamp and is not compressed.");
        }

        if (
            report.PowerHeightGain is not null
            || report.RangeMiles is not null
            || report.DirectionFindingStrength is not null
        )
        {
            throw new ArgumentException("A Mic-E report carries course and speed, not PHG, range or DF strength.");
        }

        if (report.Course is < 0 or > 360)
        {
            throw new ArgumentException("Course must be 0 to 360 degrees.");
        }

        if (
            report.ExtensionBytes.IsEmpty
            && report.SpeedKnots is { } speed
            && (speed < 0 || Math.Round(speed) > MaxSpeedKnots)
        )
        {
            throw new ArgumentException($"A Mic-E speed must be 0 to {MaxSpeedKnots} knots.");
        }
    }

    /// <summary>
    /// The speed and course bytes, each offset by 28. Where the format allows (speeds under 40 knots, a DC byte whose
    /// tens are zero), the bytes are moved out of the control characters: the decoder subtracts 800 knots and 400
    /// degrees again. The last byte (tens and units of the course) has no such form.
    /// </summary>
    private static void SpeedCourse(List<byte> info, MicEReport report)
    {
        var speed = (int)Math.Round(report.SpeedKnots ?? 0, MidpointRounding.AwayFromZero);
        var course = report.Course ?? 0;
        var sp = speed / 10;
        var dc = (speed % 10 * 10) + (course / 100);
        info.Add((byte)(sp < 4 ? sp + 80 + 28 : sp + 28));
        info.Add((byte)(dc < 4 ? dc + 4 + 28 : dc + 28));
        info.Add((byte)((course % 100) + 28));
    }

    /// <summary>
    /// The longitude as degrees, minutes and hundredths, with the digits the latitude's ambiguity blanks set to zero.
    /// </summary>
    private static (int Degrees, int Minutes, int Hundredths) Longitude(MicEReport report)
    {
        var digits = Digits(report.Position.Longitude, 3, report.Datum is not null);
        var degrees = (digits[0] * 100) + (digits[1] * 10) + digits[2];
        if (degrees >= 180)
        {
            throw new ArgumentException("A Mic-E longitude must be less than 180 degrees east or west.");
        }

        var ambiguity = Ambiguity(report);
        var minutes = ((ambiguity >= 4 ? 0 : digits[3]) * 10) + (ambiguity >= 3 ? 0 : digits[4]);
        var hundredths = ((ambiguity >= 2 ? 0 : digits[5]) * 10) + (ambiguity >= 1 ? 0 : digits[6]);
        return (degrees, minutes, hundredths);
    }

    /// <summary>Longitude degrees from the first longitude byte, as the decoder reads them.</summary>
    private static int Degrees(byte value, bool offset)
    {
        var degrees = value - 28 + (offset ? 100 : 0);
        return degrees switch
        {
            >= 180 and <= 189 => degrees - 80,
            >= 190 and <= 199 => degrees - 190,
            _ => degrees,
        };
    }

    private static int Ambiguity(MicEReport report) =>
        report.Position.Ambiguity is >= 0 and <= 4
            ? report.Position.Ambiguity
            : throw new ArgumentException("Position ambiguity must be 0 to 4.");

    /// <summary>
    /// The decimal digits of <c>DDMMhh</c> or <c>DDDMMhh</c>, rounded (or truncated when a <c>!DAO!</c> token adds
    /// precision) to hundredths of a minute.
    /// </summary>
    private static int[] Digits(double degrees, int degreeDigits, bool truncate)
    {
        var hundredths = Math.Abs(degrees) * 6000;
        var total = (long)(
            truncate ? Math.Floor(hundredths + 1e-6) : Math.Round(hundredths, MidpointRounding.AwayFromZero)
        );
        var text =
            (total / 6000).ToString(new string('0', degreeDigits), System.Globalization.CultureInfo.InvariantCulture)
            + (total % 6000).ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
        return [.. text.Select(c => c - '0')];
    }

    /// <summary>Whether the message bits are custom, and their value: 7 for M0/C0 down to 1 for M6/C6, 0 for Emergency.</summary>
    private static (bool Custom, int Bits) MessageBits(MicEMessage message) =>
        message switch
        {
            MicEMessage.Emergency => (false, 0),
            >= MicEMessage.Custom0 and <= MicEMessage.Custom6 => (true, 7 - (message - MicEMessage.Custom0)),
            >= MicEMessage.OffDuty and <= MicEMessage.Priority => (false, 7 - (message - MicEMessage.OffDuty)),
            _ => throw new ArgumentException("A Mic-E message must be a standard or custom message, or Emergency."),
        };
}
