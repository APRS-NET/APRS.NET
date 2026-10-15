using System.Text;

namespace AprsNet.Packets.Tests;

public class CommentTelemetryTests
{
    private static readonly PacketAddress _address = PacketAddress.Parse("N0CALL", "APZ123", []);

    private static PositionedPayload Position(string line) => (PositionedPayload)AprsPacket.Parse(line).Payload!;

    // The vectors from the base-91 comment telemetry specification.

    [Fact]
    public void ReadsTheSequenceAndOneChannel()
    {
        var telemetry = Position("N0CALL>APRS:!6304.03NN02739.63E#|ss11|").Telemetry!;

        Assert.Equal(7544, telemetry.SequenceNumber);
        Assert.Equal([1472], telemetry.Analog);
        Assert.Empty(telemetry.Digital);
    }

    [Fact]
    public void ReadsThreeChannels()
    {
        var telemetry = Position("N0CALL>APRS:!6304.03NN02739.63E#|ss112233|").Telemetry!;

        Assert.Equal([1472, 1564, 1656], telemetry.Analog);
    }

    [Fact]
    public void ReadsFiveChannelsAndTheDigitalBits()
    {
        var telemetry = Position("N0CALL>APRS:!6304.03NN02739.63E#|ss1122334455!\"|").Telemetry!;

        Assert.Equal([1472, 1564, 1656, 1748, 1840], telemetry.Analog);
        Assert.Equal([true, false, false, false, false, false, false, false], telemetry.Digital);
    }

    [Fact]
    public void ReadsTelemetryAfterACompressedPositionAndLeavesItOutOfTheComment()
    {
        var position = Position("N0CALL>APRS,qAR,IGATE:!/0%3RTh<6>dS_http://aprs.fi/|\"p%T'.ag|");

        Assert.Equal("http://aprs.fi/", position.Comment);
        Assert.Equal(3, position.Telemetry!.Analog.Count);
    }

    [Fact]
    public void ReadsTelemetryAfterAPhgExtension()
    {
        var position = Position("N0CALL>APRS,qAR,IGATE:!6304.03NN02739.63E#PHG26303/Siilinjarvi|\"p%T'.agff|");

        Assert.Equal("3/Siilinjarvi", position.Comment); // PHG2630, then the comment
        Assert.Equal(4, position.Telemetry!.Analog.Count);
        Assert.NotNull(position.PowerHeightGain);
    }

