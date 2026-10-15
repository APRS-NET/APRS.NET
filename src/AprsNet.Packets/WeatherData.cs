namespace AprsNet.Packets;

/// <summary>
/// Weather readings (APRS101 ch. 12, with the APRS 1.1 luminosity, snowfall and raw rain counter fields). Each value
/// is <see langword="null"/> when the station did not send it or sent dots or spaces for "no data".
/// </summary>
/// <remarks>
/// To send readings, set the ones the station has on a <see cref="WeatherReport"/>, or on a position, object or item
/// with the weather station symbol (<c>_</c>). Values are rounded to what the format carries: whole degrees, mph and
/// °F, hundredths of an inch of rain and tenths of a millibar.
/// </remarks>
public sealed record WeatherData
{
    /// <summary>Wind direction in degrees: from <c>DIR/SPD</c>, a compressed course, or <c>c</c>.</summary>
    public int? WindDirection { get; init; }

    /// <summary>
    /// Sustained wind speed: from <c>DIR/SPD</c>, a compressed speed, or <c>s</c> in a positionless report. APRS101
    /// says knots for <c>DIR/SPD</c> and mph for the positionless form; weather stations send mph in both.
    /// </summary>
    public double? WindSpeedMph { get; init; }

    /// <summary>Peak wind speed in the last five minutes, in mph (<c>g</c>).</summary>
    public int? WindGustMph { get; init; }

    /// <summary>Temperature in degrees Fahrenheit (<c>t</c>), which may be negative.</summary>
    public int? TemperatureF { get; init; }

    /// <summary>Rainfall in the last hour, in inches (<c>r</c>, sent in hundredths).</summary>
    public double? RainLastHourInches { get; init; }

    /// <summary>Rainfall in the last 24 hours, in inches (<c>p</c>, sent in hundredths).</summary>
    public double? RainLast24HoursInches { get; init; }

    /// <summary>Rainfall since local midnight, in inches (<c>P</c>, sent in hundredths).</summary>
    public double? RainSinceMidnightInches { get; init; }

    /// <summary>Relative humidity in percent (<c>h</c>; <c>h00</c> means 100%).</summary>
    public int? HumidityPercent { get; init; }

    /// <summary>Barometric pressure in millibars (hectopascals) (<c>b</c>, sent in tenths).</summary>
    public double? PressureMillibars { get; init; }

    /// <summary>Luminosity in watts per square metre (<c>L</c> below 1000, <c>l</c> from 1000).</summary>
    public int? LuminosityWattsPerSquareMeter { get; init; }

    /// <summary>Snowfall in the last 24 hours, in inches (<c>s</c> after the wind fields).</summary>
    public double? SnowLast24HoursInches { get; init; }

    /// <summary>The raw rain counter (<c>#</c>), for stations without a calibrated rain gauge.</summary>
    public int? RainCounter { get; init; }
}
