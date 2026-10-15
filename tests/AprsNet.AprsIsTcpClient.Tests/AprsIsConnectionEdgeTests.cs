using AprsNet.Packets;

namespace AprsNet.AprsIsTcpClient.Tests;

/// <summary>The connection's less travelled paths: starting twice, a bad filter, and a full line queue.</summary>
public class AprsIsConnectionEdgeTests
{
    private static AprsIsClientOptions Options(Action<AprsIsClientOptions>? configure = null)
    {
        var options = new AprsIsClientOptions
        {
            Callsign = "N0CALL-9",
            SoftwareName = "test",
            SoftwareVersion = "1.0",
            IdleTimeout = Timeout.InfiniteTimeSpan,
        };
        configure?.Invoke(options);
        return options;
    }

    private static FakeAprsTransport Server()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp N0CALL-9 unverified, server T2TEST");
        return transport;
    }

    [Fact]
    public async Task RefusesToStartTwice()
    {
        var transport = Server();
        await using var connection = new AprsIsConnection(Options(), transportFactory: () => transport);
        await connection.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.StartAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task RefusesAFilterWithAControlCharacter()
    {
        await using var connection = new AprsIsConnection(Options(), transportFactory: Server);

        var ex = await Assert.ThrowsAsync<AprsValidationException>(async () =>
            await connection.SetFilterAsync("r/35/-97/200\r\nuser x", TestContext.Current.CancellationToken)
        );
        Assert.Equal(AprsValidationErrorCode.FilterControlCharacter, ex.Code);
    }

    [Fact]
    public async Task DropsLinesWhenTheQueueIsFull()
    {
        var transport = Server();
        var connection = new AprsIsConnection(
            Options(o =>
            {
                o.ChannelCapacity = 2;
                o.OverflowPolicy = OverflowPolicy.DropOldest;
            }),
            transportFactory: () => transport
        );
        await connection.StartAsync(TestContext.Current.CancellationToken);
        for (var i = 0; i < 10; i++)
        {
            transport.Server($"W1AW>APRS:>line {i}");
        }

        // Nobody reads while all ten arrive, so the bounded queues (two lines each) drop the oldest.
        await transport.WaitUntilReadingAsync();
        await connection.DisposeAsync();

        var kept = 0;
        await foreach (var _ in connection.ReadLinesAsync(TestContext.Current.CancellationToken))
        {
            kept++;
        }

        Assert.InRange(kept, 0, 2);
    }
}