    [Fact]
    public void ReadsTelemetryBeforeTheDaoAndTheMicEDeviceCode()
    {
        var report = (MicEReport)AprsPacket.Parse("N0CALL>S32U6T,qAR,IGATE:`pZ3l-B]/'\"6{}|!9'X$u|!wr8!|3").Payload!;

        Assert.Equal(2, report.Telemetry!.Analog.Count);
        Assert.Equal('w', report.Datum); // !wr8!: WGS84, base-91 extra digits
        Assert.DoesNotContain("|", report.Comment, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("|ss1|")] // odd length
    [InlineData("|ss|")] // a sequence without a channel
    [InlineData("|ss11223344556677|")] // too long
    [InlineData("|ss1 |")] // a space is not base-91
    public void LeavesBlocksThatAreNotTelemetryInTheComment(string block)
    {
        var position = Position($"N0CALL>APRS:!6304.03NN02739.63E#Hello{block}");

        Assert.Null(position.Telemetry);
        Assert.Equal("Hello" + block, position.Comment);
    }

    [Fact]
    public void TakesTheLastBlock()
    {
        var position = Position("N0CALL>APRS:!6304.03NN02739.63E#|!!!!| and |ss11|");

        Assert.Equal(7544, position.Telemetry!.SequenceNumber);
        Assert.Equal("|!!!!| and ", position.Comment);
    }

    // Encoding

    [Fact]
    public void WritesTelemetryAfterTheComment()
    {
        var report = new PositionReport
        {
            Position = new AprsPosition(63 + (4.03 / 60), 27 + (39.63 / 60)),
            Symbol = new AprsSymbol('N', '#'),
            Comment = "Siilinjarvi",
            Telemetry = new TelemetryReport
            {
                SequenceNumber = 7544,
                Analog = [1472, 1564, 1656, 1748, 1840],
                Digital = [true, false, false, false, false, false, false, false],
            },
        };

        var packet = _address.Create(report);

        Assert.Equal(
            "!6304.03NN02739.63E#Siilinjarvi|ss1122334455!\"|",
            Encoding.Latin1.GetString(packet.Information.Span)
        );
        Assert.Equal("Siilinjarvi", ((PositionReport)packet.Payload!).Comment);
    }

    [Fact]
    public void WritesTelemetryOnAMicEReport()
    {
        var report = new MicEReport
        {
            Position = new AprsPosition(33.5, -112.1),
            Symbol = new AprsSymbol('/', '>'),
            Message = MicEMessage.InService,
            Comment = "Mobile",
            Telemetry = new TelemetryReport { SequenceNumber = 0, Analog = [0] },
        };

        var decoded = (MicEReport)_address.Create(report).Payload!;

        Assert.Equal("Mobile", decoded.Comment);
        Assert.Equal([0], decoded.Telemetry!.Analog);
    }

    [Theory]
    [InlineData(8281)]
    [InlineData(-1)]
    public void RefusesValuesOutsideTheRange(double value)
    {
        var report = new PositionReport
        {
            Position = new AprsPosition(10, 10),
            Symbol = new AprsSymbol('/', '-'),
            Telemetry = new TelemetryReport { SequenceNumber = 1, Analog = [value] },
        };

        var ex = Assert.Throws<ArgumentException>(() => _address.Create(report));
        Assert.Contains("0 to 8280", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesACommentThatWouldReadAsTelemetry()
    {
        var report = new PositionReport
        {
            Position = new AprsPosition(10, 10),
            Symbol = new AprsSymbol('/', '-'),
            Comment = "Values |ss11|",
        };

        var ex = Assert.Throws<ArgumentException>(() => _address.Create(report));
        Assert.Contains("read as telemetry", ex.Message, StringComparison.Ordinal);
    }

    private static void Rejects(TelemetryReport telemetry, string reason)
    {
        var report = new PositionReport
        {
            Position = new AprsPosition(10, 10),
            Symbol = new AprsSymbol('/', '-'),
            Telemetry = telemetry,
        };

        var ex = Assert.Throws<ArgumentException>(() => _address.Create(report));
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TakesTheSequenceFromItsTextToo()
    {
        var report = new PositionReport
        {
            Position = new AprsPosition(10, 10),
            Symbol = new AprsSymbol('/', '-'),
            Telemetry = new TelemetryReport { Sequence = "12", Analog = [5] },
        };

        Assert.Equal(12, ((PositionReport)_address.Create(report).Payload!).Telemetry!.SequenceNumber);
    }

    [Fact]
    public void RefusesTelemetryWithoutASequence()
    {
        Rejects(new TelemetryReport { Analog = [1] }, "needs a SequenceNumber");
    }

    [Fact]
    public void RefusesSixAnalogValues()
    {
        Rejects(new TelemetryReport { SequenceNumber = 1, Analog = [1, 2, 3, 4, 5, 6] }, "one to five analog values");
    }

    [Fact]
    public void RefusesTheWrongNumberOfDigitalChannels()
    {
        Rejects(
            new TelemetryReport
            {
                SequenceNumber = 1,
                Analog = [1, 2, 3, 4, 5],
                Digital = [true],
            },
            "all eight digital channels or none"
        );
    }

    [Fact]
    public void RefusesDigitalChannelsWithoutAllFiveAnalogValues()
    {
        Rejects(
            new TelemetryReport
            {
                SequenceNumber = 1,
                Analog = [1],
                Digital = [.. Enumerable.Repeat(true, 8)],
            },
            "follow all five analog values"
        );
    }

    [Fact]
    public void RefusesACommentOfItsOwn()
    {
        Rejects(
            new TelemetryReport
            {
                SequenceNumber = 1,
                Analog = [1],
                Comment = "x",
            },
            "no comment of its own"
        );
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(null)]
    public void RefusesFractionalOrMissingValues(double? value)
    {
        Rejects(new TelemetryReport { SequenceNumber = 1, Analog = [value] }, "whole numbers 0 to 8280, none missing");
    }
}
