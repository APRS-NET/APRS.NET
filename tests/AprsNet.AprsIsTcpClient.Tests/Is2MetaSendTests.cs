using System.Collections.Concurrent;
using AprsNet.Packets;
using AprsNet.Packets.Is2;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.AprsIsTcpClient.Tests;

public class Is2MetaSendTests
{
    private static readonly PacketAddress _address = PacketAddress.Parse("N0CALL-9", "APZ123", []);

    private static readonly ReceptionMetadata _ax25 = new()
    {
        Modem = ReceptionModem.Ax25,
        BitRate = 1200,
        FrequencyHz = 144390000,
    };

    private const string Ax25Line = "#is2-meta rx_modem=1 rx_bitrate=1200 rx_frequency=144390000";

    private static AprsPacket Status(string text) => _address.Create(new StatusReport { Text = text });

    private static AprsIsClientOptions Options() =>
        new()
        {
            Callsign = "N0CALL-9",
            Passcode = AprsIsPasscode.Compute("N0CALL-9"),
            SoftwareName = "test",
            SoftwareVersion = "1.0",
            IdleTimeout = Timeout.InfiniteTimeSpan,
        };

    private static async Task<AprsIsClient> Connected(IAprsTransport transport)
    {
        var options = Options();
        options.SendInterval = TimeSpan.Zero; // no rate limit: the tests send many packets at once
        var client = new AprsIsClient(options, transport: transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        return client;
    }

    private static FakeAprsTransport Verified()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp N0CALL-9 verified, server T2TEST");
        return transport;
    }

    [Fact]
    public async Task SendsTheMetadataLineJustBeforeThePacket()
    {
        var transport = Verified();
        await using var client = await Connected(transport);

        await client.SendWithReceptionAsync(Status("Heard"), _ax25, TestContext.Current.CancellationToken);

        Assert.Equal([Ax25Line, "N0CALL-9>APZ123:>Heard"], transport.Written[^2..]);
        Assert.Equal(1, client.PacketsSent);
    }

    [Fact]
    public async Task SendsOnlyThePacketWithoutUsableMetadata()
    {
        var transport = Verified();
        await using var client = await Connected(transport);
        var before = transport.Written.Count;

        await client.SendWithReceptionAsync(Status("One"), null, TestContext.Current.CancellationToken);
        await client.SendWithReceptionAsync(
            Status("Two"),
            new ReceptionMetadata { RssiDbm = double.NaN },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(["N0CALL-9>APZ123:>One", "N0CALL-9>APZ123:>Two"], transport.Written[before..]);
    }

    [Fact]
    public async Task ThePlainSendNeverSendsAMetadataLine()
    {
        var transport = Verified();
        await using var client = await Connected(transport);

        await client.SendAsync(Status("Plain"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(transport.Written, line => line.StartsWith("#is2-meta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoOtherLineComesBetweenTheMetadataAndItsPacket()
    {
        var transport = new SlowTransport();
        transport.Inner.Server("# logresp N0CALL-9 verified, server T2TEST");
        await using var client = await Connected(transport);

        var sends = Enumerable
            .Range(0, 40)
            .Select(i =>
                i % 2 == 0
                    ? client.SendWithReceptionAsync(Status($"with {i}"), _ax25, TestContext.Current.CancellationToken)
                    : client.SendAsync(Status($"plain {i}"), TestContext.Current.CancellationToken)
            )
            .Select(send => send.AsTask());
        await Task.WhenAll(sends);

        var lines = transport.Inner.Written.Skip(1).ToList(); // after the login
        Assert.Equal(60, lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i] == Ax25Line)
            {
                Assert.StartsWith("N0CALL-9>APZ123:>with ", lines[i + 1], StringComparison.Ordinal);
            }
            else if (lines[i].Contains(">with ", StringComparison.Ordinal))
            {
                Assert.Equal(Ax25Line, lines[i - 1]);
            }
        }
    }

    [Fact]
    public async Task ThePacketStillFollowsItsMetadataWhenTheSendIsCancelledInBetween()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var transport = new SlowTransport { OnWrite = _ => cancel.Cancel() };
        transport.Inner.Server("# logresp N0CALL-9 verified, server T2TEST");
        await using var client = await Connected(transport);
        transport.CancelAfterMetadata = true;

        await client.SendWithReceptionAsync(Status("Heard"), _ax25, cancel.Token);

        Assert.Equal([Ax25Line, "N0CALL-9>APZ123:>Heard"], transport.Inner.Written[^2..]);
    }

    [Fact]
    public async Task AReconnectSendsTheMetadataAgainWithItsPacket()
    {
        var time = new FakeTimeProvider();
        var first = Verified();
        var second = Verified();
        var transports = new ConcurrentQueue<IAprsTransport>([first, second]);
        var options = Options();
        options.TimeProvider = time;
        await using var connection = new AprsIsConnection(options, transportFactory: () =>
            transports.TryDequeue(out var next) ? next : throw new InvalidOperationException()
        );
        await connection.StartAsync(TestContext.Current.CancellationToken);
        first.Close();
        await WaitUntil(() => connection.State == AprsIsConnectionState.Reconnecting);

        var send = connection
            .SendWithReceptionAsync(Status("Back"), _ax25, TestContext.Current.CancellationToken)
            .AsTask();
        for (var i = 0; i < 600 && !send.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(2, TestContext.Current.CancellationToken);
        }

        await send;
        Assert.Single(first.Written); // only its login: no metadata line left behind
        Assert.Equal([Ax25Line, "N0CALL-9>APZ123:>Back"], second.Written[^2..]);
    }

    [Fact]
    public async Task AListenOnlyConnectionRefusesIt()
    {
        var options = Options();
        options.ListenOnly = true;
        await using var connection = new AprsIsConnection(options, transportFactory: () =>
            throw new InvalidOperationException("It should not even connect.")
        );

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await connection.SendWithReceptionAsync(Status("No"), _ax25, TestContext.Current.CancellationToken)
        );
        Assert.Contains("listen", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitUntil(Func<bool> done)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!done())
        {
            await Task.Delay(2, timeout.Token);
        }
    }

    /// <summary>
    /// Yields before each write, so parallel senders get every chance to interleave, and honours the token as a real
    /// socket would.
    /// </summary>
    private sealed class SlowTransport : IAprsTransport
    {
        public FakeAprsTransport Inner { get; } = new();

        public bool CancelAfterMetadata { get; set; }

        public Action<string>? OnWrite { get; init; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken) => Inner.ConnectAsync(cancellationToken);

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            Inner.ReadLineAsync(cancellationToken);

        public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            await Inner.WriteLineAsync(line, cancellationToken);
            if (CancelAfterMetadata && line.StartsWith("#is2-meta", StringComparison.Ordinal))
            {
                OnWrite?.Invoke(line);
            }
        }

        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}
