using System.Text;

namespace AprsNet.Packets.Tests;

public class WeatherCreateTests
{
    private static readonly PacketAddress _address = PacketAddress.Parse("N0CALL-9", "APZ123", []);
    private static readonly AprsPosition _position = new(49 + (3.5 / 60), -(72 + (1.75 / 60)));
    private static readonly AprsSymbol _weatherStation = new('/', '_');

    // The readings of the APRS101 ch. 12 examples.
    private static readonly WeatherData _apr101 = new()
    {
        WindDirection = 220,
        WindSpeedMph = 4,
        WindGustMph = 5,
        TemperatureF = 77,
        RainLastHourInches = 0,
        RainLast24HoursInches = 0,
        RainSinceMidnightInches = 0,
        HumidityPercent = 50,
        PressureMillibars = 990,
    };

    private static string Information(AprsPayload payload) =>
        Encoding.Latin1.GetString(_address.Create(payload).Information.Span);

    private static void Rejects(AprsPayload payload, string reason)
    {
        var ex = Assert.Throws<ArgumentException>(() => _address.Create(payload));
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
    }

    private static PositionReport Station(WeatherData weather, string comment = "") =>
        new()
        {
            Position = _position,
            Symbol = _weatherStation,
            Weather = weather,
            Comment = comment,
        };

    // Complete reports: a position with the weather station symbol

    [Fact]
    public void WritesACompleteReportAsInApr101()
    {
        var packet = _address.Create(Station(_apr101, "wRSW"));

        Assert.Equal(
            "!4903.50N/07201.75W_220/004g005t077r000p000P000h50b09900wRSW",
            Encoding.Latin1.GetString(packet.Information.Span)
        );
        var decoded = (PositionReport)packet.Payload!;
        Assert.Equal(_apr101, decoded.Weather);
        Assert.Equal("wRSW", decoded.Comment);
    }

    [Fact]
    public void WritesANegativeTemperatureWithItsSign()
    {
        var report = new PositionReport
        {
            Timestamp = AprsTimestamp.DayHourMinute(9, 23, 45),
            Position = _position,
            Symbol = _weatherStation,
            Weather = _apr101 with { TemperatureF = -7 },
            Comment = "wRSW",
        };

        Assert.Equal("/092345z4903.50N/07201.75W_220/004g005t-07r000p000P000h50b09900wRSW", Information(report));
    }

    [Fact]
    public void WritesUnknownWindGustAndTemperatureAsDots()
    {
        Assert.Equal(
            "!4903.50N/07201.75W_.../...g...t...h00",
            Information(Station(new WeatherData { HumidityPercent = 100 }))
        );
    }

    [Fact]
    public void WritesOnlyTheReadingsThatAreSet()
    {
        Assert.Equal(
            "!4903.50N/07201.75W_090/005g...t068",
            Information(
                Station(
                    new WeatherData
                    {
                        WindDirection = 90,
                        WindSpeedMph = 5,
                        TemperatureF = 68,
                    }
                )
            )
        );
    }

    [Fact]
    public void TakesTheWindFromTheCourseAndSpeedToo()
    {
        var report = new PositionReport
        {
            Position = _position,
            Symbol = _weatherStation,
            Course = 220,
            SpeedKnots = 4,
            Weather = new WeatherData { TemperatureF = 77 },
        };

        Assert.Equal("!4903.50N/07201.75W_220/004g...t077", Information(report));
    }

