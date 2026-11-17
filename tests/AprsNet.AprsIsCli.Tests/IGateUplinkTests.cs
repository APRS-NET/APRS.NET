using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli.Tests;

/// <summary>
/// <c>serve --igate</c> never saves packets up while APRS-IS is down, nor sends them late, as aprsc's IGate hints ask.
/// </summary>
public class IGateUplinkTests
{
    [Fact]
    public async Task PacketsHeardWhileConnectedAreSentInOrder()
    {
        await using var rig = await UplinkRig.StartAsync();
        rig.Run();

        await rig.HearAsync("N1ABC>APRS:>one", "N2ABC>APRS:>two", "N3ABC>APRS:>three");

        await UplinkRig.WaitForAsync(() => UplinkRig.Sent(rig.First).Count == 3);
        Assert.Equal(
            ["N1ABC>APRS,qAO,N0CALL-10:>one", "N2ABC>APRS,qAO,N0CALL-10:>two", "N3ABC>APRS,qAO,N0CALL-10:>three"],
            UplinkRig.Sent(rig.First)
        );
    }

    [Fact]
    public async Task APacketHeardWhileAprsIsIsDownIsNotSentAfterItReconnects()
    {
        await using var rig = await UplinkRig.StartAsync();
        rig.Run();
        await rig.DropAsync();

        await rig.HearAsync("N1ABC>APRS:>while down");
        await rig.ReconnectAsync();
        await rig.HearAsync("N2ABC>APRS:>after");

        await UplinkRig.WaitForAsync(() => UplinkRig.Sent(rig.Second).Count == 1);
        Assert.Equal(["N2ABC>APRS,qAO,N0CALL-10:>after"], UplinkRig.Sent(rig.Second));
        Assert.Empty(UplinkRig.Sent(rig.First));
    }

    [Fact]
    public async Task APacketHeardWhileAprsIsIsDownIsCountedAsDropped()
    {
        await using var rig = await UplinkRig.StartAsync();
        await rig.DropAsync();

        await rig.HearAsync("N1ABC>APRS:>while down");

        Assert.Equal((1, 0, 1), (rig.Uplink.Gated, rig.Uplink.Sent, rig.Uplink.Dropped));
    }

    [Fact]
    public async Task APacketQueuedLongerThanTheLimitIsDroppedRatherThanSentLate()
    {
        await using var rig = await UplinkRig.StartAsync();
        await rig.HearAsync("N1ABC>APRS:>stale");
        rig.Clock.Advance(IGateUplink.MaxQueueWait + TimeSpan.FromSeconds(1));

        rig.Run();
        await rig.HearAsync("N2ABC>APRS:>fresh");

        await UplinkRig.WaitForAsync(() => rig.Uplink.Sent == 1);
        Assert.Equal(["N2ABC>APRS,qAO,N0CALL-10:>fresh"], UplinkRig.Sent(rig.First));
        Assert.Equal(1, rig.Uplink.Dropped);
    }

    [Fact]
    public async Task APacketQueuedWithinTheLimitIsStillSent()
    {
        await using var rig = await UplinkRig.StartAsync();
        await rig.HearAsync("N1ABC>APRS:>a short wait");
        rig.Clock.Advance(IGateUplink.MaxQueueWait - TimeSpan.FromSeconds(1));

        rig.Run();

        await UplinkRig.WaitForAsync(() => rig.Uplink.Sent == 1);
        Assert.Equal(["N1ABC>APRS,qAO,N0CALL-10:>a short wait"], UplinkRig.Sent(rig.First));
        Assert.Equal(0, rig.Uplink.Dropped);
    }

    [Fact]
    public async Task ABeaconDueWhileAprsIsIsDownGoesOutOnceItReconnects()
    {
        await using var rig = await UplinkRig.StartAsync();
        await rig.DropAsync();
        rig.Run();

        await rig.ReconnectAsync();

        await UplinkRig.WaitForAsync(() => rig.Uplink.Beacons == 1);
        Assert.Contains(UplinkRig.Beacon, UplinkRig.Written(rig.Second));
    }

    [Fact]
    public async Task APacketQueuedJustBeforeAprsIsWentDownIsNotSentAfterItReconnects()
    {
        await using var rig = await UplinkRig.StartAsync();
        await rig.HearAsync("N1ABC>APRS:>queued");
        await rig.DropAsync();

        rig.Run();
        await UplinkRig.WaitForAsync(() => rig.Uplink.Dropped == 1);
        await rig.ReconnectAsync();
        await rig.HearAsync("N2ABC>APRS:>after");

        await UplinkRig.WaitForAsync(() => rig.Uplink.Sent == 1);
        Assert.Equal(["N2ABC>APRS,qAO,N0CALL-10:>after"], UplinkRig.Sent(rig.Second));
    }

    [Fact]
    public async Task AprsIsGoingDownIsWarnedOnceNotForEveryDroppedPacket()
    {
        await using var rig = await UplinkRig.StartAsync();
        await rig.DropAsync();

        await rig.HearAsync("N1ABC>APRS:>one", "N2ABC>APRS:>two", "N3ABC>APRS:>three");

        var warning = Assert.Single(rig.Logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal(
            "APRS-IS is down; dropping gated packets until it is back, not sending them late",
            warning.Message
        );
    }

    [Fact]
    public async Task AprsIsComingBackIsLoggedWithTheCountDroppedMeanwhile()
    {
        await using var rig = await UplinkRig.StartAsync();
        rig.Run();
        await rig.DropAsync();
        await rig.HearAsync("N1ABC>APRS:>one", "N2ABC>APRS:>two");
        await rig.ReconnectAsync();

        await rig.HearAsync("N3ABC>APRS:>three");

        // The uplink counts the packet as sent, then logs that APRS-IS is back: wait for the line, not the count.
        await UplinkRig.WaitForAsync(() =>
            rig.Logs.Entries.Any(e =>
                e.Level == LogLevel.Information && e.Message == "APRS-IS is back; gating again (2 dropped meanwhile)"
            )
        );
        Assert.Equal(1, rig.Uplink.Sent);
    }
}
