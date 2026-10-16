namespace AprsNet.Packets.Tests;

public sealed class TelemetryProfileTests
{
    // APRS101 ch. 13's example: N0QBF-11's balloon, with its definitions sent from N0QBF.
    private const string Parm = "N0QBF>APRS::N0QBF-11 :PARM.Battery,Btemp,ATemp,Pres,Alt,Camra,Chut,Sun,10m,ATV";
    private const string Unit = "N0QBF>APRS::N0QBF-11 :UNIT.Volts,deg.F,deg.F,Mbar,Kft,Click,OPEN,on,on,hi";
    private const string Eqns = "N0QBF>APRS::N0QBF-11 :EQNS.0,5.2,0,0,.53,-32,3,4.39,49,-32,3,18,1,2,3";
    private const string Bits = "N0QBF>APRS::N0QBF-11 :BITS.10110000,N0QBF's Big Balloon";
    private const string Report = "N0QBF-11>APRS:T#005,199,000,255,073,123,01101001";

    private static TelemetryDefinition D(string line) =>
        Assert.IsType<TelemetryDefinition>(AprsPacket.Parse(line).Payload);

    private static TelemetryReport T(string line) => Assert.IsType<TelemetryReport>(AprsPacket.Parse(line).Payload);

    private static TelemetryProfile Balloon() =>
        TelemetryProfile.Empty.With(D(Parm)).With(D(Unit)).With(D(Eqns)).With(D(Bits));

    [Fact]
    public void TheSpecExampleGivesNamedScaledReadings()
    {
        Assert.Equal("N0QBF-11", D(Parm).Addressee);
        var reading = Balloon().Apply(T(Report));

        Assert.Equal(5, reading.SequenceNumber);
        Assert.Equal("N0QBF's Big Balloon", reading.ProjectName);
        Assert.Equal(
            [
                new TelemetryAnalogReading(0, "Battery", "Volts", 199, 1034.8, true),
                new TelemetryAnalogReading(1, "Btemp", "deg.F", 0, -32, true),
                new TelemetryAnalogReading(2, "ATemp", "deg.F", 255, 196243.45, true),
                new TelemetryAnalogReading(3, "Pres", "Mbar", 73, -170291, true),
                new TelemetryAnalogReading(4, "Alt", "Kft", 123, 15378, true),
            ],
            reading.Analog.Select(Round)
        );
    }

    [Fact]
    public void DigitalChannelsAreOnByTheirBitSense()
    {
        // Bits 01101001 against sense 10110000: on where they match.
        var reading = Balloon().Apply(T(Report));
        Assert.Equal(
            [
                new TelemetryDigitalReading(0, "Camra", "Click", false, false),
                new TelemetryDigitalReading(1, "Chut", "OPEN", true, false),
                new TelemetryDigitalReading(2, "Sun", "on", true, true),
                new TelemetryDigitalReading(3, "10m", "on", false, false),
                new TelemetryDigitalReading(4, "ATV", "hi", true, false),
                new TelemetryDigitalReading(5, null, null, false, true),
                new TelemetryDigitalReading(6, null, null, false, true),
                new TelemetryDigitalReading(7, null, null, true, false),
            ],
            reading.Digital
        );
    }

    [Fact]
    public void WithoutDefinitionsReadingsAreRawAndUnnamed()
    {
        var reading = TelemetryProfile.Empty.Apply(T(Report));
        Assert.Equal(new TelemetryAnalogReading(0, null, null, 199, 199, false), reading.Analog[0]);
        Assert.All(reading.Digital, d => Assert.Equal(d.Bit, d.IsOn));
        Assert.Null(reading.ProjectName);
    }

    [Fact]
    public void APartialEqnsScalesOnlyTheChannelsItCovers()
    {
        var profile = TelemetryProfile.Empty.With(D("N0QBF>APRS::N0QBF-11 :EQNS.0,0.1,0,0,2,0"));
        var reading = profile.Apply(T(Report));
        Assert.Equal([19.9, 0, 255, 73, 123], reading.Analog.Select(a => Math.Round(a.Value!.Value, 6)));
        Assert.Equal([true, true, false, false, false], reading.Analog.Select(a => a.IsScaled));
    }

    [Fact]
    public void TheLatestDefinitionOfEachKindWins()
    {
        var profile = Balloon().With(D("N0QBF>APRS::N0QBF-11 :PARM.Vbat"));
        var reading = profile.Apply(T(Report));
        Assert.Equal("Vbat", reading.Analog[0].Name);
        Assert.Null(reading.Analog[1].Name);
        Assert.Equal("Volts", reading.Analog[0].Unit);
    }

    [Theory]
    [InlineData("N0QBF>APRS::N0QBF-11 :EQNS.0,5.2")]
    [InlineData("N0QBF>APRS::N0QBF-11 :EQNS.a,b,c")]
    [InlineData("N0QBF>APRS::N0QBF-11 :BITS.1011")]
    public void ADefinitionThatCannotBeReadKeepsTheEarlierOne(string line)
    {
        var profile = Balloon();
        Assert.Equal(profile, profile.With(D(line)));
    }

    [Fact]
    public void EmptyNamesAndFieldsStayEmpty()
    {
        var profile = TelemetryProfile.Empty.With(D("N0QBF>APRS::N0QBF-11 :PARM.Vbat,,Temp"));
        var reading = profile.Apply(T("N0QBF-11>APRS:T#012,138,,042"));
        Assert.Equal(["Vbat", null, "Temp"], reading.Analog.Select(a => a.Name));
        Assert.Null(reading.Analog[1].Raw);
        Assert.Null(reading.Analog[1].Value);
        Assert.Empty(reading.Digital);
    }

    [Fact]
    public void CommentTelemetryUsesTheSameDefinitions()
    {
        // Base-91 comment telemetry (APRS 1.2): sequence 1, analog values 1 and 2.
        var position = Assert.IsType<PositionReport>(
            AprsPacket.Parse("N0QBF-11>APRS:!4903.50N/07201.75W>|!\"!\"!#|").Payload
        );
        var reading = Balloon().Apply(position.Telemetry!);
        Assert.Equal(["Battery", "Btemp"], reading.Analog.Select(a => a.Name));
        Assert.Equal(5.2, reading.Analog[0].Value!.Value, 6);
    }

    private static TelemetryAnalogReading Round(TelemetryAnalogReading a) =>
        a with
        {
            Value = Math.Round(a.Value!.Value, 6),
        };
}
