namespace AprsNet.Packets.Tests;

public class WeatherReportTests
{
    private const string _position = "!4903.50N/07201.75W_";

    private static AprsPacket Parse(string information) => AprsPacket.Parse("N0CALL>APRS:" + information);

    private static WeatherReport Positionless(string information)
    {
        var packet = Parse(information);
        Assert.Equal(PacketType.Weather, packet.Type);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.Empty(packet.Issues);
        return Assert.IsType<WeatherReport>(packet.Payload);
    }

    private static PositionedPayload Positioned(string information)
    {
        var packet = Parse(information);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.Empty(packet.Issues);
        return Assert.IsAssignableFrom<PositionedPayload>(packet.Payload);
    }

    private static WeatherData Weather(string comment) => Positioned(_position + comment).Weather!;

    [Fact]
    public void DecodesThePositionlessSpecExample()
    {
        var report = Positionless("_10090556c220s004g005t077r001p002P003h50b09900wRSW");

        Assert.Equal(AprsTimestampFormat.MonthDayHourMinute, report.Timestamp.Format);
        Assert.Equal("10090556", report.Timestamp.ToString());
        var w = report.Weather;
        Assert.Equal(220, w.WindDirection);
        Assert.Equal(4, w.WindSpeedMph);
        Assert.Equal(5, w.WindGustMph);
        Assert.Equal(77, w.TemperatureF);
        Assert.Equal(0.01, w.RainLastHourInches!.Value, 6);
        Assert.Equal(0.02, w.RainLast24HoursInches!.Value, 6);
        Assert.Equal(0.03, w.RainSinceMidnightInches!.Value, 6);
        Assert.Equal(50, w.HumidityPercent);
        Assert.Equal(990.0, w.PressureMillibars!.Value, 6);
        Assert.Equal("wRSW", report.Comment);
        Assert.Equal("wRSW"u8.ToArray(), report.CommentBytes.ToArray());
    }

    [Fact]
    public void PositionlessWindIsOptional()
    {
        var report = Positionless("_10090556t077");

        Assert.Null(report.Weather.WindDirection);
        Assert.Equal(77, report.Weather.TemperatureF);
    }

    [Fact]
    public void PositionlessDirectionWithoutSpeed()
    {
        var report = Positionless("_10090556c220g005");

        Assert.Equal(220, report.Weather.WindDirection);
        Assert.Null(report.Weather.WindSpeedMph);
        Assert.Equal(5, report.Weather.WindGustMph);
    }

    [Theory]
    [InlineData("_1009055")]
    [InlineData("_13090556c220")]
    [InlineData("_10320556c220")]
    [InlineData("_10092456c220")]
    [InlineData("_10090560c220")]
    [InlineData("_1009x556c220")]
    public void APositionlessReportNeedsAValidTimestamp(string information)
    {
        var packet = Parse(information);

        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Equal([new ParseIssue(ParseIssueCode.InvalidTimestamp, ParseIssueSeverity.Error, 13)], packet.Issues);
    }

    [Fact]
    public void RawWeatherIsNotDecoded()
    {
        var packet = Parse("!!006B005803500000----03E9--------002105140000005D");

        Assert.Equal(PacketType.Weather, packet.Type);
        Assert.Equal(ParseOutcome.Unsupported, packet.Outcome);
    }

    [Fact]
    public void MonthDayTimestampsResolveToTheLatestYear()
    {
        var received = new DateTimeOffset(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);

        Assert.Equal(
            new DateTimeOffset(2025, 12, 31, 23, 55, 0, TimeSpan.Zero),
            Positionless("_12312355t077").Timestamp.ToDateTimeOffset(received)
        );
        Assert.Equal(
            new DateTimeOffset(2026, 1, 1, 0, 20, 0, TimeSpan.Zero),
            Positionless("_01010020t077").Timestamp.ToDateTimeOffset(received)
        );
        // 29 February is in neither 2026 nor 2025.
        Assert.Null(Positionless("_02291200t077").Timestamp.ToDateTimeOffset(received));
        Assert.Equal(2, Positionless("_02291200t077").Timestamp.Month);
    }

    [Fact]
    public void DecodesTheCompleteSpecExample()
    {
        var report = Positioned(_position + "220/004g005t077r000p000P000h50b09900wRSW");

        Assert.Equal(220, report.Course);
        var w = Assert.IsType<WeatherData>(report.Weather);
        Assert.Equal(220, w.WindDirection);
        Assert.Equal(4, w.WindSpeedMph);
        Assert.Equal(77, w.TemperatureF);
        Assert.Equal(0, w.RainLastHourInches);
        Assert.Equal("wRSW", report.Comment);
        Assert.Equal("g005t077r000p000P000h50b09900wRSW"u8.ToArray(), report.CommentBytes.ToArray());
    }

