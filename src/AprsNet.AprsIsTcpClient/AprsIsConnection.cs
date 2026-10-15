using System.Threading.Channels;
using AprsNet.Packets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AprsNet.AprsIsTcpClient;

/// <summary>Where an <see cref="AprsIsConnection"/> is in its life.</summary>
public enum AprsIsConnectionState
{
    /// <summary>Not started yet.</summary>
    NotStarted,

    /// <summary>Connecting and logging in.</summary>
    Connecting,

    /// <summary>Logged in; lines are flowing and packets can be sent.</summary>
    Connected,

    /// <summary>The connection ended and the next attempt is waiting; <see cref="AprsIsConnectionStateChangedEventArgs.RetryIn"/> says how long.</summary>
    Reconnecting,

    /// <summary>Disposed. The line stream has ended.</summary>
    Closed,
}

/// <summary>A change of <see cref="AprsIsConnection.State"/>.</summary>
public sealed class AprsIsConnectionStateChangedEventArgs : EventArgs
{
    internal AprsIsConnectionStateChangedEventArgs(
        AprsIsConnectionState state,
        LoginResult? login,
        Exception? error,
        TimeSpan? retryIn
    )
    {
        State = state;
        Login = login;
        Error = error;
        RetryIn = retryIn;
    }

    /// <summary>The new state.</summary>
    public AprsIsConnectionState State { get; }

    /// <summary>The server's login response, when the state is <see cref="AprsIsConnectionState.Connected"/>.</summary>
    public LoginResult? Login { get; }

    /// <summary>
    /// What ended the previous connection or attempt, when the state is <see cref="AprsIsConnectionState.Reconnecting"/>:
    /// <see langword="null"/> when the server closed the connection cleanly.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>How long until the next attempt, when the state is <see cref="AprsIsConnectionState.Reconnecting"/>.</summary>
    public TimeSpan? RetryIn { get; }
}

/// <summary>
/// A long-lived APRS-IS connection that reconnects by itself. It wraps a series of single-use
/// <see cref="AprsIsClient"/>s: when one ends (the server closes, the link fails, the server goes silent for
/// <see cref="AprsIsClientOptions.IdleTimeout"/>, or no packet arrives for
/// <see cref="AprsIsClientOptions.MaxPacketSilence"/>), it waits <see cref="AprsIsClientOptions.ReconnectDelay"/>,
/// doubling up to <see cref="AprsIsClientOptions.MaxReconnectDelay"/> with jitter, and logs in again.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReadLinesAsync"/> is one stream across reconnects, <see cref="SendAsync"/> waits while the connection is
/// down, and a filter set with <see cref="SetFilterAsync"/> is sent again on every new connection. One send rate limit
/// applies across reconnects.
/// </para>
/// <para>
/// Only the first connection fails fast: <see cref="StartAsync"/> throws if the server cannot be reached or refuses the
/// login, so wrong settings are reported at once. After that, every failure is retried until the connection is
/// disposed.
/// </para>
/// </remarks>
public sealed class AprsIsConnection : IAsyncDisposable
{
    private readonly AprsIsClientOptions _options;
    private readonly ILogger _logger;
    private readonly Func<IAprsTransport>? _transportFactory;
    private readonly Channel<string> _lines;
    private readonly SendRateLimiter _sendLimiter;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _stateLock = new();
    private TaskCompletionSource<AprsIsClient> _connected = NewConnectedSignal();
    private AprsIsClient? _client;
    private string? _filter;
    private Task? _run;
    private int _started;
    private int _disposed;

    /// <summary>Creates a connection; nothing happens until <see cref="StartAsync"/>.</summary>
    /// <param name="options">Server, login and reconnect settings. The connection keeps its own copy of the filter.</param>
    /// <param name="logger">Receives connection and reconnect events.</param>
    /// <param name="transportFactory">Makes the transport for each connection, in place of TCP; for tests.</param>
    public AprsIsConnection(
        AprsIsClientOptions options,
        ILogger? logger = null,
        Func<IAprsTransport>? transportFactory = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _filter = options.Filter;
        _logger = logger ?? NullLogger.Instance;
        _transportFactory = transportFactory;
        _sendLimiter = new SendRateLimiter(options.SendBurst, options.SendInterval, options.TimeProvider);
        _lines = Channel.CreateBounded<string>(
            new BoundedChannelOptions(options.ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode =
                    options.OverflowPolicy == OverflowPolicy.Block
                        ? BoundedChannelFullMode.Wait
                        : BoundedChannelFullMode.DropOldest,
            },
            _ =>
            {
                ClientMetrics.QueueFull();
                _logger.QueueFull();
            }
        );
    }

