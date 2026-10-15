using System.Globalization;
using System.Text;

namespace AprsNet.Packets;

/// <summary>
/// Writes weather readings (APRS101 ch. 12 with the APRS 1.1 clarifications): positionless reports, and the readings
/// that follow the wind in a position, object or item with the weather station symbol. Gust and temperature are
/// always written, as dots when unknown, since APRS101 makes them part of every report; the other fields only when
/// set. Values are rounded to the field's units.
/// </summary>
internal static class WeatherEncoder
{
    /// <summary>
    /// Writes a positionless report: <c>_</c>, the <c>MMDDHHMM</c> timestamp, wind as <c>cDDD</c> and <c>sSSS</c>, then
    /// the other readings and the comment.
    /// </summary>
    public static byte[] Encode(WeatherReport report)
    {
        var info = new List<byte>(64) { (byte)'_' };
        if (report.ReadingsBytes is { } readings)
        {
            // Parsed: the timestamp, readings and comment as they arrived, an unreadable timestamp included.
            info.AddRange(readings.Span);
            info.AddRange(report.CommentBytes.Span);
            return [.. info];
        }

        if (report.Timestamp is not { Format: AprsTimestampFormat.MonthDayHourMinute } timestamp)
        {
            throw new ArgumentException(
                "A positionless weather report needs a MMDDHHMM timestamp: use AprsTimestampFormat.MonthDayHourMinute."
            );
        }

        info.AddRange(Encoding.ASCII.GetBytes(timestamp.ToString()));

        var weather = report.Weather ?? throw new ArgumentException("A weather report needs its readings.");
        if (weather.SnowLast24HoursInches is not null)
        {
            throw new ArgumentException(
                "A positionless report cannot carry snowfall, since its s field is the wind speed. Send a position "
                    + "with the weather station symbol instead."
            );
        }

        var text = new StringBuilder();
        text.Append('c').Append(Direction(weather.WindDirection));
        text.Append('s').Append(Speed(weather.WindSpeedMph, "Wind speed"));
        Readings(text, weather);
        info.AddRange(Encoding.ASCII.GetBytes(text.ToString()));
        info.AddRange(AprsEncoder.TextBytes(ReadOnlyMemory<byte>.Empty, report.Comment, "comment"));
        return [.. info];
    }

    /// <summary>The readings after the wind, from gust onwards, for the comment of a complete report.</summary>
    public static string Readings(WeatherData weather)
    {
        var text = new StringBuilder();
        Readings(text, weather);
        if (weather.SnowLast24HoursInches is { } snow)
        {
            text.Append('s').Append(Snow(snow));
        }

        if (weather.RainCounter is { } counter)
        {
            text.Append('#').Append(Number(counter, 0, 999, "The rain counter", "000"));
        }

        return text.ToString();
    }

    /// <summary>
    /// The wind direction for the course field, three digits or dots: 0 to 360 degrees, as APRS101 has 360 for north
    /// so 000 can mean calm.
    /// </summary>
    public static string Direction(int? degrees) =>
        degrees is { } d ? Number(d, 0, 360, "Wind direction", "000") : "...";

    /// <summary>A speed in mph for the speed field: three digits or dots.</summary>
    public static string Speed(double? mph, string what) =>
        mph is { } s ? Number(Round(s), 0, 999, what, "000") : "...";