    [Fact]
    public void ReadsFieldsInAnyOrder()
    {
        var w = Weather("220/004g005t077P012p034r056b10135h93L456AmbientCWOP.com");

        Assert.Equal(0.12, w.RainSinceMidnightInches!.Value, 6);
        Assert.Equal(0.34, w.RainLast24HoursInches!.Value, 6);
        Assert.Equal(0.56, w.RainLastHourInches!.Value, 6);
        Assert.Equal(1013.5, w.PressureMillibars!.Value, 6);
        Assert.Equal(93, w.HumidityPercent);
        Assert.Equal(456, w.LuminosityWattsPerSquareMeter);
    }

    [Theory]
    [InlineData("h00", 100)]
    [InlineData("h100", 100)]
    [InlineData("h092", 92)]
    [InlineData("h07", 7)]
    public void ReadsHumidity(string field, int percent)
    {
        Assert.Equal(percent, Weather("220/004" + field).HumidityPercent);
    }

    [Theory]
    [InlineData("t-07", -7)]
    [InlineData("t105", 105)]
    [InlineData("t000", 0)]
    public void ReadsTemperature(string field, int fahrenheit)
    {
        Assert.Equal(fahrenheit, Weather("220/004" + field).TemperatureF);
    }

    [Fact]
    public void ReadsTheApRs11Fields()
    {
        var w = Weather("220/004l123s1.5#456");

        Assert.Equal(1123, w.LuminosityWattsPerSquareMeter);
        Assert.Equal(1.5, w.SnowLast24HoursInches);
        Assert.Equal(456, w.RainCounter);
    }

    [Fact]
    public void DotsAndSpacesMeanNoData()
    {
        var report = Positioned(_position + ".../...g...t   r...b...L123h50 Hello");
        var w = report.Weather!;

        Assert.Null(w.WindDirection);
        Assert.Null(w.WindSpeedMph);
        Assert.Null(w.WindGustMph);
        Assert.Null(w.TemperatureF);
        Assert.Null(w.RainLastHourInches);
        Assert.Null(w.PressureMillibars);
        Assert.Equal(123, w.LuminosityWattsPerSquareMeter);
        Assert.Equal(50, w.HumidityPercent);
        Assert.Equal(" Hello", report.Comment);
    }

    [Theory]
    [InlineData("220/004g005t077t078", "t078")]
    [InlineData("220/004g005tx77", "tx77")]
    [InlineData("220/004g005t7", "t7")]
    [InlineData("220/004g005s1..", "s1..")]
    [InlineData("220/004g005Tesla", "Tesla")]
    public void StopsAtTheFirstByteThatIsNotAField(string comment, string rest)
    {
        Assert.Equal(rest, Positioned(_position + comment).Comment);
    }

    [Fact]
    public void ReadsWeatherWithoutWind()
    {
        var report = Positioned(_position + "g005t077Tesla Model S");

        Assert.Null(report.Weather!.WindDirection);
        Assert.Equal(77, report.Weather.TemperatureF);
        Assert.Equal("Tesla Model S", report.Comment);
    }

    [Fact]
    public void AWeatherSymbolWithoutReadingsHasNoWeather()
    {
        var report = Positioned(_position + "Davis VP Weather Stn");

        Assert.Null(report.Weather);
        Assert.Equal("Davis VP Weather Stn", report.Comment);
    }

    [Fact]
    public void OtherSymbolsHaveNoWeather()
    {
        Assert.Null(Positioned("!4903.50N/07201.75W>220/004g005t077").Weather);
    }

    [Fact]
    public void TakesWindFromACompressedCourseAndSpeed()
    {
        var w = Positioned("=/5L!!<*e7_7P[g005t077").Weather!;

        Assert.Equal(88, w.WindDirection);
        Assert.Equal(36.2, w.WindSpeedMph!.Value, 1);
        Assert.Equal(77, w.TemperatureF);
    }

    [Fact]
    public void ReadsDirSpdAfterACompressedPositionWithoutCourse()
    {
        // LoRa APRS iGates send a compressed position with blank course and speed, then the full weather string.
        var report = Positioned("!L4Q'-P2JP_  G090/005g...t063h59b10223LoRa APRS RX");

        Assert.Equal(90, report.Weather!.WindDirection);
        Assert.Equal(5, report.Weather.WindSpeedMph);
        Assert.Equal(63, report.Weather.TemperatureF);
        Assert.Equal("LoRa APRS RX", report.Comment);
    }

    [Fact]
    public void RemovesADaoTokenFromTheTextAfterTheReadings()
    {
        var report = Positioned(_position + "220/004t077 Hi !W23!");

        Assert.Equal(" Hi ", report.Comment);
        Assert.Equal('W', report.Datum);
    }

    [Fact]
    public void ObjectsCarryWeather()
    {
        var packet = Parse(";BRENDA   *092345z4903.50N/07201.75W_220/004g005t077");

        Assert.Equal(77, Assert.IsType<ObjectReport>(packet.Payload).Weather!.TemperatureF);
    }
}