    [Theory]
    [InlineData(850, "L850")]
    [InlineData(999, "L999")]
    [InlineData(1000, "l000")]
    [InlineData(1123, "l123")]
    public void WritesLuminosityInTwoRanges(int wattsPerSquareMeter, string field)
    {
        var info = Information(Station(new WeatherData { LuminosityWattsPerSquareMeter = wattsPerSquareMeter }));

        Assert.EndsWith(field, info, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1.5, "s1.5")]
    [InlineData(1, "s001")]
    [InlineData(10, "s010")]
    [InlineData(9.96, "s010")]
    public void WritesSnowfallWithATenthBelowTenInches(double inches, string field)
    {
        var info = Information(Station(new WeatherData { SnowLast24HoursInches = inches }));

        Assert.EndsWith(field, info, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundsReadingsToTheirUnits()
    {
        var packet = _address.Create(
            Station(
                new WeatherData
                {
                    WindSpeedMph = 4.6,
                    RainLastHourInches = 0.123,
                    PressureMillibars = 1013.26,
                    RainCounter = 42,
                }
            )
        );

        Assert.Equal(
            "!4903.50N/07201.75W_.../005g...t...r012b10133#042",
            Encoding.Latin1.GetString(packet.Information.Span)
        );
        var weather = ((PositionReport)packet.Payload!).Weather!;
        Assert.Equal(0.12, weather.RainLastHourInches);
        Assert.Equal(1013.3, weather.PressureMillibars);
    }

    [Fact]
    public void PutsTheReadingsAheadOfTheAltitude()
    {
        var report = new PositionReport
        {
            Position = _position,
            Symbol = _weatherStation,
            Weather = new WeatherData { TemperatureF = 50 },
            AltitudeFeet = 1234,
            Comment = "Hilltop",
        };

        var packet = _address.Create(report);

        Assert.Equal(
            "!4903.50N/07201.75W_.../...g...t050/A=001234Hilltop",
            Encoding.Latin1.GetString(packet.Information.Span)
        );
        Assert.Equal(1234, ((PositionReport)packet.Payload!).AltitudeFeet);
    }

    [Fact]
    public void WritesACompressedWeatherStation()
    {
        var report = new PositionReport
        {
            Position = _position,
            Symbol = _weatherStation,
            IsCompressed = true,
            Weather = _apr101,
        };

        var decoded = (PositionReport)_address.Create(report).Payload!;

        Assert.True(decoded.IsCompressed);
        Assert.Equal(220, decoded.Weather!.WindDirection);
        Assert.Equal(77, decoded.Weather.TemperatureF);
        Assert.Equal(990, decoded.Weather.PressureMillibars);
    }

    [Fact]
    public void WritesAnObjectWithWeather()
    {
        var report = new ObjectReport
        {
            Name = "WX-HILL",
            Timestamp = AprsTimestamp.DayHourMinute(9, 23, 45),
            Position = _position,
            Symbol = _weatherStation,
            Weather = new WeatherData
            {
                WindDirection = 180,
                WindSpeedMph = 10,
                TemperatureF = 45,
            },
        };

        Assert.Equal(";WX-HILL  *092345z4903.50N/07201.75W_180/010g...t045", Information(report));
    }

    [Fact]
    public void RefusesReadingsWithoutTheWeatherStationSymbol()
    {
        var report = new PositionReport
        {
            Position = _position,
            Symbol = new AprsSymbol('/', '>'),
            Weather = _apr101,
        };

        Rejects(report, "weather station symbol");
    }

    [Fact]
    public void RefusesACourseThatDiffersFromTheWindDirection()
    {
        var report = new PositionReport
        {
            Position = _position,
            Symbol = _weatherStation,
            Course = 90,
            Weather = _apr101,
        };

        Rejects(report, "Course and wind direction differ");
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(1000)]
    public void RefusesATemperatureOutOfRange(int fahrenheit)
    {
        Rejects(Station(new WeatherData { TemperatureF = fahrenheit }), "Temperature must be -99 to 999");
    }

    [Fact]
    public void RefusesZeroHumidityWhichWouldReadAsAHundred()
    {
        Rejects(Station(new WeatherData { HumidityPercent = 0 }), "Humidity must be 1 to 100");
    }

    [Fact]
    public void RefusesWeatherOnAMicEReport()
    {
        var report = new MicEReport
        {
            Position = _position,
            Symbol = _weatherStation,
            Message = MicEMessage.InService,
            Weather = _apr101,
        };

        Rejects(report, "Mic-E report carries no weather");
    }

    [Fact]
    public void RefusesACommentThatWouldReadAsReadings()
    {
        Rejects(Station(new WeatherData { TemperatureF = 50 }, "h45 in the shade"), "weather");
    }

    // Positionless reports

    [Fact]
    public void WritesAPositionlessReportAsInApr101()
    {
        var report = new WeatherReport
        {
            Timestamp = AprsTimestamp.MonthDayHourMinute(10, 9, 5, 56),
            Weather = _apr101,
            Comment = "wRSW",
        };

        var packet = _address.Create(report);

        Assert.Equal(
            "_10090556c220s004g005t077r000p000P000h50b09900wRSW",
            Encoding.Latin1.GetString(packet.Information.Span)
        );
        Assert.Equal(_apr101, ((WeatherReport)packet.Payload!).Weather);
    }

    [Fact]
    public void WritesUnknownPositionlessWindAsDots()
    {
        var report = new WeatherReport
        {
            Timestamp = AprsTimestamp.MonthDayHourMinute(10, 9, 5, 56),
            Weather = new WeatherData { TemperatureF = 41 },
        };

        Assert.Equal("_10090556c...s...g...t041", Information(report));
    }

    [Fact]
    public void RefusesAPositionlessTimestampInAnotherFormat()
    {
        var report = new WeatherReport { Timestamp = AprsTimestamp.DayHourMinute(9, 23, 45), Weather = _apr101 };

        Rejects(report, "MMDDHHMM");
    }

    [Fact]
    public void RefusesSnowfallInAPositionlessReport()
    {
        var report = new WeatherReport
        {
            Timestamp = AprsTimestamp.MonthDayHourMinute(10, 9, 5, 56),
            Weather = new WeatherData { SnowLast24HoursInches = 2 },
        };

        Rejects(report, "cannot carry snowfall");
    }

    [Fact]
    public void RebuildsAParsedReportByteForByte()
    {
        // Spaces for no data, three humidity digits and an unusual order: kept as sent.
        var parsed = AprsPacket.Parse("N0CALL>APRS:_10090556c220s004t077g   h100b09900 comment");

        var rebuilt = AprsPacket.Create(parsed.Source!, parsed.Destination!, parsed.Path!, parsed.Payload!);

        Assert.Equal(parsed.Information.ToArray(), rebuilt.Information.ToArray());
    }
}