    /// <summary>
    /// Checks the readings a caller set against the decoded ones: the same fields present, each within half a step of
    /// its units. Wind is compared with the course and speed.
    /// </summary>
    public static string? Mismatch(WeatherData e, WeatherData a) =>
        Gained(e.WindGustMph, a.WindGustMph)
        || Gained(e.TemperatureF, a.TemperatureF)
        || Gained(e.RainLastHourInches, a.RainLastHourInches)
        || Gained(e.RainLast24HoursInches, a.RainLast24HoursInches)
        || Gained(e.RainSinceMidnightInches, a.RainSinceMidnightInches)
        || Gained(e.RainCounter, a.RainCounter)
        || Gained(e.HumidityPercent, a.HumidityPercent)
        || Gained(e.PressureMillibars, a.PressureMillibars)
        || Gained(e.LuminosityWattsPerSquareMeter, a.LuminosityWattsPerSquareMeter)
        || Gained(e.SnowLast24HoursInches, a.SnowLast24HoursInches)
            ? "the comment would be read as weather readings"
        : Differs(e.WindGustMph, a.WindGustMph, 0.5) ? "the wind gust does not survive encoding"
        : Differs(e.TemperatureF, a.TemperatureF, 0.5) ? "the temperature does not survive encoding"
        : Differs(e.RainLastHourInches, a.RainLastHourInches, 0.005)
        || Differs(e.RainLast24HoursInches, a.RainLast24HoursInches, 0.005)
        || Differs(e.RainSinceMidnightInches, a.RainSinceMidnightInches, 0.005)
        || Differs(e.RainCounter, a.RainCounter, 0)
            ? "the rainfall does not survive encoding"
        : Differs(e.HumidityPercent, a.HumidityPercent, 0) ? "the humidity does not survive encoding"
        : Differs(e.PressureMillibars, a.PressureMillibars, 0.05) ? "the pressure does not survive encoding"
        : Differs(e.LuminosityWattsPerSquareMeter, a.LuminosityWattsPerSquareMeter, 0)
            ? "the luminosity does not survive encoding"
        : Differs(e.SnowLast24HoursInches, a.SnowLast24HoursInches, 0.5) ? "the snowfall does not survive encoding"
        : null;

    /// <summary>Gust and temperature always, then rain, humidity, pressure and luminosity when set.</summary>
    private static void Readings(StringBuilder text, WeatherData weather)
    {
        text.Append('g').Append(Speed(weather.WindGustMph, "Wind gust"));
        text.Append('t').Append(weather.TemperatureF is { } t ? Temperature(t) : "...");
        Rain(text, 'r', weather.RainLastHourInches);
        Rain(text, 'p', weather.RainLast24HoursInches);
        Rain(text, 'P', weather.RainSinceMidnightInches);
        if (weather.HumidityPercent is { } humidity)
        {
            // h00 is 100%, so 0% cannot be sent; no real air is that dry.
            text.Append('h').Append(Number(humidity, 1, 100, "Humidity", "00")[^2..]);
        }

        if (weather.PressureMillibars is { } pressure)
        {
            text.Append('b').Append(Number(Round(pressure * 10), 0, 99999, "Pressure", "00000"));
        }

        if (weather.LuminosityWattsPerSquareMeter is { } light)
        {
            _ = Number(light, 0, 1999, "Luminosity", "0000");
            text.Append(light < 1000 ? 'L' : 'l').Append((light % 1000).ToString("000", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Three characters, a minus sign taking the first: <c>-05</c> to <c>999</c>.</summary>
    private static string Temperature(int fahrenheit) =>
        fahrenheit switch
        {
            < -99 or > 999 => throw new ArgumentException("Temperature must be -99 to 999 °F."),
            < 0 => "-" + (-fahrenheit).ToString("00", CultureInfo.InvariantCulture),
            _ => fahrenheit.ToString("000", CultureInfo.InvariantCulture),
        };

    private static void Rain(StringBuilder text, char letter, double? inches)
    {
        if (inches is { } value)
        {
            text.Append(letter).Append(Number(Round(value * 100), 0, 999, "Rainfall", "000"));
        }
    }

    /// <summary>Snowfall: one decimal below 10 inches when it has a fraction (<c>1.5</c>), whole inches otherwise.</summary>
    private static string Snow(double inches)
    {
        var tenths = Math.Round(inches, 1, MidpointRounding.AwayFromZero);
        return tenths < 10 && tenths != Math.Floor(tenths)
            ? tenths.ToString("0.0", CultureInfo.InvariantCulture)
            : Number(Round(inches), 0, 999, "Snowfall", "000");
    }

    private static int Round(double value) =>
        double.IsFinite(value) && Math.Abs(value) < int.MaxValue
            ? (int)Math.Round(value, MidpointRounding.AwayFromZero)
            : throw new ArgumentException("Weather readings must be finite numbers.");

    private static string Number(int value, int min, int max, string what, string format) =>
        value < min || value > max
            ? throw new ArgumentException($"{what} must be {min} to {max} in its units.")
            : value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>A reading the receiver finds that was not sent: the start of the comment, read as a field.</summary>
    private static bool Gained(double? expected, double? actual) => expected is null && actual is not null;

    private static bool Differs(double? expected, double? actual, double slack) =>
        (expected is null) != (actual is null)
        || (expected is { } e && actual is { } a && Math.Abs(e - a) > slack + 1e-9);
}
