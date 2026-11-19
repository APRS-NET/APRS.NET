using AprsNet.Packets.Is2;

namespace AprsNet.AprsIsCli.Tests;

/// <summary>
/// <c>serve --igate</c> sends a TNC's reception metadata to APRS-IS as an IS2 <c>#is2-meta</c> line just before each
/// packet it gates from that TNC.
/// </summary>
public class IGateUplinkReceptionTests
{
    private static readonly ReceptionMetadata _ax25 = new()
    {
        Modem = ReceptionModem.Ax25,
        BitRate = 1200,
        FrequencyHz = 144390000,
    };

    private const string Ax25Line = "#is2-meta rx_modem=1 rx_bitrate=1200 rx_frequency=144390000";

    [Fact]
    public async Task AGatedPacketGoesUpAsTheMetadataLineThenThePacketLine()
    {
        await using var rig = await UplinkRig.StartAsync();
        rig.Run();

        await rig.HearAsync(_ax25, "N1ABC>APRS:>one", "N2ABC>APRS:>two");

        await UplinkRig.WaitForAsync(() => rig.Uplink.Sent == 2);
        Assert.Equal(
            [Ax25Line, "N1ABC>APRS,qAO,N0CALL-10:>one", Ax25Line, "N2ABC>APRS,qAO,N0CALL-10:>two"],
            UplinkRig.Sent(rig.First)
        );
    }

    [Fact]
    public async Task APacketFromATncWithoutMetadataGoesAlone()
    {
        await using var rig = await UplinkRig.StartAsync();
        rig.Run();

        await rig.HearAsync(_ax25, "N1ABC>APRS:>with");
        await rig.HearAsync("N2ABC>APRS:>without");

        await UplinkRig.WaitForAsync(() => rig.Uplink.Sent == 2);
        Assert.Equal(
            [Ax25Line, "N1ABC>APRS,qAO,N0CALL-10:>with", "N2ABC>APRS,qAO,N0CALL-10:>without"],
            UplinkRig.Sent(rig.First)
        );
    }

    [Fact]
    public async Task ARefusedPacketSendsNothing()
    {
        await using var rig = await UplinkRig.StartAsync();
        rig.Run();

        await rig.HearAsync(_ax25, "N1ABC>APRS,TCPIP*:>from the internet", "N2ABC>APRS,NOGATE:>keep me off");
        await rig.HearAsync(_ax25, "N3ABC>APRS:>gated"); // something to wait for

        await UplinkRig.WaitForAsync(() => rig.Uplink.Sent == 1);
        Assert.Equal([Ax25Line, "N3ABC>APRS,qAO,N0CALL-10:>gated"], UplinkRig.Sent(rig.First));
    }

    [Fact]
    public async Task APacketDroppedWhileAprsIsIsDownLeavesNoMetadataBehind()
    {
        await using var rig = await UplinkRig.StartAsync();
        rig.Run();
        await rig.DropAsync();

        await rig.HearAsync(_ax25, "N1ABC>APRS:>while down");
        await rig.ReconnectAsync();
        await rig.HearAsync(_ax25, "N2ABC>APRS:>after");

        await UplinkRig.WaitForAsync(() => rig.Uplink.Sent == 1);
        Assert.Empty(UplinkRig.Sent(rig.First));
        Assert.Equal([Ax25Line, "N2ABC>APRS,qAO,N0CALL-10:>after"], UplinkRig.Sent(rig.Second));
    }

    [Fact]
    public async Task TheIGateBeaconNeverCarriesMetadata()
    {
        await using var rig = await UplinkRig.StartAsync();
        rig.Run();
        await rig.HearAsync(_ax25, "N1ABC>APRS:>heard");

        await UplinkRig.WaitForAsync(() => rig.Uplink.Beacons == 1 && rig.Uplink.Sent == 1);
        var written = UplinkRig.Written(rig.First);
        var beacon = written.IndexOf(UplinkRig.Beacon);
        Assert.NotEqual(Ax25Line, written[beacon - 1]);
    }
}
