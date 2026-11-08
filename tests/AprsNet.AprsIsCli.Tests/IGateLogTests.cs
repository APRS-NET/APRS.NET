using AprsNet.AprsIsTcpClient;
using AprsNet.IGate;
using AprsNet.Packets;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli.Tests;

/// <summary>What <c>serve</c>'s IGate logs about the packets it gates, drops and refuses.</summary>
public class IGateLogTests
{
    private const string IGate = "N0CALL-10";

    [Fact]
    public async Task AMessageGatedToRfIsLoggedWithItsControlCharactersEscaped()
    {
        var logs = new CapturingLoggerProvider();
        var downlink = Downlink(logs);
        await Drain(downlink.HearAsync(Packets("N1ABC>APRS,WIDE1-1:>on RF")));

        await Drain(downlink.GateAsync(Packets("W1AW>APRS,TCPIP*,qAC,T2TEST::N1ABC    :\u001b[2J\u009b31m{01")));

        var logged = Assert.Single(logs.Entries, e => e.Message.StartsWith("Gating to RF", StringComparison.Ordinal));
        Assert.EndsWith(@"::N1ABC    :\x1B[2J\x9B31m{01", logged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOrdinaryMessageGatedToRfIsLoggedAsItIs()
    {
        var logs = new CapturingLoggerProvider();
        var downlink = Downlink(logs);
        await Drain(downlink.HearAsync(Packets("N1ABC>APRS,WIDE1-1:>on RF")));

        await Drain(downlink.GateAsync(Packets("W1AW>APRS,TCPIP*,qAC,T2TEST::N1ABC    :Hello{01")));

        Assert.Contains(
            logs.Entries,
            e => e.Message == "Gating to RF: N0CALL-10>APZNET,WIDE1-1:}W1AW>APRS,TCPIP,N0CALL-10*::N1ABC    :Hello{01"
        );
    }

    [Fact]
    public async Task APacketNotGatedToRfIsLoggedWithItsControlCharactersEscaped()
    {
        var logs = new CapturingLoggerProvider();

        await Drain(
            Downlink(logs).GateAsync(Packets("W1AW>APRS,TCPIP*,qAC,T2TEST::N9NONE   :\u001b]0;title\u0007{01"))
        );

        var logged = Assert.Single(
            logs.Entries,
            e => e.Message.StartsWith("Not gated to RF", StringComparison.Ordinal)
        );
        Assert.EndsWith(@":\x1B]0;title\x07{01", logged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APacketNotGatedToAprsIsIsLoggedWithItsControlCharactersEscaped()
    {
        var logs = new CapturingLoggerProvider();

        await Drain(Uplink(logs).TapAsync(Packets("N1ABC>APRS,TCPIP*:>\u001b[2J\u009d52;c;x\u0007")));

        var logged = Assert.Single(logs.Entries, e => e.Message.StartsWith("Not gated", StringComparison.Ordinal));
        Assert.EndsWith(@":>\x1B[2J\x9D52;c;x\x07", logged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APacketDroppedFromAFullAprsIsQueueIsLoggedWithItsControlCharactersEscaped()
    {
        var logs = new CapturingLoggerProvider();

        // Nothing sends the queue, so the 501st gated packet doesn't fit.
        var heard = Enumerable
            .Range(0, 500)
            .Select(i => $"N1ABC>APRS,WIDE1-1:>status {i}")
            .Append("N1ABC>APRS:>\u001b[2J");
        await Drain(Uplink(logs).TapAsync(Packets([.. heard])));

        var logged = Assert.Single(
            logs.Entries,
            e => e.Message.StartsWith("IGate queue full", StringComparison.Ordinal)
        );
        Assert.Equal(@"IGate queue full; dropped N1ABC>APRS,qAO,N0CALL-10:>\x1B[2J", logged.Message);
    }

    private static IGateDownlink Downlink(CapturingLoggerProvider logs) =>
        new(PacketAddress.Parse(IGate, "APZNET", ["WIDE1-1"]), TimeProvider.System, logs.CreateLogger("aprscli"));

    private static IGateUplink Uplink(CapturingLoggerProvider logs) =>
        new(
            new AprsIsConnection(
                new AprsIsClientOptions
                {
                    Callsign = IGate,
                    SoftwareName = "test",
                    SoftwareVersion = "1.0",
                }
            ),
            new RfToInternetGate(Callsign.Parse(IGate)),
            AprsPacket.Parse($"{IGate}>APZNET:!4903.50NI07201.75W&"),
            TimeSpan.FromMinutes(30),
            TimeProvider.System,
            logs.CreateLogger("aprscli")
        );

    private static async IAsyncEnumerable<AprsPacket> Packets(params string[] lines)
    {
        foreach (var line in lines)
        {
            yield return AprsPacket.Parse(line);
        }

        await Task.CompletedTask;
    }

    private static async Task Drain(IAsyncEnumerable<AprsPacket> packets)
    {
        await foreach (var _ in packets) { }
    }
}
