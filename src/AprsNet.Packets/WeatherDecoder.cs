using System.Buffers;
using System.Globalization;

namespace AprsNet.Packets;

/// <summary>
/// Decodes weather readings (APRS101 ch. 12): positionless reports, and the readings in the comment of a position,
/// object or item with the weather station symbol (<c>_</c>). Fields may come in any order, as real stations send
/// them; reading stops at the first byte that does not start a field, and the rest is the comment.
/// </summary>
internal static class WeatherDecoder
{
    private static readonly SearchValues<byte> _decimal = SearchValues.Create("0123456789."u8);

    /// <summary>Decodes a positionless report; <paramref name="offset"/> is where the information field starts in Raw.</summary>
    public static WeatherReport? Decode(ReadOnlyMemory<byte> information, int offset, List<ParseIssue> issues)
    {
        // "!!" raw Ultimeter data is also typed as weather; its layout is not in APRS101, so it is not decoded.
        if (information.Span[0] != '_')
        {
            return null;
        }

        if (!AprsTimestamp.TryParseMonthDay(information.Span[1..], out var timestamp))
        {
            issues.Add(new ParseIssue(ParseIssueCode.InvalidTimestamp, ParseIssueSeverity.Error, offset + 1));
            return null;
        }

        var weather = new WeatherData();
        var body = information[9..];
        var at = 0;
        // Positionless reports start with the wind: cDDD direction and sSSS speed in mph.
        if (TryField(body.Span, (byte)'c', 3, ref at, out var direction))
        {
            weather.WindDirection = (int?)direction;
            if (TryField(body.Span, (byte)'s', 3, ref at, out var speed))
            {
                weather.WindSpeedMph = speed;
            }
        }

        at += ReadFields(body.Span[at..], weather, out _);
        var comment = body[at..];
        return new WeatherReport
        {
            Timestamp = timestamp,
            Weather = weather,
            CommentBytes = comment,
            Comment = AprsText.Decode(comment.Span),
        };
    }

    /// <summary>
    /// Reads the weather in the comment of a weather station position into <paramref name="target"/>: wind from its
    /// course and speed, the other fields from the comment, whose text then starts after the readings.
    /// </summary>
    public static void Apply(PositionedPayload target)
    {
        var weather = new WeatherData { WindDirection = target.Course, WindSpeedMph = target.SpeedKnots };
        var text = target.CommentBytes.Span;
        var used = 0;
        var wind = target.Course is not null || target.SpeedKnots is not null;
        // Some LoRa iGates send a compressed position with no course and speed, then DIR/SPD in the comment.
        if (!wind && target.IsCompressed && PositionDecoder.TryCourseSpeed(text, out var direction, out var speed))
        {
            (weather.WindDirection, weather.WindSpeedMph, used, wind) = (direction, speed, 7, true);
        }

        used += ReadFields(text[used..], weather, out var fields);
        if (fields == 0 && !wind)
        {
            return;
        }

        target.Weather = weather;
        target.Comment = PositionDecoder.ApplyDao(target.Position, text[used..]).Comment;
    }

    /// <summary>Reads fields until a byte that does not start one; returns the bytes used.</summary>
    private static int ReadFields(ReadOnlySpan<byte> text, WeatherData weather, out int fields)
    {
        var at = 0;
        fields = 0;
        while (at < text.Length)
        {
            var letter = text[at];
            var width = letter switch
            {
                (byte)'h' => 2,
                (byte)'b' => 5,
                (byte)'g' or (byte)'t' or (byte)'r' or (byte)'p' or (byte)'P' or (byte)'L' or (byte)'l' => 3,
                (byte)'s' or (byte)'#' => 3,
                _ => 0,
            };
            // Some stations send three humidity digits: h100 for 100%, or h092.
            if (
                letter == 'h'
                && text.Length >= at + 4
                && text.Slice(at + 1, 3).IndexOfAnyExceptInRange((byte)'0', (byte)'9') < 0
            )
            {
                width = 3;
            }

            // A field that repeats is more likely the start of the comment than a second reading.
            if (width == 0 || text[..at].Contains(letter) || !TryField(text, letter, width, ref at, out var value))
            {
                break;
            }

            fields++;
            switch (letter)
            {
                case (byte)'g':
                    weather.WindGustMph = (int?)value;
                    break;
                case (byte)'t':
                    weather.TemperatureF = (int?)value;
                    break;
                case (byte)'r':
                    weather.RainLastHourInches = value / 100;
                    break;
                case (byte)'p':
                    weather.RainLast24HoursInches = value / 100;
                    break;
                case (byte)'P':
                    weather.RainSinceMidnightInches = value / 100;
                    break;
                case (byte)'h':
                    weather.HumidityPercent = value == 0 ? 100 : (int?)value;
                    break;
                case (byte)'b':
                    weather.PressureMillibars = value / 10;
                    break;
                case (byte)'L':
                    weather.LuminosityWattsPerSquareMeter = (int?)value;
                    break;
                case (byte)'l':
                    weather.LuminosityWattsPerSquareMeter = (int?)value + 1000;
                    break;
                case (byte)'s':
                    weather.SnowLast24HoursInches = value;
                    break;
                default:
                    weather.RainCounter = (int?)value;
                    break;
            }
        }

        return at;
    }

    /// <summary>
    /// One field at <paramref name="at"/>: the letter, then <paramref name="width"/> digits (a leading <c>-</c> for
    /// temperature, a decimal point for snowfall), or dots or spaces for no data. Extra dots are taken too.
    /// </summary>
    private static bool TryField(ReadOnlySpan<byte> text, byte letter, int width, ref int at, out double? value)
    {
        value = null;
        if (text.Length < at + 2 || text[at] != letter)
        {
            return false;
        }

        // No data: dots, of any count since some stations send b... for b....., or exactly the width in spaces.
        if (text[at + 1] == '.')
        {
            at++;
            while (at < text.Length && text[at] == '.')
            {
                at++;
            }

            return true;
        }

        if (text.Length < at + 1 + width)
        {
            return false;
        }

        var field = text.Slice(at + 1, width);

        if (field.IndexOfAnyExcept((byte)' ') < 0)
        {
            at += 1 + width;
            return true;
        }

        var valid = letter switch
        {
            (byte)'t' => field[1..].IndexOfAnyExceptInRange((byte)'0', (byte)'9') < 0
                && (field[0] == '-' || char.IsAsciiDigit((char)field[0])),
            (byte)'s' => field.IndexOfAnyExcept(_decimal) < 0 && field.Count((byte)'.') <= 1,
            _ => field.IndexOfAnyExceptInRange((byte)'0', (byte)'9') < 0,
        };
        if (
            !valid
            || !double.TryParse(
                field,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var number
            )
        )
        {
            return false;
        }

        value = number;
        at += 1 + width;
        return true;
    }
}
