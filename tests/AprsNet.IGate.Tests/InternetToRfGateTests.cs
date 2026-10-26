using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.IGate.Tests;

/// <summary>
/// The internet-to-RF rules from aprs-is.net, "IGate Details", one row each: which packets heard on APRS-IS a two-way
/// IGate sends on RF, and the third-party packet it builds. N0CALL-9 is heard on RF direct; W1AW writes from APRS-IS.
/// </summary>
public sealed class InternetToRfGateTests
{
    private static readonly PacketAddress _igate = PacketAddress.Parse("N0CALL-10", "APZNET", ["WIDE1-1"]);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly HeardStations _heard;

    public InternetToRfGateTests()
    {
        _heard = new HeardStations(new HeardStationsOptions { TimeProvider = _time });
        _heard.Record(AprsPacket.Parse("N0CALL-9>APRS,WIDE1-1:>on RF"));
        _heard.Record(AprsPacket.Parse("N3FAR>APRS,DIGI1,DIGI2,DIGI3*:>three hops away"));
        _heard.Record(AprsPacket.Parse("N2LOC>APRS:>also on RF"));
    }

    private InternetToRfGate Gate(int maxPerMinute = 6, int maxPerFiveMinutes = 10) =>
        new(
            _igate,
            _heard,
            new InternetToRfGateOptions
            {
                TimeProvider = _time,
                MaxPerMinute = maxPerMinute,
                MaxPerFiveMinutes = maxPerFiveMinutes,
            }
        );

    private static string? Sent(RfGateDecision decision) => decision.Packet?.ToString();

    [Theory]
    // A message, an ack and a rejection for a local station, wrapped as third-party traffic.
    [InlineData(
        "W1AW>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :Hello{01",
        "N0CALL-10>APZNET,WIDE1-1:}W1AW>APRS,TCPIP,N0CALL-10*::N0CALL-9 :Hello{01"
    )]
    [InlineData(
        "W1AW>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :ack01",
        "N0CALL-10>APZNET,WIDE1-1:}W1AW>APRS,TCPIP,N0CALL-10*::N0CALL-9 :ack01"
    )]
    [InlineData(
        "W1AW>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :rej01",
        "N0CALL-10>APZNET,WIDE1-1:}W1AW>APRS,TCPIP,N0CALL-10*::N0CALL-9 :rej01"
    )]
    // Through another IGate's path on APRS-IS: only the sender and destination are kept.
    [InlineData(
        "W1AW>APWW11,KJ4ERJ-15*,TCPIP*,qAS,KJ4ERJ-15::N0CALL-9 :Hi",
        "N0CALL-10>APZNET,WIDE1-1:}W1AW>APWW11,TCPIP,N0CALL-10*::N0CALL-9 :Hi"
    )]
    // A trailing CR can't go in a packet on RF.
    [InlineData(
        "W1AW>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :Hello\r",
        "N0CALL-10>APZNET,WIDE1-1:}W1AW>APRS,TCPIP,N0CALL-10*::N0CALL-9 :Hello"
    )]
    public void AMessageForALocalStationGoesOut(string fromInternet, string sent)
    {
        var decision = Gate().Gate(AprsPacket.Parse(fromInternet));

        Assert.True(decision.IsGated);
        Assert.Equal(sent, Sent(decision));
        Assert.True(Ax25Frame.FromAprsPacket(decision.Packet!).IsAprs);
    }

