using System.Diagnostics.Metrics;
using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.IGate.Tests;

/// <summary>
/// The RF-to-internet rules from aprs-is.net, "IGate Details", one row each, with what the IGate sends for the packets
/// it gates.
/// </summary>
public sealed class RfToInternetGateTests
{
    private static readonly Callsign _igate = Callsign.Parse("N0CALL-10");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

    private RfToInternetGate Gate(bool receiveOnly = true) =>
        new(_igate, new RfToInternetGateOptions { ReceiveOnly = receiveOnly, TimeProvider = _time });

    [Theory]
    // Heard direct, through digipeaters, and with unused hops: the path is kept as heard.
    [InlineData("N0CALL>APRS:!4903.50N/07201.75W-Test", "N0CALL>APRS,qAO,N0CALL-10:!4903.50N/07201.75W-Test")]
    [InlineData("N0CALL-9>APRS,WIDE1-1,WIDE2-2:>Status", "N0CALL-9>APRS,WIDE1-1,WIDE2-2,qAO,N0CALL-10:>Status")]
    [InlineData(
        "N0CALL-9>APRS,DIGI1,WIDE1*,WIDE2-1:>Status",
        "N0CALL-9>APRS,DIGI1,WIDE1*,WIDE2-1,qAO,N0CALL-10:>Status"
    )]
    // Messages and directed queries are gated; only general queries are not.
    [InlineData("N8UR>APRS::KH2Z     :?APRSD", "N8UR>APRS,qAO,N0CALL-10::KH2Z     :?APRSD")]
    [InlineData("N8UR>APRS::KH2Z     :Hello{1", "N8UR>APRS,qAO,N0CALL-10::KH2Z     :Hello{1")]
    // A payload with errors is still gated as heard; only the header has to be readable.
    [InlineData("N0CALL>APRS:!99X3.50N/07201.75W-", "N0CALL>APRS,qAO,N0CALL-10:!99X3.50N/07201.75W-")]
    // A type this library doesn't decode goes up too.
    [InlineData("N0CALL>APRS:%DF data", "N0CALL>APRS,qAO,N0CALL-10:%DF data")]
    // Third-party traffic from another RF network goes up as the packet it carries, without the RF header.
    [InlineData(
        "N0GW>APRS,WIDE2-1:}N0CALL-5>APRS,N0GW*:>From the HF side",
        "N0CALL-5>APRS,N0GW*,qAO,N0CALL-10:>From the HF side"
    )]
    // A trailing CR, as many TNCs send, can't go on an APRS-IS line; neither can what follows one.
    [InlineData("N0CALL>APRS:>Status\r", "N0CALL>APRS,qAO,N0CALL-10:>Status")]
    [InlineData("N0CALL>APRS:>Status\rjunk", "N0CALL>APRS,qAO,N0CALL-10:>Status")]
    [InlineData("N0CALL>APRS:>Status\0junk", "N0CALL>APRS,qAO,N0CALL-10:>Status")]
    public void GatesPacketsWithTheQConstructAfterTheirPath(string heard, string sent)
    {
        var decision = Gate().Gate(AprsPacket.Parse(heard));

        Assert.True(decision.IsGated);
        Assert.Null(decision.Refusal);
        Assert.Equal(sent, decision.Packet.ToString());
        Assert.Equal(heard.TrimEnd('\r'), decision.Heard.ToString()); // parsing drops a trailing CR
    }