    /// <summary>
    /// Raised when <see cref="State"/> changes, on the connection's own task: keep handlers short. An exception
    /// thrown by a handler is logged and ignored.
    /// </summary>
    public event EventHandler<AprsIsConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>Where the connection is in its life.</summary>
    public AprsIsConnectionState State { get; private set; } = AprsIsConnectionState.NotStarted;

    /// <summary>The current connection's login response, or <see langword="null"/> while not connected.</summary>
    public LoginResult? Login => State == AprsIsConnectionState.Connected ? _client?.Login : null;

    /// <summary>How many times the connection has been re-established after the first login.</summary>
    public int Reconnects { get; private set; }

    /// <summary>Connects and logs in for the first time, then keeps the connection up until disposed.</summary>
    /// <exception cref="LoginRejectedException">The server refused the login.</exception>
    /// <exception cref="LoginException">The server closed the connection, did not answer in time, or answered unreadably.</exception>
    /// <exception cref="InvalidOperationException">The connection has already been started.</exception>
    public async Task<LoginResult> StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("This connection has already been started.");
        }

        SetState(AprsIsConnectionState.Connecting);
        var client = CreateClient();
        try
        {
            using var start = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            var login = await client.ConnectAsync(start.Token).ConfigureAwait(false);
            OnConnected(client);
            _run = Task.Run(() => RunAsync(client), CancellationToken.None);
            return login;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            SetState(AprsIsConnectionState.Closed);
            _lines.Writer.TryComplete();
            throw;
        }
    }

    /// <summary>
    /// Yields packet lines from every connection in turn, until the connection is disposed. Reconnects do not end the
    /// stream. Intended for a single consumer.
    /// </summary>
    public IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken = default) =>
        _lines.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Yields each packet line parsed, across reconnects, as <see cref="ReadLinesAsync"/> yields the lines; use one or
    /// the other. Parsing never throws: a malformed line comes back with <see cref="AprsPacket.Outcome"/> and
    /// <see cref="AprsPacket.Issues"/> saying why. Each packet is counted in the
    /// <c>aprsnet.aprsis.packets.received</c> metric by type and outcome.
    /// </summary>
    public IAsyncEnumerable<AprsPacket> ReadPacketsAsync(CancellationToken cancellationToken = default) =>
        PacketStream.ParseAsync(ReadLinesAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Sends a packet as <see cref="AprsIsClient.SendAsync"/> does. While the connection is down it waits for the next
    /// one, so a beacon sent during a reconnect goes out once the login succeeds; cancel the token to give up.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The connection has not been started or is closed, or the login is not verified.
    /// </exception>
    /// <exception cref="ArgumentException">The packet cannot be sent; see <see cref="AprsIsClient.SendAsync"/>.</exception>
    public async ValueTask SendAsync(AprsPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        while (true)
        {
            var client = await WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await client.SendAsync(packet, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
                when (ex is InvalidOperationException or ObjectDisposedException && client.IsConnectionEnded)
            {
                // The connection ended before the line was written: wait for the next one and send it there.
            }
        }
    }

    /// <summary>
    /// Changes the server-side filter now and for every later connection. While the connection is down the new filter
    /// is sent with the next login.
    /// </summary>
    /// <exception cref="AprsValidationException">The filter holds a control character.</exception>
    public async ValueTask SetFilterAsync(string filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (AprsIsClientOptions.FilterError(filter, nameof(filter)) is { } error)
        {
            throw error;
        }

        _filter = filter;
        if (State == AprsIsConnectionState.Connected && _client is { IsConnectionEnded: false } client)
        {
            try
            {
                await client.SetFilterAsync(filter, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException && client.IsConnectionEnded)
            {
                // The connection dropped meanwhile; the next login carries the filter.
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        if (_run is not null)
        {
            await _run.ConfigureAwait(false); // the loop catches its own exceptions
        }

        _connected.TrySetException(new ObjectDisposedException(nameof(AprsIsConnection)));
        _lines.Writer.TryComplete();
        SetState(AprsIsConnectionState.Closed);
        _sendLimiter.Dispose();
        _shutdown.Dispose();
    }

    private AprsIsClient CreateClient() =>
        new(_options.WithFilter(_filter), _logger, _transportFactory?.Invoke(), _sendLimiter);

    private async Task RunAsync(AprsIsClient client)
    {
        var shutdown = _shutdown.Token;
        var delay = _options.ReconnectDelay;
        try
        {
            while (true)
            {
                var connectedAt = _options.TimeProvider.GetTimestamp();
                var (cause, error) = await PumpAsync(client, shutdown).ConfigureAwait(false);
                OnDisconnected();
                await client.DisposeAsync().ConfigureAwait(false);
                if (shutdown.IsCancellationRequested)
                {
                    return;
                }

                // A connection that held for a while was not part of a failing streak: start the backoff again.
                if (_options.TimeProvider.GetElapsedTime(connectedAt) >= _options.MaxReconnectDelay)
                {
                    delay = _options.ReconnectDelay;
                }

                var attempts = 0;
                while (true)
                {
                    var wait = Jitter(delay);
                    attempts++;
                    ClientMetrics.Reconnect(cause);
                    _logger.Reconnecting(_options.Endpoint, cause, wait.TotalSeconds, error);
                    SetState(AprsIsConnectionState.Reconnecting, error: error, retryIn: wait);
                    await Task.Delay(wait, _options.TimeProvider, shutdown).ConfigureAwait(false);
                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxReconnectDelay.Ticks));

                    SetState(AprsIsConnectionState.Connecting);
                    client = CreateClient();
                    try
                    {
                        await client.ConnectAsync(shutdown).ConfigureAwait(false);
                        break;
                    }
                    catch (Exception ex) when (!shutdown.IsCancellationRequested)
                    {
                        await client.DisposeAsync().ConfigureAwait(false);
                        (cause, error) = ("connect_failed", ex);
                    }
                }

                Reconnects++;
                _logger.Reconnected(_options.Endpoint, attempts);
                OnConnected(client);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Moves one connection's lines into the shared stream; returns why it ended.</summary>
    private async Task<(string Cause, Exception? Error)> PumpAsync(AprsIsClient client, CancellationToken shutdown)
    {
        // The watchdog runs on the configured clock, so tests (and callers with their own TimeProvider) drive it.
        using var silence = new CancellationTokenSource(Timeout.InfiniteTimeSpan, _options.TimeProvider);
        using var read = CancellationTokenSource.CreateLinkedTokenSource(shutdown, silence.Token);
        var maxSilence = _options.MaxPacketSilence;
        var keepalivesAtLastPacket = client.ServerCommentsReceived;
        try
        {
            if (maxSilence is { } armed)
            {
                silence.CancelAfter(armed);
            }

            await foreach (var line in client.ReadLinesAsync(read.Token).ConfigureAwait(false))
            {
                if (maxSilence is { } rearm)
                {
                    silence.CancelAfter(rearm); // restart the wait for the next packet
                    keepalivesAtLastPacket = client.ServerCommentsReceived;
                }

                await _lines.Writer.WriteAsync(line, shutdown).ConfigureAwait(false);
            }

            return ("server_closed", null);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return ("shutdown", null);
        }
        catch (OperationCanceledException) when (silence.IsCancellationRequested)
        {
            // Keepalives in the gap tell the two failures apart: some means the server's feed stalled while the link
            // stayed up, none means the link itself died.
            var keepalives = client.ServerCommentsReceived - keepalivesAtLastPacket;
            var server = client.Login?.Server ?? _options.Endpoint.ToString();
            return (
                "packet_silence",
                new TimeoutException(
                    $"No packets from {server} for {maxSilence!.Value.TotalSeconds:0.#}s "
                        + $"(server keepalives in that time: {keepalives})."
                )
            );
        }
        catch (TimeoutException ex)
        {
            return ("keepalive_timeout", ex);
        }
        catch (Exception ex)
        {
            return ("socket_error", ex);
        }
    }

    private async ValueTask<AprsIsClient> WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (State == AprsIsConnectionState.NotStarted)
        {
            throw new InvalidOperationException("Start the connection before sending.");
        }

        Task<AprsIsClient> connected;
        lock (_stateLock)
        {
            connected = _connected.Task;
        }

        try
        {
            return await connected.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            throw new InvalidOperationException("The connection is closed.");
        }
    }

    private void OnConnected(AprsIsClient client)
    {
        lock (_stateLock)
        {
            _client = client;
            _connected.TrySetResult(client);
        }

        SetState(AprsIsConnectionState.Connected, client.Login);
    }

    private void OnDisconnected()
    {
        lock (_stateLock)
        {
            if (_connected.Task.IsCompleted)
            {
                _connected = NewConnectedSignal();
            }
        }
    }

    private void SetState(
        AprsIsConnectionState state,
        LoginResult? login = null,
        Exception? error = null,
        TimeSpan? retryIn = null
    )
    {
        State = state;
        try
        {
            StateChanged?.Invoke(this, new AprsIsConnectionStateChangedEventArgs(state, login, error, retryIn));
        }
        catch (Exception ex)
        {
            _logger.StateHandlerFailed(ex);
        }
    }

    /// <summary>The delay varied by up to 20% either way, so clients dropped together do not return together.</summary>
    private static TimeSpan Jitter(TimeSpan delay) => delay * (0.8 + (Random.Shared.NextDouble() * 0.4));

    private static TaskCompletionSource<AprsIsClient> NewConnectedSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
