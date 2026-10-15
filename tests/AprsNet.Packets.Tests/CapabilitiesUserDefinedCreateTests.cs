using System.Text;

namespace AprsNet.Packets.Tests;

public class CapabilitiesUserDefinedCreateTests
{
    private static readonly PacketAddress _address = PacketAddress.Parse("N0CALL-10", "APZ123", []);

    private static string Information(AprsPayload payload) =>
        Encoding.Latin1.GetString(_address.Create(payload).Information.Span);

    private static void Rejects(AprsPayload payload, string reason)
    {
        var ex = Assert.Throws<ArgumentException>(() => _address.Create(payload));
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
    }

    // Capabilities

    [Fact]
    public void WritesAnIGateAsInApr101()
    {
        var packet = _address.Create(CapabilitiesReport.ForIGate(messageCount: 4, localStationCount: 23));

        Assert.Equal("<IGATE,MSG_CNT=4,LOC_CNT=23", Encoding.Latin1.GetString(packet.Information.Span));
        var decoded = (CapabilitiesReport)packet.Payload!;
        Assert.True(decoded.IsIGate);
        Assert.Equal(4, decoded.MessageCount);
        Assert.Equal(23, decoded.LocalStationCount);
    }

    [Fact]
    public void LeavesOutUnknownCounts()
    {
        Assert.Equal("<IGATE", Information(CapabilitiesReport.ForIGate(null, null)));
    }

    [Fact]
    public void RefusesNegativeCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CapabilitiesReport.ForIGate(-1, null));
    }

    [Fact]
    public void WritesAnyTokensInOrder()
    {
        var report = new CapabilitiesReport
        {
            Capabilities = [new("IGATE", null), new("DX", "41*N0CALL(12.5mi 241°)"), new("FMTP", null)],
        };

        var packet = _address.Create(report);

        Assert.Equal(report.Capabilities, ((CapabilitiesReport)packet.Payload!).Capabilities);
    }

    [Theory]
    [InlineData("A,B", null, "must be non-empty")]
    [InlineData("A=B", null, "must be non-empty")]
    [InlineData(" A", null, "must be non-empty")]
    [InlineData("A", "1,2", "must not contain ','")]
    [InlineData("A", " 1", "must not contain ','")]
    public void RefusesTokensAndValuesThatWouldReadDifferently(string token, string? value, string reason)
    {
        Rejects(new CapabilitiesReport { Capabilities = [new(token, value)] }, reason);
    }

    [Fact]
    public void RefusesAnEmptyList()
    {
        Rejects(new CapabilitiesReport(), "at least one capability");
    }

    [Fact]
    public void RebuildsAParsedReportByteForByte()
    {
        var parsed = AprsPacket.Parse("N0CALL-10>APRS:<IGATE ,MSG_CNT=4,,LOC_CNT= 23");

        var rebuilt = AprsPacket.Create(parsed.Source!, parsed.Destination!, parsed.Path!, parsed.Payload!);

        Assert.Equal(parsed.Information.ToArray(), rebuilt.Information.ToArray());
    }

    // User-defined data

    [Fact]
    public void WritesUserDefinedData()
    {
        var payload = new UserDefinedPayload
        {
            UserId = 'Q',
            UserPacketType = '1',
            Data = "qwerty",
        };

        var packet = _address.Create(payload);

        Assert.Equal("{Q1qwerty", Encoding.Latin1.GetString(packet.Information.Span));
        var decoded = (UserDefinedPayload)packet.Payload!;
        Assert.Equal('Q', decoded.UserId);
        Assert.Equal("qwerty", decoded.Data);
    }

    [Fact]
    public void WritesAnExperimentalPacket()
    {
        var payload = new UserDefinedPayload
        {
            UserId = '{',
            UserPacketType = 'X',
            Data = "test",
        };

        Assert.Equal("{{Xtest", Information(payload));
        Assert.True(payload.IsExperimental);
    }

    [Theory]
    [InlineData(' ', 'A')]
    [InlineData('Q', '\0')]
    [InlineData('é', 'A')]
    public void RefusesUnprintableIds(char userId, char packetType)
    {
        Rejects(new UserDefinedPayload { UserId = userId, UserPacketType = packetType }, "printable ASCII");
    }

    [Fact]
    public void RefusesControlCharactersInData()
    {
        Rejects(
            new UserDefinedPayload
            {
                UserId = 'Q',
                UserPacketType = '1',
                Data = "a\rb",
            },
            "control characters"
        );
    }
}
