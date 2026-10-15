using System.Text;

namespace AprsNet.Packets.Tests;

public class TelemetryCreateTests
{
    private static readonly PacketAddress _address = PacketAddress.Parse("N0QBF-11", "APZ123", []);

    private static string Information(AprsPayload payload) =>
        Encoding.Latin1.GetString(_address.Create(payload).Information.Span);

    private static void Rejects(AprsPayload payload, string reason)
    {
        var ex = Assert.Throws<ArgumentException>(() => _address.Create(payload));
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
    }

    private static TelemetryReport Report(params double?[] analog) => new() { SequenceNumber = 5, Analog = analog };

    // Reports

    [Fact]
    public void WritesAReportAsInApr101()
    {
        var report = new TelemetryReport
        {
            SequenceNumber = 5,
            Analog = [199, 0, 255, 73, 123],
            Digital = [false, true, true, false, true, false, false, true],
        };

        var packet = _address.Create(report);

        Assert.Equal("T#005,199,000,255,073,123,01101001", Encoding.Latin1.GetString(packet.Information.Span));
        var decoded = (TelemetryReport)packet.Payload!;
        Assert.Equal(5, decoded.SequenceNumber);
        Assert.Equal(report.Digital, decoded.Digital);
    }

    [Fact]
    public void WritesACommentAfterTheDigitalChannels()
    {
        var report = new TelemetryReport
        {
            SequenceNumber = 12,
            Analog = [1, 2, 3, 4, 5],
            Digital = [true, false, false, false, false, false, false, false],
            Comment = "Solar shed",
        };

        Assert.Equal("T#012,001,002,003,004,005,10000000Solar shed", Information(report));
    }

    [Fact]
    public void WritesFewerAnalogValuesWithoutDigitalChannels()
    {
        Assert.Equal("T#005,138,042", Information(Report(138, 42)));
    }

    [Fact]
    public void WritesAMissingAnalogValueAsAnEmptyField()
    {
        var packet = _address.Create(Report(1, null, 3));

        Assert.Equal("T#005,001,,003", Encoding.Latin1.GetString(packet.Information.Span));
        Assert.Equal([1, null, 3], ((TelemetryReport)packet.Payload!).Analog);
    }

    [Fact]
    public void AcceptsTheApr12RangeUpTo999()
    {
        Assert.Equal("T#005,999", Information(Report(999)));
    }

    [Fact]
    public void WritesAMicSequence()
    {
        var report = new TelemetryReport { Sequence = "MIC", Analog = [10, 20] };

        Assert.Equal("T#MIC,010,020", Information(report));
    }

