using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.AprsIsTcpClient.Tests;

public class SendTests
{
    private static readonly PacketAddress _address = PacketAddress.Parse("N0CALL-9", "APZ123", []);

    private static AprsPacket Status(string text = "On the air") => _address.Create(new StatusReport { Text = text });

    private static async Task<(AprsIsClient Client, FakeAprsTransport Transport)> Connected(
        string status = "verified",
        Action<AprsIsClientOptions>? configure = null
    )
    {
        var transport = new FakeAprsTransport();
        transport.Server($"# logresp N0CALL-9 {status}, server T2TEST");
        var options = new AprsIsClientOptions
        {
            Callsign = "N0CALL-9",
            Passcode = AprsIsPasscode.Compute("N0CALL-9"),
            SoftwareName = "test",
            SoftwareVersion = "1.0",
            IdleTimeout = Timeout.InfiniteTimeSpan,
        };
        configure?.Invoke(options);
        var client = new AprsIsClient(options, transport: transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        return (client, transport);
    }

    [Fact]
    public async Task SendsThePacketLineOnAVerifiedLogin()
    {
        var (client, transport) = await Connected();
        await using var _ = client;

        await client.SendAsync(Status(), TestContext.Current.CancellationToken);

        Assert.Equal("N0CALL-9>APZ123:>On the air", transport.Written[^1]);
        Assert.Equal(1, client.PacketsSent);
    }

    [Fact]
    public async Task KeepsNonAsciiBytesAsTheyAre()
    {
        var (client, transport) = await Connected();
        await using var _ = client;

        await client.SendAsync(Status("Grüße"), TestContext.Current.CancellationToken);

        // UTF-8 bytes, carried one character per byte: the transport writes them back as the same bytes.
        Assert.Equal("N0CALL-9>APZ123:>GrÃ¼Ã\u009fe", transport.Written[^1]);
    }

    [Fact]
    public async Task RefusesToSendOnAnUnverifiedLogin()
    {
        var (client, transport) = await Connected("unverified");
        await using var _ = client;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.SendAsync(Status(), TestContext.Current.CancellationToken)
        );

        Assert.Contains("not verified", ex.Message, StringComparison.Ordinal);
        Assert.Single(transport.Written); // only the login line
    }

    [Fact]
    public async Task RefusesToSendBeforeConnecting()
    {
        await using var client = new AprsIsClient(
            new AprsIsClientOptions
            {
                Callsign = "N0CALL",
                SoftwareName = "test",
                SoftwareVersion = "1.0",
            },
            transport: new FakeAprsTransport()
        );

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.SendAsync(Status(), TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task RefusesToSendAfterTheConnectionEnded()
    {
        var (client, transport) = await Connected();
        await using var _ = client;
        transport.Close();
        await foreach (var line in client.ReadLinesAsync(TestContext.Current.CancellationToken))
        {
            Assert.Fail($"unexpected {line}");
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.SendAsync(Status(), TestContext.Current.CancellationToken)
        );

        Assert.Contains("connection has ended", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("N0CALL>APRS,TCPIP*,qAC,T2TEST:>relayed", "q-construct")]
    [InlineData("N0CALL>APRS,WIDE1-1,qAR,N0CALL-10:>gated by another IGate", "q-construct")]
    [InlineData("N0CALL>APRS,qAS,N0CALL-9:>passed on by a server", "q-construct")]
    [InlineData("N0CALL>APRS,qAR,N0CALL-9,T2TEST:>more after the IGate", "q-construct")]
    [InlineData("N0CALL>APRS:>one\nline", "line break")]
    [InlineData("not a packet", "header")]
    public async Task RefusesPacketsThatCannotGoOnTheConnection(string line, string reason)
    {
        var (client, transport) = await Connected();
        await using var _ = client;

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SendAsync(AprsPacket.Parse(line), TestContext.Current.CancellationToken)
        );

        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
        Assert.Single(transport.Written);
    }

    [Theory]
    [InlineData("N0CALL>APRS,WIDE1-1,qAO,N0CALL-9:>heard on RF")]
    [InlineData("N0CALL>APRS,WIDE1*,WIDE2-1,qAR,n0call-9:>heard on RF")]
    public async Task SendsWhatTheLoginGatedFromRf(string line)
    {
        var (client, transport) = await Connected();
        await using var _ = client;

        await client.SendAsync(AprsPacket.Parse(line), TestContext.Current.CancellationToken);

        Assert.Equal(line, transport.Written[^1]);
    }

    [Fact]
    public async Task RefusesALineLongerThanAprsIsAccepts()
    {
        var (client, _) = await Connected();
        await using var __ = client;
        var packet = AprsPacket.Parse("N0CALL>APRS:>" + new string('x', 500));

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SendAsync(packet, TestContext.Current.CancellationToken)
        );

        Assert.Contains("at most 510", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpreadsSendsOutAfterTheBurst()
    {
        var time = new FakeTimeProvider();
        var (client, transport) = await Connected(configure: o =>
        {
            o.TimeProvider = time;
            o.SendBurst = 2;
            o.SendInterval = TimeSpan.FromSeconds(10);
        });
        await using var _ = client;

        await client.SendAsync(Status("1"), TestContext.Current.CancellationToken);
        await client.SendAsync(Status("2"), TestContext.Current.CancellationToken);
        var third = client.SendAsync(Status("3"), TestContext.Current.CancellationToken).AsTask();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(third.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(10));
        await third.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(4, transport.Written.Count);
    }

    [Fact]
    public async Task AWaitingSendCanBeCancelled()
    {
        var time = new FakeTimeProvider();
        var (client, transport) = await Connected(configure: o =>
        {
            o.TimeProvider = time;
            o.SendBurst = 1;
        });
        await using var _ = client;
        await client.SendAsync(Status("1"), TestContext.Current.CancellationToken);
        using var cancel = new CancellationTokenSource();

        var second = client.SendAsync(Status("2"), cancel.Token).AsTask();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(2, transport.Written.Count);
    }

    [Fact]
    public async Task AZeroIntervalTurnsTheLimitOff()
    {
        var (client, transport) = await Connected(configure: o =>
        {
            o.SendBurst = 1;
            o.SendInterval = TimeSpan.Zero;
        });
        await using var _ = client;

        for (var i = 0; i < 20; i++)
        {
            await client.SendAsync(Status(), TestContext.Current.CancellationToken);
        }

        Assert.Equal(21, transport.Written.Count);
    }

    [Fact]
    public void TheDefaultLimitIsFiveThenOneEverySixSeconds()
    {
        var options = new AprsIsClientOptions();

        Assert.Equal(5, options.SendBurst);
        Assert.Equal(TimeSpan.FromSeconds(6), options.SendInterval);
    }

    [Fact]
    public void OptionsRejectAnUnusableLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AprsIsClientOptions { Callsign = "N0CALL", SendBurst = 0 }.Validate()
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AprsIsClientOptions { Callsign = "N0CALL", SendInterval = TimeSpan.FromSeconds(-1) }.Validate()
        );
    }
}
