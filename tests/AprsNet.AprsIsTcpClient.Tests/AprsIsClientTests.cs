namespace AprsNet.AprsIsTcpClient.Tests;

public class AprsIsClientTests
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(5);

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
        using var cts = new CancellationTokenSource(Short);
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

    [Fact]
    public async Task Sends_a_read_only_login_with_filter_and_reads_the_response()
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
    public async Task Omits_the_filter_when_none_is_set()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P verified, server T2TEST");
        await using var client = new AprsIsClient(Options(o => o.Passcode = 12345), transport: transport);

        var result = await client.ConnectAsync();

        Assert.Equal(LoginStatus.Verified, result.Status);
        Assert.Equal(["user NZ0P pass 12345 vers test 1.0"], transport.Written);
    }

    [Fact]
    public async Task Delivers_packets_and_skips_server_comments_and_blank_lines()
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
    public async Task Stream_ends_cleanly_when_the_server_closes()
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
    public async Task Stream_throws_when_the_connection_fails()
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
    public async Task Login_fails_when_the_server_closes_first()
    {
        var transport = new FakeAprsTransport();
        transport.Close();
        await using var client = new AprsIsClient(Options(), transport: transport);

        await Assert.ThrowsAsync<LoginException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task Login_is_rejected_when_the_server_refuses_N0CALL_without_a_logresp()
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
    public async Task Login_is_rejected_when_the_server_refuses_and_then_closes()
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
    public async Task A_rejected_login_is_a_login_exception_and_stops_the_client()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# Login by user not allowed");
        await using var client = new AprsIsClient(Options(o => o.Callsign = "N0CALL"), transport: transport);

        await Assert.ThrowsAsync<LoginRejectedException>(() => client.ConnectAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync()); // single-use
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SetFilterAsync("t/m"));
    }

    [Fact]
    public async Task Login_fails_on_an_unreadable_response()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P maybe");
        await using var client = new AprsIsClient(Options(), transport: transport);

        await Assert.ThrowsAsync<LoginException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task Login_times_out()
    {
        var transport = new FakeAprsTransport();
        await using var client = new AprsIsClient(
            Options(o => o.LoginTimeout = TimeSpan.FromMilliseconds(100)),
            transport: transport
        );

        var ex = await Assert.ThrowsAsync<LoginException>(() => client.ConnectAsync());
        Assert.IsType<TimeoutException>(ex.InnerException);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_login_failure()
    {
        var transport = new FakeAprsTransport();
        await using var client = new AprsIsClient(Options(), transport: transport);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(cts.Token));
    }

    [Fact]
    public async Task SetFilter_sends_a_filter_comment_line()
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
    public async Task SetFilter_rejects_control_characters(string filter)
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<ArgumentException>(async () => await client.SetFilterAsync(filter));
        Assert.Single(transport.Written);
    }

    [Fact]
    public async Task SetFilter_requires_a_connection()
    {
        await using var client = new AprsIsClient(Options(), transport: new FakeAprsTransport());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SetFilterAsync("t/m"));
    }

    [Fact]
    public async Task Cannot_connect_twice()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task Dispose_completes_the_stream_and_disposes_the_transport()
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
    public async Task DropOldest_keeps_the_newest_lines_when_the_consumer_is_slow()
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
        {
            transport.Server($"A>B:>{i}");
        }

        // Let the receive loop queue all five before the consumer starts, so the queue overflows.
        using var wait = new CancellationTokenSource(Short);
        while (client.PacketsReceived < 5)
        {
            await Task.Delay(5, wait.Token);
        }

        await Task.Delay(50, wait.Token);
        transport.Close();

        Assert.Equal(["A>B:>4", "A>B:>5"], await Take(client, 10));
    }

    [Theory]
    [InlineData("")]
    [InlineData("TOOLONGCALL1")]
    [InlineData("N0 CALL")]
    [InlineData("NZ0P\r\n")]
    public void Options_reject_bad_callsigns(string callsign) =>
        Assert.Throws<ArgumentException>(() => new AprsIsClient(Options(o => o.Callsign = callsign)));

    [Fact]
    public void Options_reject_a_filter_with_control_characters() =>
        Assert.Throws<ArgumentException>(() => new AprsIsClient(Options(o => o.Filter = "r/1/2/3\r\nx")));

    [Theory]
    [InlineData("# logresp NZ0P verified, server T2TEST", "NZ0P", LoginStatus.Verified, "T2TEST")]
    [InlineData("# logresp NZ0P unverified, server T2TEST", "NZ0P", LoginStatus.Unverified, "T2TEST")]
    [InlineData("# logresp NZ0P verified", "NZ0P", LoginStatus.Verified, null)]
    [InlineData("# logresp NZ0P-5 UNVERIFIED, server X", "NZ0P-5", LoginStatus.Unverified, "X")]
    public void LoginResult_parses(string line, string callsign, LoginStatus status, string? server) =>
        Assert.Equal(new LoginResult(callsign, status, server), LoginResult.TryParse(line));

    [Theory]
    [InlineData("# logresp")]
    [InlineData("# logresp NZ0P")]
    [InlineData("# logresp NZ0P maybe")]
    [InlineData("# aprsc 2.1")]
    [InlineData("W1AW>APRS:>x")]
    public void LoginResult_rejects(string line) => Assert.Null(LoginResult.TryParse(line));
}