    [Theory]
    [InlineData("N0CALL>APRS,TCPIP*:>Status", GateRefusal.FromInternet)]
    [InlineData("N0CALL>APRS,TCPXX*:>Status", GateRefusal.FromInternet)]
    [InlineData("N0CALL>APRS,tcpip*:>Status", GateRefusal.FromInternet)]
    [InlineData("N0CALL>APRS,NOGATE:>Status", GateRefusal.NoGate)]
    [InlineData("N0CALL>APRS,WIDE1-1,RFONLY:>Status", GateRefusal.NoGate)]
    // A two-way IGate sends what it heard on APRS-IS to RF as third-party traffic with TCPIP in the inner header.
    [InlineData("N0GW>APRS,WIDE2-1:}N0CALL-5>APRS,TCPIP,N0GW*::N0CALL   :Hi{1", GateRefusal.FromInternet)]
    [InlineData("N0GW>APRS:}N0CALL-5>APRS,TCPXX,N0GW*:>Status", GateRefusal.FromInternet)]
    [InlineData("N0GW>APRS:}N0CALL-5>APRS,NOGATE,N0GW*:>Status", GateRefusal.NoGate)]
    [InlineData("N0GW>APRS,RFONLY:}N0CALL-5>APRS,N0GW*:>Status", GateRefusal.NoGate)]
    [InlineData("N0GW>APRS:}N0CALL-5>APRS,qAR,N0GW:>Status", GateRefusal.AlreadyGated)]
    [InlineData("N0GW>APRS:}no header", GateRefusal.Unreadable)]
    [InlineData("N0CALL>APRS:?APRS?", GateRefusal.GeneralQuery)]
    [InlineData("N0CALL>APRS:?IGATE?", GateRefusal.GeneralQuery)]
    [InlineData("N0CALL>APRS:?WX? 34.02,-117.15,0200", GateRefusal.GeneralQuery)]
    [InlineData("N0GW>APRS:}N0CALL-5>APRS,N0GW*:?APRS?", GateRefusal.GeneralQuery)]
    [InlineData("N0CALL>APRS,qAR,N0GW:>Status", GateRefusal.AlreadyGated)]
    [InlineData("N0CALL>APRS:", GateRefusal.Empty)]
    [InlineData("N0CALL>APRS:\r", GateRefusal.Empty)]
    [InlineData("no header at all", GateRefusal.Unreadable)]
    [InlineData(">APRS:>Status", GateRefusal.Unreadable)]
    [InlineData("N0CALL>APRS,,WIDE1-1:>Status", GateRefusal.Unreadable)]
    public void RefusesWhatTheRulesKeepOffAprsIs(string heard, GateRefusal refusal)
    {
        var decision = Gate().Gate(AprsPacket.Parse(heard));

        Assert.False(decision.IsGated);
        Assert.Null(decision.Packet);
        Assert.Equal(refusal, decision.Refusal);
    }

    [Fact]
    public void ATwoWayIGateAddsQar()
    {
        var gate = Gate(receiveOnly: false);

        var decision = gate.Gate(AprsPacket.Parse("N0CALL>APRS,WIDE1-1:>Status"));

        Assert.False(gate.ReceiveOnly);
        Assert.Equal("N0CALL>APRS,WIDE1-1,qAR,N0CALL-10:>Status", decision.Packet?.ToString());
        Assert.True(PathAnalysis.FromPacket(decision.Packet!).IsGatedFromRadio);
    }

    [Fact]
    public void AReceiveOnlyIGateAddsQaoByDefault()
    {
        var gate = new RfToInternetGate(_igate);

        var decision = gate.Gate(AprsPacket.Parse("N0CALL>APRS:>Status"));

        Assert.True(gate.ReceiveOnly);
        var path = PathAnalysis.FromPacket(decision.Packet!);
        Assert.Equal("qAO", path.QConstruct);
        Assert.Equal(_igate, path.EntryStation);
    }

    [Fact]
    public void GatesAPacketHeardThroughSeveralDigipeatersOnce()
    {
        var gate = Gate();

        var direct = gate.Gate(AprsPacket.Parse("N0CALL-9>APRS,WIDE1-1,WIDE2-1:>Status"));
        var first = gate.Gate(AprsPacket.Parse("N0CALL-9>APRS,DIGI1,WIDE1*,WIDE2-1:>Status"));
        var second = gate.Gate(AprsPacket.Parse("N0CALL-9>APRS,DIGI1,WIDE1,DIGI2,WIDE2*:>Status"));

        Assert.True(direct.IsGated);
        Assert.Equal(GateRefusal.Duplicate, first.Refusal);
        Assert.Equal(GateRefusal.Duplicate, second.Refusal);
    }

    [Fact]
    public void GatesTheSamePacketAgainAfterTheDuplicateWindow()
    {
        var gate = Gate();
        var packet = AprsPacket.Parse("N0CALL>APRS:>Status");

        var first = gate.Gate(packet);
        _time.Advance(TimeSpan.FromSeconds(29));
        var copy = gate.Gate(packet);
        _time.Advance(TimeSpan.FromSeconds(1));
        var beacon = gate.Gate(packet);

        Assert.True(first.IsGated);
        Assert.Equal(GateRefusal.Duplicate, copy.Refusal);
        Assert.True(beacon.IsGated);
    }