    [Theory]
    [InlineData(13.8)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void RefusesAnalogValuesOutsideWholeNumbersTo999(double value)
    {
        Rejects(Report(value), "whole numbers 0 to 999");
    }

    [Fact]
    public void RefusesDigitalChannelsWithoutAllFiveAnalogValues()
    {
        var report = new TelemetryReport
        {
            SequenceNumber = 5,
            Analog = [1, 2],
            Digital = [true, true, true, true, true, true, true, true],
        };

        Rejects(report, "follow all five analog values");
    }

    [Fact]
    public void RefusesACommentWithoutDigitalChannels()
    {
        var report = new TelemetryReport
        {
            SequenceNumber = 5,
            Analog = [1, 2, 3, 4, 5],
            Comment = "x",
        };

        Rejects(report, "comment follows the eight digital channels");
    }

    [Fact]
    public void RefusesTheWrongNumberOfDigitalChannels()
    {
        var report = new TelemetryReport
        {
            SequenceNumber = 5,
            Analog = [1, 2, 3, 4, 5],
            Digital = [true],
        };

        Rejects(report, "all eight digital channels or none");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    public void RefusesASequenceNumberOutOfRange(int sequence)
    {
        Rejects(new TelemetryReport { SequenceNumber = sequence, Analog = [1] }, "0 to 999");
    }

    [Fact]
    public void RefusesAReportWithoutASequence()
    {
        Rejects(new TelemetryReport { Analog = [1] }, "needs a SequenceNumber");
    }

    [Fact]
    public void RefusesSixAnalogValues()
    {
        Rejects(Report(1, 2, 3, 4, 5, 6), "one to five analog values");
    }

    [Fact]
    public void RebuildsAParsedReportByteForByte()
    {
        // Decimals and a short sequence, as many stations send: kept as they arrived.
        var parsed = AprsPacket.Parse("N0QBF-11>APRS:T#7,13.8,-2.5,0,1,2,00000000 comment");

        var rebuilt = AprsPacket.Create(parsed.Source!, parsed.Destination!, parsed.Path!, parsed.Payload!);

        Assert.Equal(parsed.Information.ToArray(), rebuilt.Information.ToArray());
    }

    // Definitions

    [Fact]
    public void WritesParameterNamesAsAMessageToTheStation()
    {
        var definition = new TelemetryDefinition
        {
            Addressee = "N0QBF-11",
            Kind = TelemetryDefinitionKind.ParameterNames,
            Labels = ["Battery", "Btemp", "ATemp", "Pres", "Alt", "Camra", "Chute", "Sun", "10m", "ATV"],
        };

        var packet = _address.Create(definition);

        Assert.Equal(
            ":N0QBF-11 :PARM.Battery,Btemp,ATemp,Pres,Alt,Camra,Chute,Sun,10m,ATV",
            Encoding.Latin1.GetString(packet.Information.Span)
        );
        Assert.Equal(definition.Labels, ((TelemetryDefinition)packet.Payload!).Labels);
    }

    [Fact]
    public void WritesUnits()
    {
        var definition = new TelemetryDefinition
        {
            Addressee = "N0QBF-11",
            Kind = TelemetryDefinitionKind.Units,
            Labels = ["v/100", "deg.F", "deg.F", "Mbar", "Kft", "Click", "OPEN", "on", "on", "hi"],
        };

        Assert.Equal(":N0QBF-11 :UNIT.v/100,deg.F,deg.F,Mbar,Kft,Click,OPEN,on,on,hi", Information(definition));
    }

    [Fact]
    public void WritesEquationsAsInApr101()
    {
        var definition = new TelemetryDefinition
        {
            Addressee = "N0QBF-11",
            Kind = TelemetryDefinitionKind.Equations,
            Equations =
            [
                new TelemetryEquation(0, 5.2, 0),
                new TelemetryEquation(0, 0.53, -32),
                new TelemetryEquation(3, 4.39, 49),
                new TelemetryEquation(-32, 3, 18),
                new TelemetryEquation(1, 2, 3),
            ],
        };

        var packet = _address.Create(definition);

        Assert.Equal(
            ":N0QBF-11 :EQNS.0,5.2,0,0,0.53,-32,3,4.39,49,-32,3,18,1,2,3",
            Encoding.Latin1.GetString(packet.Information.Span)
        );
        Assert.Equal(definition.Equations, ((TelemetryDefinition)packet.Payload!).Equations);
    }

    [Fact]
    public void WritesTheBitSenseAndProjectName()
    {
        var definition = new TelemetryDefinition
        {
            Addressee = "N0QBF-11",
            Kind = TelemetryDefinitionKind.BitSense,
            BitSense = "10110000",
            ProjectName = "N0QBF's Big Balloon",
        };

        Assert.Equal(":N0QBF-11 :BITS.10110000,N0QBF's Big Balloon", Information(definition));
    }

    [Fact]
    public void RefusesADefinitionOverTheMessageLimit()
    {
        var definition = new TelemetryDefinition
        {
            Addressee = "N0QBF-11",
            Kind = TelemetryDefinitionKind.ParameterNames,
            Labels = [.. Enumerable.Repeat("Temperature", 6)],
        };

        Rejects(definition, "at most 67 characters");
    }

    [Fact]
    public void RefusesALabelWithAComma()
    {
        var definition = new TelemetryDefinition
        {
            Addressee = "N0QBF-11",
            Kind = TelemetryDefinitionKind.Units,
            Labels = ["deg,F"],
        };

        Rejects(definition, "must not contain commas");
    }

    [Fact]
    public void RefusesFieldsAnotherKindCarries()
    {
        var definition = new TelemetryDefinition
        {
            Addressee = "N0QBF-11",
            Kind = TelemetryDefinitionKind.ParameterNames,
            Labels = ["Battery"],
            BitSense = "11111111",
        };

        Rejects(definition, "carries labels only");
    }

    [Fact]
    public void RefusesABadBitSense()
    {
        var definition = new TelemetryDefinition
        {
            Addressee = "N0QBF-11",
            Kind = TelemetryDefinitionKind.BitSense,
            BitSense = "1011",
        };

        Rejects(definition, "eight 0 or 1 characters");
    }

    [Fact]
    public void RefusesAnAddresseeThatIsNotACallsign()
    {
        Rejects(
            new TelemetryDefinition
            {
                Addressee = "N0 QBF",
                Kind = TelemetryDefinitionKind.ParameterNames,
                Labels = ["Battery"],
            },
            "telemetry station's callsign"
        );
    }

    [Fact]
    public void RebuildsAParsedDefinitionByteForByte()
    {
        var parsed = AprsPacket.Parse("N0QBF-11>APRS::N0QBF-11 :EQNS.0,5.20,0,0,.53,-32,3,4.39,49,-32,3,18,1,2,3");

        var rebuilt = AprsPacket.Create(parsed.Source!, parsed.Destination!, parsed.Path!, parsed.Payload!);

        Assert.Equal(parsed.Information.ToArray(), rebuilt.Information.ToArray());
    }
}
