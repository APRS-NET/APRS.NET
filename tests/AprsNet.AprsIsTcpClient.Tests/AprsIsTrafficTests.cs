using AprsNet.Packets;

namespace AprsNet.AprsIsTcpClient.Tests;

/// <summary>The <c>Traffic</c> event: every line to and from APRS-IS, for a troubleshooting console.</summary>
public sealed class AprsIsTrafficTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private const int Passcode = 24680; // made up; the fake server accepts any
    private const string PasscodeText = "24680";

    private static AprsIsClientOptions Options(int passcode = Passcode) =>
        new()
        {
            Callsign = "N0CALL",
            Passcode = passcode,
            SoftwareName = "test",
            SoftwareVersion = "1.0",
            Filter = "r/35/-97/200",
        };

    private static List<AprsIsTrafficEventArgs> Record(Action<EventHandler<AprsIsTrafficEventArgs>> subscribe)
    {
        var entries = new List<AprsIsTrafficEventArgs>();
        subscribe(
            (_, e) =>
            {
                lock (entries)
                {
                    entries.Add(e);
                }
            }
        );
        return entries;
    }

    private static List<AprsIsTrafficEventArgs> Snapshot(List<AprsIsTrafficEventArgs> entries)
    {
        lock (entries)
        {
            return [.. entries];
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Wait);
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    [Fact]
    public async Task EveryLineIsReportedWithWhatItIs()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# aprsc 2.1.19-g730c5c0");
        transport.Server("# logresp N0CALL verified, server T2TEST");
        transport.Server("# aprsc 2.1.19-g730c5c0 18 Oct 2026 04:00:00 GMT T2TEST 1.2.3.4:14580");
        transport.Server("N0CALL>APRS,TCPIP*,qAC,T2TEST:>hello");
        await using var client = new AprsIsClient(Options(), transport: transport);
        var entries = Record(h => client.Traffic += h);

        await client.ConnectAsync(TestContext.Current.CancellationToken);
        await WaitUntil(() => Snapshot(entries).Any(e => e.Kind == AprsIsTrafficKind.Packet));
        await client.SetFilterAsync("b/N0CALL", TestContext.Current.CancellationToken);
        await client.SendAsync(AprsPacket.Parse("N0CALL>APZNET,TCPIP*:>sent"), TestContext.Current.CancellationToken);

        var seen = Snapshot(entries);
        Assert.Equal(
            [
                AprsIsTrafficKind.LoginSent,
                AprsIsTrafficKind.ServerComment,
                AprsIsTrafficKind.LoginResponse,
                AprsIsTrafficKind.ServerComment,
                AprsIsTrafficKind.Packet,
                AprsIsTrafficKind.FilterSent,
                AprsIsTrafficKind.PacketSent,
            ],
            seen.Select(e => e.Kind)
        );
        Assert.Equal("user N0CALL pass ***** vers test 1.0 filter r/35/-97/200", seen[0].Line);
        Assert.DoesNotContain(seen, e => e.Line.Contains(PasscodeText, StringComparison.Ordinal));
        Assert.Equal("N0CALL>APRS,TCPIP*,qAC,T2TEST:>hello", seen[4].Packet!.ToString());
        Assert.Equal("#filter b/N0CALL", seen[5].Line);
        Assert.Equal("N0CALL>APZNET,TCPIP*:>sent", seen[6].Packet!.ToString());
        Assert.Equal(
            [AprsIsTrafficDirection.Sent, AprsIsTrafficDirection.Received],
            seen.Take(2).Select(e => e.Direction)
        );
    }

    [Theory]
    [InlineData("user N0CALL pass 24680 vers test 1.0", "user N0CALL pass ***** vers test 1.0")]
    [InlineData(
        "user N0CALL pass 24680 vers test 1.0 filter r/1/2/3",
        "user N0CALL pass ***** vers test 1.0 filter r/1/2/3"
    )]
    [InlineData("user SWL482913 pass -1 vers test 1.0", "user SWL482913 pass -1 vers test 1.0")]
    [InlineData("user N0CALL pass -12 vers test 1.0", "user N0CALL pass ***** vers test 1.0")]
    public void TheLoginLineNeverCarriesAPasscode(string line, string shown) =>
        Assert.Equal(shown, AprsIsClient.MaskLogin(line));

    [Fact]
    public async Task NothingIsReportedWhileNoOneListensAndAThrowingHandlerIsIgnored()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp N0CALL verified, server T2TEST");
        transport.Server("N0CALL>APRS:>one");
        await using var client = new AprsIsClient(Options(), transport: transport) { WantsTraffic = () => false };
        var entries = Record(h => client.Traffic += h);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Single(await Take(client, 1));
        Assert.Empty(entries);

        var throwing = new FakeAprsTransport();
        throwing.Server("# logresp N0CALL verified, server T2TEST");
        throwing.Server("N0CALL>APRS:>one");
        throwing.Server("N0CALL>APRS:>two");
        await using var other = new AprsIsClient(Options(), transport: throwing);
        other.Traffic += (_, _) => throw new InvalidOperationException("console bug");
        await other.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, (await Take(other, 2)).Count);
    }

    [Fact]
    public async Task TheConnectionForwardsTrafficAndSendsServerCommandsByHand()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp N0CALL verified, server T2TEST");
        await using var connection = new AprsIsConnection(Options(), transportFactory: () => transport);
        var entries = Record(h => connection.Traffic += h);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.SendCommandAsync("#filter b/N0CALL", TestContext.Current.CancellationToken).AsTask()
        );
        await connection.StartAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            connection.SendCommandAsync("N0CALL>APRS:>not a command", TestContext.Current.CancellationToken).AsTask()
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            connection
                .SendCommandAsync("#filter x\r\nN0CALL>APRS:>smuggled", TestContext.Current.CancellationToken)
                .AsTask()
        );
        await connection.SendCommandAsync("#filter b/N0CALL", TestContext.Current.CancellationToken);

        var seen = Snapshot(entries);
        Assert.Equal(
            [AprsIsTrafficKind.LoginSent, AprsIsTrafficKind.LoginResponse, AprsIsTrafficKind.CommandSent],
            seen.Select(e => e.Kind)
        );
        Assert.Contains("#filter b/N0CALL", transport.Written);
        Assert.DoesNotContain(seen, e => e.Line.Contains(PasscodeText, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryKindHasADescription()
    {
        foreach (var kind in Enum.GetValues<AprsIsTrafficKind>())
        {
            Assert.NotEqual(kind.ToString(), kind.Describe());
        }
    }

    private static async Task<List<string>> Take(AprsIsClient client, int count)
    {
        using var cts = new CancellationTokenSource(Wait);
        var lines = new List<string>();
        await foreach (var line in client.ReadLinesAsync(cts.Token))
        {
            lines.Add(line);
            if (lines.Count == count)
            {
                break;
            }
        }

        return lines;
    }
}
