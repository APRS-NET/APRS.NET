using AprsNet.AprsIsTcpClient;
using AprsNet.AprsIsTcpClient.Tests;
using AprsNet.IGate;
using AprsNet.Packets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.AprsIsCli.Tests;

/// <summary>
/// An <see cref="IGateUplink"/> on a verified APRS-IS connection over fake transports, on a fake clock: the first
/// connection, then one reconnect.
/// </summary>
internal sealed class UplinkRig : IAsyncDisposable
{
    public const string IGate = "N0CALL-10";
    public const string Beacon = "N0CALL-10>APZNET:!4903.50NR07201.75W&";

    private readonly CancellationTokenSource _stop = new();
    private Task? _run;
    private int _connections;

    private UplinkRig()
    {
        Connection = new AprsIsConnection(
            new AprsIsClientOptions
            {
                Callsign = IGate,
                Passcode = AprsIsPasscode.Compute(IGate),
                SoftwareName = "test",
                SoftwareVersion = "1.0",
                SendInterval = TimeSpan.Zero,
                ReconnectDelay = TimeSpan.FromSeconds(1),
                TimeProvider = Clock,
            },
            transportFactory: () => ++_connections == 1 ? First : Second
        );
        Uplink = new IGateUplink(
            Connection,
            new RfToInternetGate(
                Callsign.Parse(IGate),
                new RfToInternetGateOptions { ReceiveOnly = true, TimeProvider = Clock }
            ),
            AprsPacket.Parse(Beacon),
            TimeSpan.FromMinutes(30),
            Clock,
            Logs.CreateLogger("aprscli")
        );
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));

    public FakeAprsTransport First { get; } = Verified("T2FIRST");

    public FakeAprsTransport Second { get; } = Verified("T2SECOND");

    public CapturingLoggerProvider Logs { get; } = new();

    public AprsIsConnection Connection { get; }

    public IGateUplink Uplink { get; }

    /// <summary>Connected and logged in, with the uplink not sending yet.</summary>
    public static async Task<UplinkRig> StartAsync()
    {
        var rig = new UplinkRig();
        await rig.Connection.StartAsync(TestContext.Current.CancellationToken);
        return rig;
    }

    /// <summary>Starts sending the queue and beaconing.</summary>
    public void Run() => _run = Uplink.RunAsync(_stop.Token);

    /// <summary>The server closes the first connection; returns once the connection knows.</summary>
    public Task DropAsync()
    {
        First.Close();
        return WaitForAsync(() => Connection.State != AprsIsConnectionState.Connected);
    }

    /// <summary>Moves the clock on until the second connection is logged in.</summary>
    public async Task ReconnectAsync()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (Connection.State != AprsIsConnectionState.Connected)
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out reconnecting");
            Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>The TNC hears <paramref name="lines"/>.</summary>
    public async Task HearAsync(params string[] lines)
    {
        await foreach (var _ in Uplink.TapAsync(Packets(lines))) { }
    }

    /// <summary>What was sent on <paramref name="transport"/> after the login, beacons left out.</summary>
    public static List<string> Sent(FakeAprsTransport transport)
    {
        lock (transport.Written)
        {
            return [.. transport.Written.Skip(1).Where(line => line != Beacon)];
        }
    }

    public static List<string> Written(FakeAprsTransport transport)
    {
        lock (transport.Written)
        {
            return [.. transport.Written];
        }
    }

    public static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_run is not null)
        {
            await _run;
        }

        await Connection.DisposeAsync();
        _stop.Dispose();
    }

    private static FakeAprsTransport Verified(string server)
    {
        var transport = new FakeAprsTransport();
        transport.Server("# aprsc 2.1.test");
        transport.Server($"# logresp {IGate} verified, server {server}");
        return transport;
    }

    private static async IAsyncEnumerable<AprsPacket> Packets(string[] lines)
    {
        foreach (var line in lines)
        {
            yield return AprsPacket.Parse(line);
        }

        await Task.CompletedTask;
    }
}
