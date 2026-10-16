using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace AprsNet.Gateway;

/// <summary>
/// Streams what an <see cref="AprsIsServer"/> publishes to WebSocket clients as JSON, one packet per text message in
/// the <c>AprsNet.Packets.Json</c> format, each client with its own APRS-IS filter. A browser or app can follow the same
/// feed as the server's APRS-IS clients, with the same history and station store.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GatewayWebServer"/> accepts WebSocket connections for the hub. In an ASP.NET Core app, accept the socket
/// there and hand it to <see cref="HandleAsync"/>:
/// <c>app.Map("/aprs", async context =&gt; await hub.HandleAsync(await context.WebSockets.AcceptWebSocketAsync(),
/// context.Request.Query["filter"], context.Request.Query["call"], context.RequestAborted));</c>
/// </para>
/// <para>
/// The protocol: the server first sends <c>{"server":NAME,"software":NAME VERSION,"filter":FILTER}</c>, then packets.
/// A client changes its filter by sending <c>{"filter":"r/35/-97/200"}</c>, and the server answers
/// <c>{"filter":…,"active":true}</c> or <c>{"filter":…,"error":…}</c>. Clients are read-only: anything else they send
/// gets an <c>{"error":…}</c> reply. A client whose queue fills is disconnected at once rather than slowing the
/// others.
/// </para>
/// </remarks>
public sealed class AprsWebSocketHub : IAsyncDisposable
{
    private readonly AprsIsServer _server;
    private readonly AprsWebSocketHubOptions _options;
    private readonly ConcurrentDictionary<WebSocketSession, TaskCompletionSource> _sessions = new();
    private readonly CancellationTokenSource _stopping = new();
    private long _overflowed;

    /// <summary>Creates a hub that streams what <paramref name="server"/> publishes.</summary>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public AprsWebSocketHub(AprsIsServer server, AprsWebSocketHubOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        options ??= new AprsWebSocketHubOptions();
        options.Validate();
        _server = server;
        _options = options;
    }

    /// <summary>WebSocket clients connected now.</summary>
    public int ClientCount => _sessions.Count;

    /// <summary>Clients closed so far because they fell too far behind.</summary>
    public long ClientsDroppedForOverflow => Interlocked.Read(ref _overflowed);

    /// <summary>Whether another client can connect now, under <see cref="AprsWebSocketHubOptions.MaxClients"/>.</summary>
    public bool HasRoom => _sessions.Count < _options.MaxClients;

    internal AprsIsServer Server => _server;

    /// <summary>
    /// Streams packets to an accepted WebSocket until it closes, the hub is disposed or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="socket">An open server-side WebSocket.</param>
    /// <param name="filter">The client's first filter, in APRS-IS syntax; none passes nothing but messages to it.</param>
    /// <param name="callsign">
    /// The client's callsign, if it gave one: messages addressed to it always pass, and <c>m/</c> measures from where
    /// it was last heard.
    /// </param>
    /// <param name="cancellationToken">Ends the session.</param>
    public async Task HandleAsync(
        WebSocket socket,
        string? filter = null,
        string? callsign = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(socket);
        ObjectDisposedException.ThrowIf(_stopping.IsCancellationRequested, this);
        if (!HasRoom)
        {
            GatewayMetrics.Disconnected(_server.Name, "server_full", wasConnected: false, GatewayMetrics.WebSocket);
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.EndpointUnavailable, "Server full")
                .ConfigureAwait(false);
            return;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        var session = new WebSocketSession(socket, this, _options, callsign);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessions[session] = ended;
        GatewayMetrics.Connected(_server.Name, GatewayMetrics.WebSocket);
        try
        {
            await session.RunAsync(filter, stop.Token).ConfigureAwait(false);
        }
        finally
        {
            _server.Unsubscribe(session);
            _sessions.TryRemove(session, out _);
            if (session.Overflowed)
            {
                Interlocked.Increment(ref _overflowed);
            }

            var reason = _stopping.IsCancellationRequested ? "server_stopping" : session.EndReason;
            GatewayMetrics.Disconnected(_server.Name, reason, wasConnected: true, GatewayMetrics.WebSocket);
            ended.SetResult();
        }
    }

    /// <summary>Closes every client.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_sessions.Values.Select(e => e.Task)).ConfigureAwait(false);
        _stopping.Dispose();
    }

    internal static async Task CloseQuietlyAsync(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        try
        {
            await socket
                .CloseOutputAsync(status, reason, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(2), TimeProvider.System)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is WebSocketException or TimeoutException or IOException)
        {
            // It left first.
        }

        socket.Abort();
    }
}

/// <summary>Settings for <see cref="AprsWebSocketHub"/>.</summary>
public sealed class AprsWebSocketHubOptions
{
    /// <summary>The most WebSocket clients at once; more are closed with status 1001. Defaults to 100.</summary>
    public int MaxClients { get; set; } = 100;

    /// <summary>
    /// Packets that may wait for one client before it is closed as too slow. Defaults to 1,000, as for APRS-IS clients.
    /// </summary>
    public int ClientQueueLength { get; set; } = 1000;

    /// <summary>The longest message a client may send, in bytes; longer ones close it. Defaults to 4096.</summary>
    public int MaxMessageBytes { get; set; } = 4096;

    /// <summary>Throws <see cref="ArgumentException"/> if a setting is unusable.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxClients, 1, nameof(MaxClients));
        ArgumentOutOfRangeException.ThrowIfLessThan(ClientQueueLength, 1, nameof(ClientQueueLength));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxMessageBytes, 64, nameof(MaxMessageBytes));
    }
}