    [Theory]
    [InlineData("W1AW>APRS,TCPIP*,qAC,T2TEST::N9NONE   :Not heard", RfGateRefusal.NotLocal)]
    [InlineData("W1AW>APRS,TCPIP*,qAC,T2TEST::N3FAR    :Too far", RfGateRefusal.NotLocal)]
    [InlineData("N2LOC>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :Both on RF", RfGateRefusal.SenderOnRf)]
    [InlineData("W1AW>APRS,TCPXX*,qAX,T2TEST::N0CALL-9 :Unverified", RfGateRefusal.NoGate)]
    [InlineData("W1AW>APRS,NOGATE,qAC,T2TEST::N0CALL-9 :No gate", RfGateRefusal.NoGate)]
    [InlineData("W1AW>APRS,WIDE1-1,qAR,N0OTHER::N0CALL-9 :From RF", RfGateRefusal.FromRadio)]
    [InlineData("W1AW>APRS,TCPIP*,qAC,T2TEST::BLN1     :Bulletin", RfGateRefusal.NotForAStation)]
    [InlineData("W1AW>APRS,TCPIP*,qAC,T2TEST::NWS-WARN :Warning", RfGateRefusal.NotForAStation)]
    [InlineData("W1AW>APRS,TCPIP*,qAC,T2TEST:!4903.50N/07201.75W-Position", RfGateRefusal.NotForAStation)]
    [InlineData("W1AW>APRS,TCPIP*,qAC,T2TEST:>Status", RfGateRefusal.NotForAStation)]
    [InlineData("not a packet", RfGateRefusal.Unreadable)]
    public void EachRefusalHasAReason(string fromInternet, RfGateRefusal refusal)
    {
        var decision = Gate().Gate(AprsPacket.Parse(fromInternet));

        Assert.False(decision.IsGated);
        Assert.Equal(refusal, decision.Refusal);
    }

    [Fact]
    public void TheSendersNextPositionGoesOutOnceAfterAMessage()
    {
        var gate = Gate();
        const string Position = "W1AW>APRS,TCPIP*,qAC,T2TEST:!4903.50N/07201.75W-Writing";
        Assert.Equal(RfGateRefusal.NotForAStation, gate.Gate(AprsPacket.Parse(Position)).Refusal);

        Assert.True(gate.Gate(AprsPacket.Parse("W1AW>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :Hello{01")).IsGated);
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(
            "N0CALL-10>APZNET,WIDE1-1:}W1AW>APRS,TCPIP,N0CALL-10*:!4903.50N/07201.75W-Writing",
            Sent(gate.Gate(AprsPacket.Parse(Position)))
        );
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(RfGateRefusal.NotForAStation, gate.Gate(AprsPacket.Parse(Position)).Refusal);
    }

    [Fact]
    public void ThePositionIsOwedOnlyWithinTheWindow()
    {
        var gate = Gate();
        Assert.True(gate.Gate(AprsPacket.Parse("W1AW>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :Hello{01")).IsGated);

        _time.Advance(TimeSpan.FromMinutes(31));

        var position = AprsPacket.Parse("W1AW>APRS,TCPIP*,qAC,T2TEST:!4903.50N/07201.75W-Late");
        Assert.Equal(RfGateRefusal.NotForAStation, gate.Gate(position).Refusal);
    }

    [Fact]
    public void ACopyFromAnotherServerIsRefusedWithinTheWindow()
    {
        var gate = Gate();
        Assert.True(gate.Gate(AprsPacket.Parse("W1AW>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :Hello{01")).IsGated);

        var copy = AprsPacket.Parse("W1AW>APRS,TCPIP*,qAS,T2OTHER::N0CALL-9 :Hello{01");
        Assert.Equal(RfGateRefusal.Duplicate, gate.Gate(copy).Refusal);

        _time.Advance(TimeSpan.FromSeconds(31));
        Assert.True(gate.Gate(copy).IsGated); // a retry, after the window
    }

    [Fact]
    public void EverythingSentOnRfIsRateLimited()
    {
        var gate = Gate(maxPerMinute: 2, maxPerFiveMinutes: 3);
        RfGateDecision Message(int id) =>
            gate.Gate(AprsPacket.Parse($"W1AW>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :Message {id}{{{id}"));

        Assert.True(Message(1).IsGated);
        Assert.True(Message(2).IsGated);
        Assert.Equal(RfGateRefusal.RateLimited, Message(3).Refusal);

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(Message(4).IsGated);
        Assert.Equal(RfGateRefusal.RateLimited, Message(5).Refusal); // 3 in five minutes

        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.True(Message(6).IsGated);
    }

    [Fact]
    public void AStationHeardTooLongAgoIsNoLongerLocal()
    {
        var gate = Gate();
        _time.Advance(HeardStationsOptions.DefaultLocalWindow + TimeSpan.FromMinutes(1));

        var message = AprsPacket.Parse("W1AW>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :Hello{01");
        Assert.Equal(RfGateRefusal.NotLocal, gate.Gate(message).Refusal);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(6, 5)]
    public void UnusableSettingsAreRefused(int perMinute, int perFiveMinutes)
    {
        Assert.ThrowsAny<ArgumentException>(() => Gate(perMinute, perFiveMinutes));
    }
}
