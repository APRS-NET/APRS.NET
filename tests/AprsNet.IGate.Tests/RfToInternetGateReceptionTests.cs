using AprsNet.Packets;
using AprsNet.Packets.Is2;

namespace AprsNet.IGate.Tests;

/// <summary>Reception metadata rides along with the gate's decision, and never changes it.</summary>
public sealed class RfToInternetGateReceptionTests
{
    private static readonly ReceptionMetadata _ax25 = new()
    {
        Modem = ReceptionModem.Ax25,
        BitRate = 1200,
        FrequencyHz = 144390000,
    };

    private static RfToInternetGate Gate() => new(Callsign.Parse("N0CALL-10"));

    [Fact]
    public void AGatedPacketCarriesItsMetadata()
    {
        var decision = Gate().Gate(AprsPacket.Parse("N0CALL-9>APRS,WIDE1-1:>Status"), _ax25);

        Assert.True(decision.IsGated);
        Assert.Same(_ax25, decision.Reception);
        Assert.Equal("N0CALL-9>APRS,WIDE1-1,qAO,N0CALL-10:>Status", decision.Packet.ToString());
    }

    [Fact]
    public void TheGateWithoutMetadataGivesNone()
    {
        var packet = AprsPacket.Parse("N0CALL-9>APRS:>Status");

        Assert.Null(Gate().Gate(packet).Reception);
        Assert.Null(Gate().Gate(packet, null).Reception);
    }

    [Theory]
    [InlineData("N0CALL-9>APRS,WIDE1-1:>Status")]
    [InlineData("N0CALL-9>APRS,TCPIP*:>From the internet")]
    [InlineData("N0CALL-9>APRS,NOGATE:>Keep me off")]
    [InlineData("N0CALL-9>APRS,qAR,W1AW:>Already gated")]
    [InlineData("N0CALL-9>APRS:?APRS?")]
    [InlineData("N0CALL-9>APRS:")]
    public void MetadataNeverChangesTheDecision(string heard)
    {
        var packet = AprsPacket.Parse(heard);

        var without = Gate().Gate(packet);
        var with = Gate().Gate(packet, _ax25);

        Assert.Equal(without.Refusal, with.Refusal);
        Assert.Equal(without.Packet?.ToString(), with.Packet?.ToString());
    }

    [Fact]
    public void MetadataDoesNotLetADuplicateThrough()
    {
        var gate = new RfToInternetGate(
            Callsign.Parse("N0CALL-10"),
            new RfToInternetGateOptions { DuplicateWindow = TimeSpan.FromSeconds(30) }
        );
        var packet = AprsPacket.Parse("N0CALL-9>APRS:>Status");

        Assert.True(gate.Gate(packet, _ax25).IsGated);
        Assert.Equal(GateRefusal.Duplicate, gate.Gate(packet, _ax25 with { RssiDbm = -60 }).Refusal);
    }
}
