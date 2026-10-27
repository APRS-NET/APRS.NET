using AprsNet.Packets;

namespace AprsNet.Messaging.Tests;

/// <summary>
/// ANSRVR's group traffic as captured on APRS-IS on 2026-10-18 and 2026-10-27 (#272, #281): relays come from the
/// member who posted with no message ID, notices from ANSRVR with one.
/// </summary>
public sealed class AnsrvrMessageTests
{
    private static IncomingMessage Received(string line)
    {
        var packet = AprsPacket.Parse(line);
        if (packet.Payload is ThirdPartyPayload { Packet: { } inner })
        {
            packet = inner;
        }

        var message = Assert.IsType<MessagePayload>(packet.Payload);
        return new IncomingMessage(
            packet.Source!,
            message.Text,
            message.MessageId,
            DateTimeOffset.UnixEpoch,
            "aprs-is",
            packet
        );
    }

    [Theory]
    [InlineData(
        "N1KXJ-14>APWW11,qAO,KJ4ERJ-15::N1KXJ-10 :N:NEDR Pretty quiet ot here...",
        "N1KXJ-14",
        "NEDR",
        "Pretty quiet ot here..."
    )]
    [InlineData(
        "9M2PJU>APWW11,qAO,KJ4ERJ-15::K7XYG-7  :N:HOTG PSE JOIN #APRSMY Net https://aprsmy.hamradio.my",
        "9M2PJU",
        "HOTG",
        "PSE JOIN #APRSMY Net https://aprsmy.hamradio.my"
    )]
    // Gated to RF by a two-way IGate: the same text, another path.
    [InlineData(
        "N0CALL-10>APZNET,WIDE1-1:}N1KXJ-14>APWW11,TCPIP,N0CALL-10*::N0CALL-9 :N:NEDR Hi",
        "N1KXJ-14",
        "NEDR",
        "Hi"
    )]
    public void ARelayIsAMembersPost(string line, string from, string group, string text)
    {
        var received = Received(line);

        Assert.True(AnsrvrMessage.TryParse(received, out var relay));

        Assert.Equal(AnsrvrMessageKind.Relay, relay.Kind);
        Assert.Equal(from, relay.From.Value);
        Assert.Equal(group, relay.Group);
        Assert.Equal(text, relay.Text);
    }

    [Theory]
    [InlineData(
        "ANSRVR>APWW11,KJ4ERJ-15*,TCPIP*,qAS,KJ4ERJ-15::N1KXJ-14 :N:NEDR 2 Messages Sent{HV}",
        "NEDR",
        "2 Messages Sent"
    )]
    [InlineData(
        "ANSRVR>APWW11,KJ4ERJ-15*,TCPIP*,qAS,KJ4ERJ-15::NZ0P-3   :N:C4FM Now Monitoring with 2 Members{AB}",
        "C4FM",
        "Now Monitoring with 2 Members"
    )]
    [InlineData(
        "ANSRVR>APWW11,KJ4ERJ-15*,TCPIP*,qAS,KJ4ERJ-15::NZ0P-3   :N:APRSNETTEST2 Send D APRSNETTEST2 <Description>{BI}",
        "APRSNETTEST2",
        "Send D APRSNETTEST2 <Description>"
    )]
    [InlineData(
        "ANSRVR>APWW11,KJ4ERJ-15*,TCPIP*,qAS,KJ4ERJ-15::NZ0P-3   :N:HOTG Removed From Group, Left OwnerLess!{BK}",
        "HOTG",
        "Removed From Group, Left OwnerLess!"
    )]
    public void ANoticeIsAnsrvrsOwn(string line, string group, string text)
    {
        Assert.True(AnsrvrMessage.TryParse(Received(line), out var notice));

        Assert.Equal(AnsrvrMessageKind.Notice, notice.Kind);
        Assert.Equal(AnsrvrCommand.Ansrvr, notice.From);
        Assert.Equal(group, notice.Group);
        Assert.Equal(text, notice.Text);
    }

    [Theory]
    // A person's message that starts with N: has a message ID.
    [InlineData("W1AW>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :N:NEDR not a relay{01")]
    // ANSRVR's replies with no group.
    [InlineData("ANSRVR>APWW11,KJ4ERJ-15*,TCPIP*,qAS,KJ4ERJ-15::NZ0P-3   :No Memberships{BL}")]
    [InlineData("ANSRVR>APWW11,KJ4ERJ-15*,TCPIP*,qAS,KJ4ERJ-15::NZ0P-3   :Duplicate (retry?) request suppressed")]
    [InlineData("ANSRVR>APWW11,KJ4ERJ-15*,TCPIP*,qAS,KJ4ERJ-15::NZ0P-3   :N: no group{BM}")]
    // A post to ANSRVR, and ordinary messages.
    [InlineData("N1KXJ-14>APSTKA,TCPIP*,qAS,N1KXJ-W3::ANSRVR   :CQ NEDR Pretty quiet{9W")]
    [InlineData("W1AW>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :Net at 7pm")]
    [InlineData("W1AW>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :n:NEDR lower case")]
    public void OtherMessagesAreNotGroupTraffic(string line)
    {
        Assert.False(AnsrvrMessage.TryParse(Received(line), out var ansrvr));
        Assert.Null(ansrvr);
    }

    [Fact]
    public void AGroupWithNoTextHasEmptyText()
    {
        Assert.True(AnsrvrMessage.TryParse(Callsign.Parse("N1KXJ-14"), "N:NEDR", null, out var relay));

        Assert.Equal("NEDR", relay.Group);
        Assert.Equal("", relay.Text);
        Assert.Equal("N1KXJ-14 N:NEDR", relay.ToString());
    }

    [Fact]
    public void ANoticeIsFromAnsrvrInAnyCase()
    {
        Assert.True(AnsrvrMessage.TryParse(Callsign.Parse("ansrvr"), "N:NEDR 2 Messages Sent", "HV", out var notice));
        Assert.Equal(AnsrvrMessageKind.Notice, notice.Kind);
    }

    [Fact]
    public void ARelayIsNotAMessageFromAnsrvrsOtherStations()
    {
        // An SSID of ANSRVR's is another station; with a message ID it's a person's message, without one a relay.
        Assert.False(AnsrvrMessage.TryParse(Callsign.Parse("ANSRVR-1"), "N:NEDR 2 Messages Sent", "HV", out _));
        Assert.True(AnsrvrMessage.TryParse(Callsign.Parse("ANSRVR-1"), "N:NEDR hi", null, out var relay));
        Assert.Equal(AnsrvrMessageKind.Relay, relay.Kind);
    }
}
