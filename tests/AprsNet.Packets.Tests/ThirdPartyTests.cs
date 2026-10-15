using System.Text;

namespace AprsNet.Packets.Tests;

public class ThirdPartyTests
{
    [Fact]
    public void ReadsTheCarriedPacketAsInApr101()
    {
        var packet = AprsPacket.Parse("N0CALL-10>APRS,TCPIP*:}W1AW>APRS,TCPIP,N0CALL-10*:>Hello from the gateway");

        Assert.Equal(PacketType.ThirdParty, packet.Type);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        var carried = ((ThirdPartyPayload)packet.Payload!).Packet;
        Assert.Equal("W1AW", carried.Source!.Value);
        Assert.Equal(["TCPIP", "N0CALL-10*"], carried.Path!.Select(p => p.ToString()));
        Assert.Equal("Hello from the gateway", ((StatusReport)carried.Payload!).Text);
    }

    [Fact]
    public void ReadsACarriedPositionAndMicE()
    {
        var position = AprsPacket.Parse("GATE>APRS:}N0CALL-9>APRS,RELAY*:!4903.50N/07201.75W>088/036");
        var micE = AprsPacket.Parse("GATE>APRS:}N0CALL-9>S32U6T:`(_fn\"Oj/]");

        Assert.IsType<PositionReport>(((ThirdPartyPayload)position.Payload!).Packet.Payload);
        Assert.IsType<MicEReport>(((ThirdPartyPayload)micE.Payload!).Packet.Payload);
    }

    [Fact]
    public void KeepsTheCarriedPacketsOwnIssues()
    {
        var packet = AprsPacket.Parse("GATE>APRS:}N0CALL>APRS:!9903.50N/07201.75W>");

        Assert.Equal(ParseOutcome.Parsed, packet.Outcome); // the wrapper is fine
        var carried = ((ThirdPartyPayload)packet.Payload!).Packet;
        Assert.Equal(ParseOutcome.Malformed, carried.Outcome);
        Assert.Contains(carried.Issues, i => i.Code == ParseIssueCode.InvalidLatitude);
    }

    [Theory]
    [InlineData("GATE>APRS:}")]
    [InlineData("GATE>APRS:}no header here")]
    public void ReportsATextWithoutAHeader(string line)
    {
        var packet = AprsPacket.Parse(line);

        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Contains(packet.Issues, i => i.Code == ParseIssueCode.InvalidThirdParty && i.Offset == 11);
    }

    [Fact]
    public void StopsUnwrappingDeeplyNestedPackets()
    {
        var packet = AprsPacket.Parse("A>B:}C>D:}E>F:}G>H:}I>J:>deep");

        var level = packet;
        for (var depth = 0; depth < 2; depth++)
        {
            level = ((ThirdPartyPayload)level.Payload!).Packet;
        }

        Assert.Equal(ParseOutcome.Malformed, ((ThirdPartyPayload)level.Payload!).Packet.Outcome);
        Assert.Contains(
            ((ThirdPartyPayload)level.Payload!).Packet.Issues,
            i => i.Code == ParseIssueCode.ThirdPartyTooDeep
        );
    }

    [Fact]
    public void CarriesAPacketLongerThanTheInformationLimit()
    {
        var text = new string('x', 500);
        var packet = AprsPacket.Parse($"GATE>APRS:}}N0CALL>APRS,TCPIP,GATE*:>{text}");

        var carried = ((ThirdPartyPayload)packet.Payload!).Packet;
        Assert.Equal(text, ((StatusReport)carried.Payload!).Text);
    }

    [Fact]
    public void WrapsAPacket()
    {
        var heard = AprsPacket.Parse("W1AW>APRS,TCPIP,N0CALL-10*:>Hello");
        var address = PacketAddress.Parse("N0CALL-10", "APZ123", []);

        var packet = address.Create(new ThirdPartyPayload(heard));

        Assert.Equal("}W1AW>APRS,TCPIP,N0CALL-10*:>Hello", Encoding.Latin1.GetString(packet.Information.Span));
        Assert.Equal(heard.Raw.ToArray(), ((ThirdPartyPayload)packet.Payload!).Packet.Raw.ToArray());
    }

    [Fact]
    public void RefusesToWrapTextWithoutAHeader()
    {
        var address = PacketAddress.Parse("N0CALL-10", "APZ123", []);

        var ex = Assert.Throws<ArgumentException>(() =>
            address.Create(new ThirdPartyPayload(AprsPacket.Parse("not a packet")))
        );
        Assert.Contains("SOURCE>DESTINATION", ex.Message, StringComparison.Ordinal);
    }
}
