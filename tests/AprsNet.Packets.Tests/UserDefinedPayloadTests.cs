namespace AprsNet.Packets.Tests;

public class UserDefinedPayloadTests
{
    private static UserDefinedPayload Decode(string information)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);
        Assert.Equal(PacketType.UserDefined, packet.Type);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.Empty(packet.Issues);
        return Assert.IsType<UserDefinedPayload>(packet.Payload);
    }

    [Fact]
    public void SplitsTheUserIdPacketTypeAndData()
    {
        var payload = Decode("{Q1qwerty");

        Assert.Equal('Q', payload.UserId);
        Assert.Equal('1', payload.UserPacketType);
        Assert.Equal("qwerty", payload.Data);
        Assert.Equal("qwerty"u8.ToArray(), payload.DataBytes.ToArray());
        Assert.False(payload.IsExperimental);
    }

    [Fact]
    public void DoubleBraceIsExperimental()
    {
        var payload = Decode("{{zasdfg");

        Assert.True(payload.IsExperimental);
        Assert.Equal('z', payload.UserPacketType);
        Assert.Equal("asdfg", payload.Data);
    }

    [Fact]
    public void DataMayBeEmpty()
    {
        Assert.Equal("", Decode("{{I").Data);
    }

    [Fact]
    public void KeepsBinaryDataByteForByte()
    {
        var packet = AprsPacket.Parse([.. "N0CALL>APRS:{{B"u8, 0x00, 0xFF, 0x7F]);

        var payload = Assert.IsType<UserDefinedPayload>(packet.Payload);
        Assert.Equal([0x00, 0xFF, 0x7F], payload.DataBytes.ToArray());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{Q")]
    public void TooShortIsMalformed(string information)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);

        Assert.Equal(PacketType.UserDefined, packet.Type);
        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Equal([new ParseIssue(ParseIssueCode.InvalidUserDefined, ParseIssueSeverity.Error, 12)], packet.Issues);
    }
}
