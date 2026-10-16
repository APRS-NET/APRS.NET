using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.AprsIsTcpClient.Tests;

/// <summary>
/// <see cref="AprsIsConnection"/> over real sockets, through a proxy that drops, stalls and refuses connections. Waits
/// run on a fake clock, so the backoff and idle timeout are checked exactly without waiting for them.
/// </summary>
public sealed class ReconnectThroughProxyTests : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // 5 s doubling to the 60 s limit.
    private static readonly int[] Backoff = [5, 10, 20, 40, 60, 60];
    private readonly FakeTimeProvider _time = new();
    private readonly FakeAprsIsServer _server = new();
    private readonly ConcurrentQueue<AprsIsConnectionStateChangedEventArgs> _events = new();
    private ChaosProxy _proxy = null!;
    private AprsIsConnection _connection = null!;
    private IAsyncEnumerator<string> _lines = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _proxy = new ChaosProxy(_server.EndPoint);
        _connection = new AprsIsConnection(
            new AprsIsClientOptions
            {
                Endpoint = new AprsIsEndpoint("127.0.0.1", _proxy.EndPoint.Port),
                Callsign = "N0CALL",
                Filter = "r/39/-95/100",
                TimeProvider = _time,
            }
        );
        _connection.StateChanged += (_, e) => _events.Enqueue(e);
        await _connection.StartAsync(Token);
        _lines = _connection.ReadLinesAsync(Token).GetAsyncEnumerator(Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _lines.DisposeAsync();
        await _connection.DisposeAsync();
        await _proxy.DisposeAsync();
        await _server.DisposeAsync();
    }

    private AprsIsConnectionState[] States => [.. _events.Select(e => e.State)];

    [Fact]
    public async Task ADroppedConnectionReconnectsAndReadingCarriesOn()
    {
        _server.Broadcast("N1ABC>APRS:>before");
        Assert.Equal("N1ABC>APRS:>before", await NextLineAsync());

        _proxy.DropAll();
        // A cut link reads as a reset or as a clean close, depending on timing; either way it is one reconnect.
        var reconnecting = await NextEventAsync(AprsIsConnectionState.Reconnecting);
        AssertNear(5, reconnecting.RetryIn!.Value);
        await AdvanceUntilEventAsync(reconnecting.RetryIn.Value, AprsIsConnectionState.Connected);
        _server.Broadcast("N1ABC>APRS:>after");

        Assert.Equal("N1ABC>APRS:>after", await NextLineAsync());
        Assert.Equal(
            [
                AprsIsConnectionState.Connecting,
                AprsIsConnectionState.Connected,
                AprsIsConnectionState.Reconnecting,
                AprsIsConnectionState.Connecting,
                AprsIsConnectionState.Connected,
            ],
            States
        );
        Assert.Equal(1, _connection.Reconnects);
        Assert.Equal(2, _server.Logins);
    }

    [Fact]
    public async Task AStalledLinkIsNoticedByTheIdleTimeout()
    {
        _server.Broadcast("N1ABC>APRS:>before");
        Assert.Equal("N1ABC>APRS:>before", await NextLineAsync());

        _proxy.Stall();
        _server.Broadcast("N1ABC>APRS:>lost in the stall");
        await _server.WaitUntilSentAsync();
        _time.Advance(TimeSpan.FromSeconds(89));
        await Task.Delay(100, Token);
        Assert.Equal(AprsIsConnectionState.Connected, _connection.State);
        _time.Advance(TimeSpan.FromSeconds(1));
        var reconnecting = await NextEventAsync(AprsIsConnectionState.Reconnecting);
        Assert.IsType<TimeoutException>(reconnecting.Error);
        await AdvanceUntilEventAsync(reconnecting.RetryIn!.Value, AprsIsConnectionState.Connected);
        _server.Broadcast("N1ABC>APRS:>after");

        Assert.Equal("N1ABC>APRS:>after", await NextLineAsync());
        Assert.Equal(1, _connection.Reconnects);
    }

    [Fact]
    public async Task RefusedAttemptsBackOffUpToTheLimitThenRecover()
    {
        _proxy.Refusing = true;
        _proxy.DropAll();
        var waits = new List<TimeSpan>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var reconnecting = await NextEventAsync(AprsIsConnectionState.Reconnecting);
            waits.Add(reconnecting.RetryIn!.Value);
            await AdvanceUntilEventAsync(reconnecting.RetryIn.Value, AprsIsConnectionState.Connecting);
        }

        // Each wait varies by up to 20%.
        foreach (var (wait, expected) in waits.Zip(Backoff))
        {
            AssertNear(expected, wait);
        }

        // The sixth attempt is refused too, before the proxy lets the next one through.
        var last = await NextEventAsync(AprsIsConnectionState.Reconnecting);
        _proxy.Refusing = false;
        Assert.Equal(6, _proxy.Refused);
        await AdvanceUntilEventAsync(last.RetryIn!.Value, AprsIsConnectionState.Connected);
        _server.Broadcast("N1ABC>APRS:>back");
        Assert.Equal("N1ABC>APRS:>back", await NextLineAsync());
        Assert.Equal(1, _connection.Reconnects);
        Assert.Equal(7, States.Count(s => s == AprsIsConnectionState.Reconnecting));
    }

    [Fact]
    public async Task AConnectionThatHeldStartsTheBackoffAgain()
    {
        _proxy.Refusing = true;
        _proxy.DropAll();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var reconnecting = await NextEventAsync(AprsIsConnectionState.Reconnecting);
            await AdvanceUntilEventAsync(reconnecting.RetryIn!.Value, AprsIsConnectionState.Connecting);
        }

        var recovering = await NextEventAsync(AprsIsConnectionState.Reconnecting);
        _proxy.Refusing = false;
        AssertNear(40, recovering.RetryIn!.Value);
        await AdvanceUntilEventAsync(recovering.RetryIn.Value, AprsIsConnectionState.Connected);

        // Held for a minute, with traffic so the idle timeout does not fire.
        for (var i = 0; i < 3; i++)
        {
            _server.Broadcast($"N1ABC>APRS:>tick {i}");
            Assert.Equal($"N1ABC>APRS:>tick {i}", await NextLineAsync());
            _time.Advance(TimeSpan.FromSeconds(20));
        }

        _proxy.DropAll();
        AssertNear(5, (await NextEventAsync(AprsIsConnectionState.Reconnecting)).RetryIn!.Value);
    }

    /// <summary>
    /// Advances the clock by <paramref name="step"/> until the next event in <paramref name="state"/>. The event
    /// announcing a wait comes just before the wait starts, so a first advance can land before it; then the wait needs
    /// another.
    /// </summary>
    private async Task<AprsIsConnectionStateChangedEventArgs> AdvanceUntilEventAsync(
        TimeSpan step,
        AprsIsConnectionState state
    )
    {
        _time.Advance(step);
        for (var attempt = 0; ; attempt++)
        {
            var seen = _seen;
            var events = _events.ToArray();
            for (; seen < events.Length; seen++)
            {
                if (events[seen].State == state)
                {
                    _seen = seen + 1;
                    return events[seen];
                }
            }

            Assert.True(
                attempt < Wait.TotalMilliseconds / 10,
                $"Timed out waiting for {state}; saw {string.Join(", ", States)}."
            );
            await Task.Delay(10, Token);
            if (attempt % 20 == 19)
            {
                _time.Advance(step);
            }
        }
    }

    private static void AssertNear(double seconds, TimeSpan actual) =>
        Assert.InRange(actual.TotalSeconds, seconds * 0.8, seconds * 1.2);

    private async Task<string> NextLineAsync()
    {
        Assert.True(await _lines.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
        return _lines.Current;
    }

    /// <summary>Waits for the next event in <paramref name="state"/>, checking each event is seen once.</summary>
    private int _seen;

    private async Task<AprsIsConnectionStateChangedEventArgs> NextEventAsync(AprsIsConnectionState state)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (true)
        {
            var events = _events.ToArray();
            for (; _seen < events.Length; _seen++)
            {
                if (events[_seen].State == state)
                {
                    return events[_seen++];
                }
            }

            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {state}; saw {string.Join(", ", States)}.");
            await Task.Delay(10, Token);
        }
    }

    /// <summary>A minimal APRS-IS server: accepts any login and sends each broadcast line to every logged-in client.</summary>
    private sealed class FakeAprsIsServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();
        private readonly ConcurrentDictionary<TcpClient, SemaphoreSlim> _sessions = new();
        private readonly Task _accepting;
        private int _logins;
        private int _sending;

        public FakeAprsIsServer()
        {
            _listener.Start();
            _accepting = AcceptAsync();
        }

        public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

        public int Logins => Volatile.Read(ref _logins);

        public void Broadcast(string line)
        {
            var bytes = Encoding.Latin1.GetBytes(line + "\r\n");
            foreach (var (tcp, gate) in _sessions)
            {
                Interlocked.Increment(ref _sending);
                _ = SendAsync(tcp, gate, bytes);
            }
        }

        public async Task WaitUntilSentAsync()
        {
            var deadline = DateTime.UtcNow + Wait;
            while (Volatile.Read(ref _sending) > 0)
            {
                Assert.True(DateTime.UtcNow < deadline, "Timed out sending.");
                await Task.Delay(10);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();
            await _accepting;
            foreach (var tcp in _sessions.Keys)
            {
                tcp.Dispose();
            }

            _listener.Dispose();
            _stopping.Dispose();
        }

        private async Task SendAsync(TcpClient tcp, SemaphoreSlim gate, byte[] bytes)
        {
            await gate.WaitAsync();
            try
            {
                await tcp.GetStream().WriteAsync(bytes);
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
            {
                _sessions.TryRemove(tcp, out _);
            }
            finally
            {
                gate.Release();
                Interlocked.Decrement(ref _sending);
            }
        }

        private async Task AcceptAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = await _listener.AcceptTcpClientAsync(_stopping.Token);
                }
                catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }

                _ = LogInAsync(tcp);
            }
        }

        private async Task LogInAsync(TcpClient tcp)
        {
            try
            {
                var stream = tcp.GetStream();
                await stream.WriteAsync("# aprsc 2.1.test\r\n"u8.ToArray());
                var reader = new StreamReader(stream, Encoding.Latin1);
                var login = await reader.ReadLineAsync();
                if (login is null || !login.StartsWith("user ", StringComparison.Ordinal))
                {
                    tcp.Dispose();
                    return;
                }

                await stream.WriteAsync("# logresp N0CALL unverified, server T2CHAOS\r\n"u8.ToArray());
                Interlocked.Increment(ref _logins);
                _sessions[tcp] = new SemaphoreSlim(1, 1);
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
            {
                tcp.Dispose();
            }
        }
    }
}