    [Fact]
    public void ARefusedPacketDoesNotHoldBackItsCopies()
    {
        var gate = Gate();

        var refused = gate.Gate(AprsPacket.Parse("N0CALL>APRS,NOGATE:>Status"));
        var later = gate.Gate(AprsPacket.Parse("N0CALL>APRS:>Status"));

        Assert.Equal(GateRefusal.NoGate, refused.Refusal);
        Assert.True(later.IsGated);
    }

    [Fact]
    public void RefusesALineLongerThanAprsIsCarries()
    {
        var gate = Gate();
        // 510 bytes with ",qAO,N0CALL-10" added fits; one more does not.
        var header = "N0CALL>APRS:>";
        var fits = header + new string('x', 510 - header.Length - ",qAO,N0CALL-10".Length);

        var gated = gate.Gate(AprsPacket.Parse(fits));
        var tooLong = gate.Gate(AprsPacket.Parse(fits + "y"));

        Assert.Equal(510, gated.Packet?.Raw.Length);
        Assert.Equal(GateRefusal.TooLong, tooLong.Refusal);
    }

    [Fact]
    public void GatesAprsFramesFromATnc()
    {
        var frame = Ax25Frame.FromAprsPacket(AprsPacket.Parse("N0CALL>APRS,WIDE1-1:!4903.50N/07201.75W-Test"));

        var decision = Gate().Gate(frame);

        Assert.Equal("N0CALL>APRS,WIDE1-1,qAO,N0CALL-10:!4903.50N/07201.75W-Test", decision.Packet?.ToString());
    }

    [Fact]
    public void RefusesFramesThatAreNotAprs()
    {
        var raw = Ax25Frame.FromAprsPacket(AprsPacket.Parse("N0CALL>APRS:>Status")).GetBytes();
        raw[15] = 0xCF; // NET/ROM rather than no layer 3 protocol
        Assert.True(Ax25Frame.TryDecode(raw, out var frame, out _));

        var decision = Gate().Gate(frame);

        Assert.Equal(GateRefusal.NotAprs, decision.Refusal);
    }

    [Fact]
    public void RefusesAnIGateCallsignLongerThanALogin()
    {
        // Callsign.Parse stops at 9 characters, but a parsed header can hold a longer one.
        var tooLong = AprsPacket.Parse("N0CALL-123>APRS:>Status").Source!;

        Assert.Throws<ArgumentException>(() => new RfToInternetGate(tooLong));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RfToInternetGate(_igate, new RfToInternetGateOptions { DuplicateWindow = TimeSpan.Zero })
        );
    }

    [Fact]
    public void CountsGatedAndRefusedPacketsByReason()
    {
        var igate = Callsign.Parse("N0MTR-10"); // other tests record too; this callsign is only used here
        var measurements = new List<(string Instrument, string? Reason)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "AprsNet.IGate")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, _, tags, _) =>
            {
                string? call = null;
                string? reason = null;
                foreach (var tag in tags)
                {
                    call = tag.Key == "igate" ? (string?)tag.Value : call;
                    reason = tag.Key == "reason" ? (string?)tag.Value : reason;
                }

                if (call == igate.Value)
                {
                    lock (measurements)
                    {
                        measurements.Add((instrument.Name, reason));
                    }
                }
            }
        );
        listener.Start();
        var gate = new RfToInternetGate(igate, new RfToInternetGateOptions { TimeProvider = _time });

        gate.Gate(AprsPacket.Parse("N0CALL>APRS:>Status"));
        gate.Gate(AprsPacket.Parse("N0CALL>APRS,WIDE1*:>Status"));
        gate.Gate(AprsPacket.Parse("N0CALL>APRS,TCPIP*:>Other"));
        gate.Gate(AprsPacket.Parse("N0CALL>APRS:?APRS?"));

        Assert.Equal(
            [
                ("aprsnet.igate.packets.gated", null),
                ("aprsnet.igate.packets.refused", "duplicate"),
                ("aprsnet.igate.packets.refused", "from_internet"),
                ("aprsnet.igate.packets.refused", "general_query"),
            ],
            measurements
        );
    }

    [Fact]
    public void EveryRefusalHasItsOwnReasonTag()
    {
        var tags = Enum.GetValues<GateRefusal>().Select(IGateMetrics.Reason).ToList();

        Assert.DoesNotContain("other", tags);
        Assert.Equal(tags.Count, tags.Distinct().Count());
    }
}
