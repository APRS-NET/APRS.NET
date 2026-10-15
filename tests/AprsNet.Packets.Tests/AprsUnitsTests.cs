namespace AprsNet.Packets.Tests;

public class AprsUnitsTests
{
    [Theory]
    [InlineData(SpeedUnit.Knots, 1, SpeedUnit.KilometersPerHour, 1.852)]
    [InlineData(SpeedUnit.Knots, 1, SpeedUnit.MetersPerSecond, 0.514444)]
    [InlineData(SpeedUnit.MilesPerHour, 1, SpeedUnit.KilometersPerHour, 1.609344)]
    [InlineData(SpeedUnit.MilesPerHour, 100, SpeedUnit.Knots, 86.897624)]
    [InlineData(SpeedUnit.MetersPerSecond, 10, SpeedUnit.KilometersPerHour, 36)]
    public void ConvertsSpeeds(SpeedUnit from, double value, SpeedUnit to, double expected) =>
        Assert.Equal(expected, AprsUnits.Convert(value, from, to), 6);

    [Theory]
    [InlineData(LengthUnit.Feet, 1, LengthUnit.Meters, 0.3048)]
    [InlineData(LengthUnit.Miles, 1, LengthUnit.Kilometers, 1.609344)]
    [InlineData(LengthUnit.NauticalMiles, 1, LengthUnit.Meters, 1852)]
    [InlineData(LengthUnit.Inches, 1, LengthUnit.Millimeters, 25.4)]
    [InlineData(LengthUnit.Kilometers, 10, LengthUnit.Miles, 6.213712)]
    public void ConvertsLengths(LengthUnit from, double value, LengthUnit to, double expected) =>
        Assert.Equal(expected, AprsUnits.Convert(value, from, to), 6);

    [Theory]
    [InlineData(TemperatureUnit.Fahrenheit, 32, TemperatureUnit.Celsius, 0)]
    [InlineData(TemperatureUnit.Fahrenheit, 212, TemperatureUnit.Celsius, 100)]
    [InlineData(TemperatureUnit.Fahrenheit, -40, TemperatureUnit.Celsius, -40)]
    [InlineData(TemperatureUnit.Celsius, 37, TemperatureUnit.Fahrenheit, 98.6)]
    [InlineData(TemperatureUnit.Celsius, 20, TemperatureUnit.Celsius, 20)]
    public void ConvertsTemperatures(TemperatureUnit from, double value, TemperatureUnit to, double expected) =>
        Assert.Equal(expected, AprsUnits.Convert(value, from, to), 9);

    [Theory]
    [InlineData(PressureUnit.Millibars, 1013.25, PressureUnit.InchesOfMercury, 29.921)] // standard atmosphere
    [InlineData(PressureUnit.Millibars, 1013.25, PressureUnit.MillimetersOfMercury, 760)]
    [InlineData(PressureUnit.InchesOfMercury, 1, PressureUnit.Millibars, 33.864)]
    public void ConvertsPressures(PressureUnit from, double value, PressureUnit to, double expected) =>
        Assert.Equal(expected, AprsUnits.Convert(value, from, to), 3);

    [Fact]
    public void EveryConversionRoundTrips()
    {
        const double value = 123.456;
        foreach (var from in Enum.GetValues<SpeedUnit>())
        {
            foreach (var to in Enum.GetValues<SpeedUnit>())
            {
                Assert.Equal(value, AprsUnits.Convert(AprsUnits.Convert(value, from, to), to, from), 10);
            }
        }

        foreach (var from in Enum.GetValues<LengthUnit>())
        {
            foreach (var to in Enum.GetValues<LengthUnit>())
            {
                Assert.Equal(value, AprsUnits.Convert(AprsUnits.Convert(value, from, to), to, from), 10);
            }
        }

        foreach (var from in Enum.GetValues<TemperatureUnit>())
        {
            foreach (var to in Enum.GetValues<TemperatureUnit>())
            {
                Assert.Equal(value, AprsUnits.Convert(AprsUnits.Convert(value, from, to), to, from), 10);
            }
        }

        foreach (var from in Enum.GetValues<PressureUnit>())
        {
            foreach (var to in Enum.GetValues<PressureUnit>())
            {
                Assert.Equal(value, AprsUnits.Convert(AprsUnits.Convert(value, from, to), to, from), 10);
            }
        }
    }

    [Fact]
    public void RefusesUndefinedUnits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsUnits.Convert(1, (SpeedUnit)99, SpeedUnit.Knots));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsUnits.Convert(1, LengthUnit.Feet, (LengthUnit)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AprsUnits.Convert(1, (TemperatureUnit)99, TemperatureUnit.Celsius)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AprsUnits.Convert(1, PressureUnit.Millibars, (PressureUnit)99)
        );
    }
}
