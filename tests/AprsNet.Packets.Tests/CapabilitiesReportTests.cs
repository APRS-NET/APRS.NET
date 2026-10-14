namespace AprsNet.Packets.Tests;

public class CapabilitiesReportTests
{
    private static CapabilitiesReport Decode(string information)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);
        Assert.Equal(PacketType.Capabilities, packet.Type);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.Empty(packet.Issues);
        return Assert.IsType<CapabilitiesReport>(packet.Payload);
    }

    [Fact]
    public void DecodesTheIGateExample()
    {
        var report = Decode("<IGATE,MSG_CNT=43,LOC_CNT=14");

        Assert.Equal([new("IGATE", null), new("MSG_CNT", "43"), new("LOC_CNT", "14")], report.Capabilities);
        Assert.True(report.IsIGate);
        Assert.Equal(43, report.MessageCount);
        Assert.Equal(14, report.LocalStationCount);
    }

    [Fact]
    public void KeepsExtraCountersInOrder()
    {
        // From the corpus: newer IGate software adds counters the spec does not list.
        var report = Decode("<IGATE,MSG_CNT=0,PKT_CNT=0,DIR_CNT=1,LOC_CNT=1,RF_CNT=1,UPL_CNT=4");

        Assert.Equal(
            ["IGATE", "MSG_CNT", "PKT_CNT", "DIR_CNT", "LOC_CNT", "RF_CNT", "UPL_CNT"],
            report.Capabilities.Select(c => c.Key)
        );
        Assert.Equal("4", report.GetValue("UPL_CNT"));
        Assert.Equal("1", report.GetValue("rf_cnt")); // lookups ignore case
    }

    [Fact]
    public void TrimsSpacesAndSkipsEmptyEntries()
    {
        var report = Decode("<IGATE, MSG_CNT = 5 ,,LOC_CNT=,");

        Assert.Equal([new("IGATE", null), new("MSG_CNT", "5"), new("LOC_CNT", "")], report.Capabilities);
        Assert.Equal(5, report.MessageCount);
        Assert.Null(report.LocalStationCount); // an empty value is not a number
    }

    [Fact]
    public void AStationThatIsNotAnIGate()
    {
        var report = Decode("<DIGI");

        Assert.False(report.IsIGate);
        Assert.Null(report.MessageCount);
        Assert.Null(report.GetValue("MSG_CNT"));
    }

    [Fact]
    public void AnEmptyListIsValid()
    {
        Assert.Empty(Decode("<").Capabilities);
    }

    [Fact]
    public void ACountThatIsNotANumberIsNull()
    {
        Assert.Null(Decode("<IGATE,MSG_CNT=many").MessageCount);
    }
}
