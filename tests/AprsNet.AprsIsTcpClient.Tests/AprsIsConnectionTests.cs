using System.Collections.Concurrent;
using System.Net.Sockets;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.AprsIsTcpClient.Tests;

public sealed class AprsIsConnectionTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly ConcurrentQueue<IAprsTransport> _transports = new();
    private readonly List<FakeAprsTransport> _made = [];
    private readonly ConcurrentQueue<AprsIsConnectionStateChangedEventArgs> _states = new();
    private AprsIsConnection? _connection;

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    private FakeAprsTransport Server(string status = "verified")
    {
        var transport = new FakeAprsTransport();
        transport.Server($"# logresp N0CALL-9 {status}, server T2TEST{_transports.Count + 1}");
        _transports.Enqueue(transport);
        lock (_made)
        {
            _made.Add(transport);
        }

        return transport;
    }

    private void Unreachable() => _transports.Enqueue(new UnreachableTransport());

    private AprsIsConnection Connection(Action<AprsIsClientOptions>? configure = null)
    {
        var options = new AprsIsClientOptions
        {
            Callsign = "N0CALL-9",
            Passcode = AprsIsPasscode.Compute("N0CALL"),
            SoftwareName = "test",
            SoftwareVersion = "1.0",
            IdleTimeout = Timeout.InfiniteTimeSpan,
            TimeProvider = _time,
            Filter = "r/35/-97/200",
        };
        configure?.Invoke(options);
        _connection = new AprsIsConnection(
            options,
            transportFactory: () =>
                _transports.TryDequeue(out var next)
                    ? next
                    : throw new InvalidOperationException("The test queued no transport.")
        );
        _connection.StateChanged += (_, e) => _states.Enqueue(e);
        return _connection;
    }

    /// <summary>Moves the fake clock a second at a time until <paramref name="done"/>, so timers armed late still fire.</summary>
    private async Task AdvanceUntil(Func<bool> done, int maxSeconds = 600)
    {
        for (var i = 0; i < maxSeconds && !done(); i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(2, TestContext.Current.CancellationToken);
        }

        Assert.True(done(), "the condition never became true");
    }

    private static async Task WaitUntil(Func<bool> done)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!done())
        {
            await Task.Delay(2, timeout.Token);
        }
    }

    private static async Task<List<string>> Take(AprsIsConnection connection, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var lines = new List<string>();
        await foreach (var line in connection.ReadLinesAsync(timeout.Token))
        {
            lines.Add(line);
            if (lines.Count == count)
            {
                break;
            }
        }

        return lines;
    }

    private List<AprsIsConnectionStateChangedEventArgs> States => [.. _states];

    private static AprsPacket Status(string text) =>
        PacketAddress.Parse("N0CALL-9", "APZ123", []).Create(new StatusReport { Text = text });

    [Fact]
    public async Task StartsAndStreamsLines()
    {
        var server = Server();
        server.Server("W1AW>APRS:>one");
        var connection = Connection();

        var login = await connection.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("T2TEST1", login.Server);
        Assert.Equal(AprsIsConnectionState.Connected, connection.State);
        Assert.Equal(["W1AW>APRS:>one"], await Take(connection, 1));
        Assert.Equal([AprsIsConnectionState.Connecting, AprsIsConnectionState.Connected], States.Select(s => s.State));
    }

    [Fact]
    public async Task ReconnectsWhenTheServerClosesAndKeepsOneStream()
    {
        var first = Server();
        first.Server("W1AW>APRS:>one");
        var second = Server();
        second.Server("W1AW>APRS:>two");
        var connection = Connection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        first.Close();
        await AdvanceUntil(() => connection.Reconnects == 1);

        Assert.Equal(["W1AW>APRS:>one", "W1AW>APRS:>two"], await Take(connection, 2));
        var reconnecting = Assert.Single(States, s => s.State == AprsIsConnectionState.Reconnecting);
        Assert.Null(reconnecting.Error); // a clean close
        Assert.InRange(reconnecting.RetryIn!.Value.TotalSeconds, 4, 6); // 5 s with up to 20% jitter
        Assert.Equal("T2TEST2", connection.Login!.Server);
    }

    [Fact]
    public async Task BacksOffExponentiallyUpToTheMaximum()
    {
        var first = Server();
        Unreachable();
        Unreachable();
        Unreachable();
        Unreachable();
        Server();
        var connection = Connection(o =>
        {
            o.ReconnectDelay = TimeSpan.FromSeconds(10);
            o.MaxReconnectDelay = TimeSpan.FromSeconds(40);
        });
        await connection.StartAsync(TestContext.Current.CancellationToken);

        first.Fail(new SocketException((int)SocketError.ConnectionReset));
        await AdvanceUntil(() => connection.Reconnects == 1);

        var waits = States
            .Where(s => s.State == AprsIsConnectionState.Reconnecting)
            .Select(s => s.RetryIn!.Value.TotalSeconds)
            .ToList();
        Assert.Equal(5, waits.Count);
        Assert.InRange(waits[0], 8, 12);
        Assert.InRange(waits[1], 16, 24);
        Assert.InRange(waits[2], 32, 48);
        Assert.InRange(waits[3], 32, 48); // capped at 40 s
        Assert.InRange(waits[4], 32, 48);
        Assert.IsType<SocketException>(States.First(s => s.State == AprsIsConnectionState.Reconnecting).Error);
    }

    [Fact]
    public async Task StartsTheBackoffAgainAfterAConnectionThatHeld()
    {
        var first = Server();
        Unreachable();
        var second = Server();
        Server();
        var connection = Connection(o =>
        {
            o.ReconnectDelay = TimeSpan.FromSeconds(10);
            o.MaxReconnectDelay = TimeSpan.FromSeconds(60);
        });
        await connection.StartAsync(TestContext.Current.CancellationToken);
        first.Close();
        await AdvanceUntil(() => connection.Reconnects == 1); // waits about 10 s, then 20 s

        _time.Advance(TimeSpan.FromMinutes(5)); // the second connection holds
        second.Close();
        await AdvanceUntil(() => connection.Reconnects == 2);

        var last = States.Last(s => s.State == AprsIsConnectionState.Reconnecting);
        Assert.InRange(last.RetryIn!.Value.TotalSeconds, 8, 12);
    }

    [Fact]
    public async Task TheFirstConnectionFailsFast()
    {
        var rejected = new FakeAprsTransport();
        rejected.Server("# Login by user not allowed");
        _transports.Enqueue(rejected);
        var connection = Connection();

        await Assert.ThrowsAsync<LoginRejectedException>(() =>
            connection.StartAsync(TestContext.Current.CancellationToken)
        );

        Assert.Equal(AprsIsConnectionState.Closed, connection.State);
        await foreach (var line in connection.ReadLinesAsync(TestContext.Current.CancellationToken))
        {
            Assert.Fail($"unexpected {line}");
        }
    }

    [Fact]
    public async Task SendsTheCurrentFilterWithEveryLogin()
    {
        var first = Server();
        var second = Server();
        var connection = Connection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        await connection.SetFilterAsync("b/W1AW", TestContext.Current.CancellationToken);
        first.Close();
        await AdvanceUntil(() => connection.Reconnects == 1);

        Assert.EndsWith(" filter r/35/-97/200", first.Written[0], StringComparison.Ordinal);
        Assert.Equal("#filter b/W1AW", first.Written[1]);
        Assert.EndsWith(" filter b/W1AW", second.Written[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASendDuringAReconnectGoesOutOnTheNextConnection()
    {
        var first = Server();
        var second = Server();
        var connection = Connection();
        await connection.StartAsync(TestContext.Current.CancellationToken);
        first.Close();
        await WaitUntil(() => connection.State == AprsIsConnectionState.Reconnecting);

        var send = connection.SendAsync(Status("Back"), TestContext.Current.CancellationToken).AsTask();
        await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.False(send.IsCompleted);
        await AdvanceUntil(() => send.IsCompleted);
        await send;

        Assert.Single(first.Written); // only its login
        Assert.Equal("N0CALL-9>APZ123:>Back", second.Written[^1]);
    }

    [Fact]
    public async Task ReconnectsWhenNoPacketArrivesForTheMaximumSilence()
    {
        var first = Server();
        Server();
        var connection = Connection(o => o.MaxPacketSilence = TimeSpan.FromMinutes(10));
        await connection.StartAsync(TestContext.Current.CancellationToken);

        first.Server("# keepalive"); // keepalives do not count as packets
        await AdvanceUntil(() => connection.Reconnects == 1, maxSeconds: 900);

        var reconnecting = States.First(s => s.State == AprsIsConnectionState.Reconnecting);
        Assert.IsType<TimeoutException>(reconnecting.Error);
        Assert.StartsWith(
            "No packets from T2TEST1 for 600s (server keepalives in that time:",
            reconnecting.Error!.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ReconnectsWhenTheServerGoesSilent()
    {
        Server();
        Server();
        var connection = Connection(o => o.IdleTimeout = TimeSpan.FromSeconds(90));
        await connection.StartAsync(TestContext.Current.CancellationToken);

        await AdvanceUntil(() => connection.Reconnects == 1);

        Assert.IsType<TimeoutException>(States.First(s => s.State == AprsIsConnectionState.Reconnecting).Error);
    }

    [Fact]
    public async Task DisposingEndsTheStreamAndPendingSends()
    {
        var first = Server();
        var connection = Connection();
        await connection.StartAsync(TestContext.Current.CancellationToken);
        first.Close();
        await WaitUntil(() => connection.State == AprsIsConnectionState.Reconnecting);
        var send = connection.SendAsync(Status("Never"), TestContext.Current.CancellationToken).AsTask();

        await connection.DisposeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => send);
        Assert.Equal(AprsIsConnectionState.Closed, connection.State);
        await foreach (var line in connection.ReadLinesAsync(TestContext.Current.CancellationToken))
        {
            Assert.Fail($"unexpected {line}");
        }
    }

    [Fact]
    public async Task SendingOnAnUnverifiedLoginStillFails()
    {
        Server("unverified");
        var connection = Connection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await connection.SendAsync(Status("No"), TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task SendingBeforeStartFails()
    {
        var connection = Connection();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await connection.SendAsync(Status("No"), TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task AThrowingStateHandlerDoesNotStopReconnecting()
    {
        var first = Server();
        Server();
        var connection = Connection();
        connection.StateChanged += (_, _) => throw new InvalidOperationException("handler bug");
        await connection.StartAsync(TestContext.Current.CancellationToken);

        first.Close();
        await AdvanceUntil(() => connection.Reconnects == 1);

        Assert.Equal(AprsIsConnectionState.Connected, connection.State);
    }

    [Fact]
    public void OptionsRejectUnusableReconnectSettings()
    {
        static void Validate(Action<AprsIsClientOptions> configure)
        {
            var options = new AprsIsClientOptions { Callsign = "N0CALL" };
            configure(options);
            options.Validate();
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => Validate(o => o.ReconnectDelay = TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Validate(o =>
            {
                o.ReconnectDelay = TimeSpan.FromSeconds(30);
                o.MaxReconnectDelay = TimeSpan.FromSeconds(10);
            })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() => Validate(o => o.MaxPacketSilence = TimeSpan.Zero));
    }

    private sealed class UnreachableTransport : IAprsTransport
    {
        public ValueTask ConnectAsync(CancellationToken cancellationToken) =>
            throw new SocketException((int)SocketError.ConnectionRefused);

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
