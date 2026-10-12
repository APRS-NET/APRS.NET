using Microsoft.Extensions.Time.Testing;

namespace AprsNet.AprsIsTcpClient.Tests;

public class AprsIsClientTests
{
    private static readonly TimeSpan _short = TimeSpan.FromSeconds(5);

    private static AprsIsClientOptions Options(Action<AprsIsClientOptions>? configure = null)
    {
        var options = new AprsIsClientOptions
        {
            Callsign = "NZ0P",
            SoftwareName = "test",
            SoftwareVersion = "1.0",
        };
        configure?.Invoke(options);
        return options;
    }

    private static async Task<List<string>> Take(AprsIsClient client, int count)
    {
        using var cts = new CancellationTokenSource(_short);
        var lines = new List<string>();
        await foreach (var line in client.ReadLinesAsync(cts.Token))
        {
            lines.Add(line);
            if (lines.Count == count)
                break;
        }

        return lines;
    }

    [Fact]
    public async Task SendsAReadOnlyLoginWithFilterAndReadsTheResponse()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# aprsc 2.1.19");
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(o => o.Filter = "r/35/-97/100"), transport: transport);

        var result = await client.ConnectAsync();

        Assert.Equal(new LoginResult("NZ0P", LoginStatus.Unverified, "T2TEST"), result);
        Assert.Equal(result, client.Login);
        Assert.Equal(["user NZ0P pass -1 vers test 1.0 filter r/35/-97/100"], transport.Written);
    }

    [Fact]
    public async Task OmitsTheFilterWhenNoneIsSet()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P verified, server T2TEST");
        await using var client = new AprsIsClient(Options(o => o.Passcode = 12345), transport: transport);

        var result = await client.ConnectAsync();

        Assert.Equal(LoginStatus.Verified, result.Status);
        Assert.Equal(["user NZ0P pass 12345 vers test 1.0"], transport.Written);
    }

    [Fact]
    public async Task DeliversPacketsAndSkipsServerCommentsAndBlankLines()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync();

        transport.Server("# keepalive");
        transport.Server("");
        transport.Server("W1AW>APRS,TCPIP*:>status one");
        transport.Server("K1ABC>APRS,TCPIP*:>status two");

        Assert.Equal(["W1AW>APRS,TCPIP*:>status one", "K1ABC>APRS,TCPIP*:>status two"], await Take(client, 2));
        Assert.Equal(2, client.PacketsReceived);
        Assert.Equal(1, client.ServerCommentsReceived);
    }

    [Fact]
    public async Task StreamEndsCleanlyWhenTheServerCloses()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync();

        transport.Server("W1AW>APRS:>last");
        transport.Close();

        Assert.Equal(["W1AW>APRS:>last"], await Take(client, 10));
    }

    [Fact]
    public async Task StreamThrowsWhenTheConnectionFails()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync();

        transport.Fail(new IOException("reset"));

        var ex = await Assert.ThrowsAsync<IOException>(() => Take(client, 1));
        Assert.Equal("reset", ex.Message);
    }

    [Fact]
    public async Task LoginFailsWhenTheServerClosesFirst()
    {
        var transport = new FakeAprsTransport();
        transport.Close();
        await using var client = new AprsIsClient(Options(), transport: transport);

        await Assert.ThrowsAsync<LoginException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task LoginIsRejectedWhenTheServerRefusesN0CallWithoutALogresp()
    {
        // Seen live on several Tier 2 servers: a comment line, no "# logresp", and the connection stays open.
        var transport = new FakeAprsTransport();
        transport.Server("# aprsc 2.1.21-gbfc2090");
        transport.Server("# Login by user not allowed");
        await using var client = new AprsIsClient(Options(o => o.Callsign = "N0CALL"), transport: transport);

        var ex = await Assert.ThrowsAsync<LoginRejectedException>(() => client.ConnectAsync());

        Assert.Equal("Login by user not allowed", ex.ServerMessage);
        Assert.Null(client.Login);
        Assert.Equal(["user N0CALL pass -1 vers test 1.0"], transport.Written);
    }

    [Fact]
    public async Task LoginIsRejectedWhenTheServerRefusesAndThenCloses()
    {
        // Some servers (for example a non-aprsc Tier 2) also hang up after refusing.
        var transport = new FakeAprsTransport();
        transport.Server("# aprsc 2.1 # T2Romania - APRS Tier 2 Network");
        transport.Server("# Login by user not allowed");
        transport.Close();
        await using var client = new AprsIsClient(Options(o => o.Callsign = "N0CALL"), transport: transport);

        await Assert.ThrowsAsync<LoginRejectedException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task ARejectedLoginIsALoginExceptionAndStopsTheClient()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# Login by user not allowed");
        await using var client = new AprsIsClient(Options(o => o.Callsign = "N0CALL"), transport: transport);

        await Assert.ThrowsAsync<LoginRejectedException>(() => client.ConnectAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync()); // single-use
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SetFilterAsync("t/m"));
    }

    [Fact]
    public async Task LoginFailsOnAnUnreadableResponse()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P maybe");
        await using var client = new AprsIsClient(Options(), transport: transport);

        await Assert.ThrowsAsync<LoginException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task LoginTimesOut()
    {
        var time = new FakeTimeProvider();
        var transport = new FakeAprsTransport();
        await using var client = new AprsIsClient(Options(o => o.TimeProvider = time), transport: transport);

        var connect = client.ConnectAsync();
        await transport.WaitUntilReadingAsync();
        time.Advance(TimeSpan.FromSeconds(15));

        var ex = await Assert.ThrowsAsync<LoginException>(() => connect);
        Assert.IsType<TimeoutException>(ex.InnerException);
    }

    [Fact]
    public async Task LoginWaitsUntilTheTimeout()
    {
        var time = new FakeTimeProvider();
        var transport = new FakeAprsTransport();
        await using var client = new AprsIsClient(Options(o => o.TimeProvider = time), transport: transport);

        var connect = client.ConnectAsync();
        await transport.WaitUntilReadingAsync();
        time.Advance(TimeSpan.FromSeconds(14));
        transport.Server("# logresp NZ0P unverified, server T2TEST");

        Assert.Equal(LoginStatus.Unverified, (await connect).Status);
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsALoginFailure()
    {
        var transport = new FakeAprsTransport();
        await using var client = new AprsIsClient(Options(), transport: transport);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(cts.Token));
    }

    [Fact]
    public async Task SetFilterSendsAFilterCommentLine()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync();

        await client.SetFilterAsync("t/m");

        Assert.Equal("#filter t/m", transport.Written[^1]);
    }

    [Theory]
    [InlineData("t/m\r\nuser X pass 1")]
    [InlineData("a\nb")]
    [InlineData("a\0b")]
    public async Task SetFilterRejectsControlCharacters(string filter)
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<ArgumentException>(async () => await client.SetFilterAsync(filter));
        Assert.Single(transport.Written);
    }

    [Fact]
    public async Task SetFilterRequiresAConnection()
    {
        await using var client = new AprsIsClient(Options(), transport: new FakeAprsTransport());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SetFilterAsync("t/m"));
    }

    [Fact]
    public async Task CannotConnectTwice()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task DisposeCompletesTheStreamAndDisposesTheTransport()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync();
        var reading = Take(client, 10);

        await client.DisposeAsync();

        Assert.Empty(await reading);
        Assert.True(transport.Disposed);
        await client.DisposeAsync(); // idempotent
    }

    [Fact]
    public async Task DropOldestKeepsTheNewestLinesWhenTheConsumerIsSlow()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(
            Options(o =>
            {
                o.ChannelCapacity = 2;
                o.OverflowPolicy = OverflowPolicy.DropOldest;
            }),
            transport: transport
        );
        await client.ConnectAsync();

        for (var i = 1; i <= 5; i++)
            transport.Server($"A>B:>{i}");

        // Let the receive loop queue all five before the consumer starts, so the queue overflows.
        using var wait = new CancellationTokenSource(_short);
        while (client.PacketsReceived < 5)
            await Task.Delay(5, wait.Token);

        await Task.Delay(50, wait.Token);
        transport.Close();

        Assert.Equal(["A>B:>4", "A>B:>5"], await Take(client, 10));
    }

    [Fact]
    public async Task ASilentServerEndsTheStreamWithATimeout()
    {
        var time = new FakeTimeProvider();
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(o => o.TimeProvider = time), transport: transport);
        await client.ConnectAsync();
        transport.Server("W1AW>APRS:>before the stall");
        await using var lines = client.ReadLinesAsync().GetAsyncEnumerator();
        Assert.True(await lines.MoveNextAsync());
        Assert.Equal("W1AW>APRS:>before the stall", lines.Current);

        await transport.WaitUntilReadingAsync();
        time.Advance(TimeSpan.FromSeconds(90));

        var ex = await Assert.ThrowsAsync<TimeoutException>(async () => await lines.MoveNextAsync());
        Assert.Equal("No data from the server for 90s.", ex.Message);
    }

    [Fact]
    public async Task ServerKeepalivesHoldOffTheIdleTimeout()
    {
        var time = new FakeTimeProvider();
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(o => o.TimeProvider = time), transport: transport);
        await client.ConnectAsync();

        // Ten keepalives 60 s apart: 10 minutes in all, but never 90 s of silence.
        for (var i = 0; i < 10; i++)
        {
            await transport.WaitUntilReadingAsync();
            time.Advance(TimeSpan.FromSeconds(60));
            transport.Server("# aprsc keepalive");
        }

        transport.Server("W1AW>APRS:>still here");

        Assert.Equal(["W1AW>APRS:>still here"], await Take(client, 1));
        Assert.Equal(10, client.ServerCommentsReceived);
    }

    [Fact]
    public async Task OnlyTheSilenceSinceTheLastLineCounts()
    {
        var time = new FakeTimeProvider();
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(o => o.TimeProvider = time), transport: transport);
        await client.ConnectAsync();
        await using var lines = client.ReadLinesAsync().GetAsyncEnumerator();

        await transport.WaitUntilReadingAsync();
        time.Advance(TimeSpan.FromSeconds(89));
        transport.Server("W1AW>APRS:>just in time");
        Assert.True(await lines.MoveNextAsync());

        await transport.WaitUntilReadingAsync();
        time.Advance(TimeSpan.FromSeconds(90));
        await Assert.ThrowsAsync<TimeoutException>(async () => await lines.MoveNextAsync());
    }

    [Fact]
    public async Task OptionsAcceptAnInfiniteIdleTimeout()
    {
        await using var client = new AprsIsClient(
            Options(o => o.IdleTimeout = Timeout.InfiniteTimeSpan),
            transport: new FakeAprsTransport()
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void OptionsRejectANonPositiveIdleTimeout(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AprsIsClient(Options(o => o.IdleTimeout = TimeSpan.FromSeconds(seconds)))
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("TOOLONGCALL1")]
    [InlineData("N0 CALL")]
    [InlineData("NZ0P\r\n")]
    public void OptionsRejectBadCallsigns(string callsign)
    {
        Assert.Throws<ArgumentException>(() => new AprsIsClient(Options(o => o.Callsign = callsign)));
    }

    [Fact]
    public void OptionsRejectAFilterWithControlCharacters()
    {
        Assert.Throws<ArgumentException>(() => new AprsIsClient(Options(o => o.Filter = "r/1/2/3\r\nx")));
    }

    [Theory]
    [InlineData("# logresp NZ0P verified, server T2TEST", "NZ0P", LoginStatus.Verified, "T2TEST")]
    [InlineData("# logresp NZ0P unverified, server T2TEST", "NZ0P", LoginStatus.Unverified, "T2TEST")]
    [InlineData("# logresp NZ0P verified", "NZ0P", LoginStatus.Verified, null)]
    [InlineData("# logresp NZ0P-5 UNVERIFIED, server X", "NZ0P-5", LoginStatus.Unverified, "X")]
    public void LoginResultParses(string line, string callsign, LoginStatus status, string? server)
    {
        Assert.Equal(new LoginResult(callsign, status, server), LoginResult.TryParse(line));
    }

    [Theory]
    [InlineData("# logresp")]
    [InlineData("# logresp NZ0P")]
    [InlineData("# logresp NZ0P maybe")]
    [InlineData("# aprsc 2.1")]
    [InlineData("W1AW>APRS:>x")]
    public void LoginResultRejects(string line)
    {
        Assert.Null(LoginResult.TryParse(line));
    }
}
