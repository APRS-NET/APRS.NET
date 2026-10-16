using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using AprsNet.AprsIsTcpClient.Tests;
using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

/// <summary>
/// <see cref="KissTcpConnection"/> over real sockets, through a proxy that drops, stalls and refuses connections, with
/// the reconnect waits on a fake clock.
/// </summary>
public sealed class ReconnectThroughProxyTests : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly double[] Backoff = [5, 10, 20, 40, 60, 60];
    private readonly FakeTimeProvider _time = new();
    private readonly TncListener _tnc = new();
    private readonly ConcurrentQueue<KissConnectionStateChangedEventArgs> _lost = new();
    private ChaosProxy _proxy = null!;
    private KissTcpConnection _connection = null!;
    private IAsyncEnumerator<AprsPacket> _packets = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _proxy = new ChaosProxy(_tnc.EndPoint);
        _connection = new KissTcpConnection(
            new KissTcpOptions
            {
                Host = "127.0.0.1",
                Port = _proxy.EndPoint.Port,
                TimeProvider = _time,
            }
        );
        _connection.StateChanged += (_, e) =>
        {
            if (e.State == KissConnectionState.Reconnecting)
            {
                _lost.Enqueue(e);
            }
        };
        _packets = _connection.ReadPacketsAsync(Token).GetAsyncEnumerator(Token);
        var first = _packets.MoveNextAsync().AsTask();
        await WaitUntil(() => _tnc.Connections == 1);
        _tnc.Send("N1ABC>APRS:>first");
        Assert.True(await first.WaitAsync(Wait, Token));
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        try
        {
            await _packets.DisposeAsync();
        }
        catch (OperationCanceledException)
        {
            // A read still waiting when the test ended.
        }

        await _proxy.DisposeAsync();
        await _tnc.DisposeAsync();
    }

    [Fact]
    public async Task ADroppedConnectionReconnectsOnceAndReadingCarriesOn()
    {
        var next = _packets.MoveNextAsync().AsTask();
        _proxy.DropAll();
        await WaitUntil(() => _lost.Count == 1);
        Assert.Equal(TimeSpan.FromSeconds(5), _lost.Single().RetryIn!.Value);
        Assert.False(_connection.IsConnected);

        await AdvanceUntil(TimeSpan.FromSeconds(5), () => _tnc.Connections == 2);
        _tnc.Send("N1ABC>APRS:>after");

        Assert.True(await next.WaitAsync(Wait, Token));
        Assert.Equal("N1ABC>APRS:>after", _packets.Current.ToString());
        Assert.Equal(1, _connection.Reconnects);
        Assert.Single(_lost);
    }

    [Fact]
    public async Task AQuietLinkIsNotTakenForALostOne()
    {
        // KISS has no keepalive and a radio channel can be quiet for hours, so a stall alone does not reconnect.
        var next = _packets.MoveNextAsync().AsTask();
        _proxy.Stall();
        _time.Advance(TimeSpan.FromHours(2));
        await Task.Delay(100, Token);

        Assert.Empty(_lost);
        Assert.True(_connection.IsConnected);

        // Once the link is cut, it reconnects as usual.
        _proxy.DropAll();
        await WaitUntil(() => _lost.Count == 1);
        await AdvanceUntil(_lost.Single().RetryIn!.Value, () => _tnc.Connections == 2);
        _tnc.Send("N1ABC>APRS:>after the stall");
        Assert.True(await next.WaitAsync(Wait, Token));
        Assert.Equal("N1ABC>APRS:>after the stall", _packets.Current.ToString());
    }

    [Fact]
    public async Task RefusedAttemptsBackOffUpToTheLimitThenRecover()
    {
        var next = _packets.MoveNextAsync().AsTask();
        _proxy.Refusing = true;
        _proxy.DropAll();
        for (var attempt = 1; attempt <= Backoff.Length; attempt++)
        {
            await WaitUntil(() => _lost.Count == attempt);
            await AdvanceUntil(_lost.Last().RetryIn!.Value, () => _proxy.Refused == attempt);
        }

        await WaitUntil(() => _lost.Count == Backoff.Length + 1);
        _proxy.Refusing = false;
        await AdvanceUntil(_lost.Last().RetryIn!.Value, () => _tnc.Connections == 2);
        _tnc.Send("N1ABC>APRS:>back");

        Assert.True(await next.WaitAsync(Wait, Token));
        Assert.Equal("N1ABC>APRS:>back", _packets.Current.ToString());
        Assert.Equal([.. Backoff, 60], _lost.Select(e => e.RetryIn!.Value.TotalSeconds));
        // A refused attempt counts as a connection when the reset comes after the connect completes, and as a failed
        // connect when it comes during it; the waits are the same either way.
        Assert.InRange(_connection.Reconnects, 1, Backoff.Length + 1);
    }

    /// <summary>
    /// Advances the clock by <paramref name="step"/> until <paramref name="done"/>. The event announcing a wait comes
    /// just before the wait starts, so a first advance can land before it; then the wait needs another.
    /// </summary>
    private async Task AdvanceUntil(TimeSpan step, Func<bool> done)
    {
        var deadline = DateTime.UtcNow + Wait;
        _time.Advance(step);
        for (var waited = 0; !done(); waited++)
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting.");
            await Task.Delay(10, Token);
            if (waited % 20 == 19)
            {
                _time.Advance(step);
            }
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting.");
            await Task.Delay(10, Token);
        }
    }

    /// <summary>A TNC serving KISS over TCP: sends frames to whoever is connected.</summary>
    private sealed class TncListener : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();
        private readonly ConcurrentQueue<TcpClient> _clients = new();
        private readonly Task _accepting;
        private int _connections;

        public TncListener()
        {
            _listener.Start();
            _accepting = AcceptAsync();
        }

        public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

        public int Connections => Volatile.Read(ref _connections);

        public void Send(string line)
        {
            var bytes = KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line))).Encode();
            foreach (var client in _clients)
            {
                try
                {
                    client.GetStream().Write(bytes);
                }
                catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
                {
                    // That connection was cut.
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();
            await _accepting;
            foreach (var client in _clients)
            {
                client.Dispose();
            }

            _listener.Dispose();
            _stopping.Dispose();
        }

        private async Task AcceptAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                try
                {
                    _clients.Enqueue(await _listener.AcceptTcpClientAsync(_stopping.Token));
                    Interlocked.Increment(ref _connections);
                }
                catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }
            }
        }
    }
}
