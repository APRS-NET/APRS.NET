namespace AprsNet.Packets;

/// <summary>
/// Converts the readings APRS carries between units. APRS sends speed in knots (positions) or miles per hour (weather),
/// lengths in feet and miles, rain in hundredths of an inch, temperature in °F and pressure in tenths of a millibar.
/// </summary>
/// <remarks>
/// Speed and length use their exact definitions (1 knot = 1852 m/h, 1 foot = 0.3048 m, 1 mile = 1609.344 m, 1 inch =
/// 25.4 mm). Pressure uses the conventional mercury units: 1 inHg = 33.8639 hPa and 1 mmHg = 1.33322 hPa (more
/// precisely 3386.389 Pa and 133.322387415 Pa).
/// </remarks>
public static class AprsUnits
{
    /// <summary>Converts a speed.</summary>
    public static double Convert(double value, SpeedUnit from, SpeedUnit to) =>
        from == to ? value : value * MetersPerSecond(from) / MetersPerSecond(to);

    /// <summary>Converts a length or distance, including rainfall and snowfall.</summary>
    public static double Convert(double value, LengthUnit from, LengthUnit to) =>
        from == to ? value : value * Meters(from) / Meters(to);

    /// <summary>Converts a temperature.</summary>
    public static double Convert(double value, TemperatureUnit from, TemperatureUnit to) =>
        (from, to) switch
        {
            (TemperatureUnit.Fahrenheit, TemperatureUnit.Celsius) => (value - 32) * 5 / 9,
            (TemperatureUnit.Celsius, TemperatureUnit.Fahrenheit) => (value * 9 / 5) + 32,
            _ when from == to => value,
            _ => throw Undefined(Enum.IsDefined(from) ? nameof(to) : nameof(from)),
        };

    /// <summary>Converts an air pressure.</summary>
    public static double Convert(double value, PressureUnit from, PressureUnit to) =>
        from == to ? value : value * Pascals(from) / Pascals(to);

    private static double MetersPerSecond(SpeedUnit unit) =>
        unit switch
        {
            SpeedUnit.Knots => 1852.0 / 3600,
            SpeedUnit.MilesPerHour => 0.44704,
            SpeedUnit.KilometersPerHour => 1 / 3.6,
            SpeedUnit.MetersPerSecond => 1,
            _ => throw Undefined(nameof(unit)),
        };

    private static double Meters(LengthUnit unit) =>
        unit switch
        {
            LengthUnit.Millimeters => 0.001,
            LengthUnit.Inches => 0.0254,
            LengthUnit.Feet => 0.3048,
            LengthUnit.Meters => 1,
            LengthUnit.Kilometers => 1000,
            LengthUnit.Miles => 1609.344,
            LengthUnit.NauticalMiles => 1852,
            _ => throw Undefined(nameof(unit)),
        };

    private static double Pascals(PressureUnit unit) =>
        unit switch
        {
            PressureUnit.Millibars => 100,
            PressureUnit.InchesOfMercury => 3386.389,
            PressureUnit.MillimetersOfMercury => 133.322387415,
            _ => throw Undefined(nameof(unit)),
        };

    private static ArgumentOutOfRangeException Undefined(string parameter) =>
        new(parameter, "The unit is not one of the enumeration's values.");
}

/// <summary>A unit of speed, for <see cref="AprsUnits"/>.</summary>
public enum SpeedUnit
{
    /// <summary>Knots (nautical miles per hour): the speed in APRS position reports.</summary>
    Knots,

    /// <summary>Miles per hour: wind speed and gusts in APRS weather reports.</summary>
    MilesPerHour,

    /// <summary>Kilometres per hour.</summary>
    KilometersPerHour,

    /// <summary>Metres per second.</summary>
    MetersPerSecond,
}

/// <summary>A unit of length, for <see cref="AprsUnits"/>.</summary>
public enum LengthUnit
{
    /// <summary>Millimetres.</summary>
    Millimeters,

    /// <summary>Inches: rain and snow in APRS weather reports.</summary>
    Inches,

    /// <summary>Feet: altitude and antenna height in APRS.</summary>
    Feet,

    /// <summary>Metres.</summary>
    Meters,

    /// <summary>Kilometres.</summary>
    Kilometers,

    /// <summary>Statute miles: ranges in APRS.</summary>
    Miles,

    /// <summary>Nautical miles.</summary>
    NauticalMiles,
}

/// <summary>A unit of temperature, for <see cref="AprsUnits"/>.</summary>
public enum TemperatureUnit
{
    /// <summary>Degrees Fahrenheit: temperature in APRS weather reports.</summary>
    Fahrenheit,

    /// <summary>Degrees Celsius.</summary>
    Celsius,
}

/// <summary>A unit of air pressure, for <see cref="AprsUnits"/>.</summary>
public enum PressureUnit
{
    /// <summary>Millibars, the same as hectopascals: barometric pressure in APRS weather reports.</summary>
    Millibars,

    /// <summary>Inches of mercury.</summary>
    InchesOfMercury,

    /// <summary>Millimetres of mercury.</summary>
    MillimetersOfMercury,
}
