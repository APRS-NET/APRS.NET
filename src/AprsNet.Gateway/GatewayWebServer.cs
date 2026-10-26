using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace AprsNet.Gateway;

/// <summary>
/// A small web server for the gateway: it accepts WebSocket connections on
/// <see cref="GatewayWebServerOptions.WebSocketPath"/> and hands them to an <see cref="AprsWebSocketHub"/>, and, with
/// <see cref="GatewayWebServerOptions.Health"/>, answers health checks on
/// <see cref="GatewayWebServerOptions.HealthPath"/>. It needs no ASP.NET Core; an app that already runs ASP.NET Core
/// can call <see cref="AprsWebSocketHub.HandleAsync"/> and <see cref="GatewayHealth.Check"/> from its own endpoints
/// instead.
/// </summary>
/// <remarks>
/// A client connects to <c>ws://host:port/aprs?filter=r/35/-97/200&amp;call=N0CALL</c>; both query parameters are
/// optional. It speaks only enough HTTP/1.1 for that: other requests get a short error response, and every connection
/// closes after one request. It does not serve TLS; put a reverse proxy in front of it for <c>wss://</c>.
/// </remarks>
public sealed class GatewayWebServer : IAsyncDisposable
{
    // RFC 6455's fixed GUID for the Sec-WebSocket-Accept hash.
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const int MaxRequestBytes = 8192;

    private readonly AprsWebSocketHub? _hub;
    private readonly GatewayWebServerOptions _options;
    private readonly ConcurrentDictionary<Task, byte> _connections = new();
    private readonly CancellationTokenSource _stopping = new();
    private TcpListener? _listener;
    private Task? _accepting;

    /// <summary>Creates a server for <paramref name="hub"/>; <see cref="Start"/> begins listening.</summary>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public GatewayWebServer(AprsWebSocketHub hub, GatewayWebServerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(hub);
        options ??= new GatewayWebServerOptions();
        options.Validate();
        _hub = hub;
        _options = options;
    }

    /// <summary>
    /// Creates a server that only answers health checks, for a service with no packets to stream, such as one sharing a
    /// TNC; <see cref="Start"/> begins listening. The WebSocket path answers 404.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <see cref="GatewayWebServerOptions.Health"/> isn't set, or a setting in <paramref name="options"/> is unusable.
    /// </exception>
    public GatewayWebServer(GatewayWebServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (options.Health is null)
        {
            throw new ArgumentException("A server without a hub needs a health check to answer.", nameof(options));
        }

        _options = options;
    }

    /// <summary>
    /// The address the server is listening on, once started. With port 0 in
    /// <see cref="GatewayWebServerOptions.EndPoint"/>, this gives the port the system chose.
    /// </summary>
    public IPEndPoint? LocalEndPoint => (IPEndPoint?)_listener?.LocalEndpoint;

    /// <summary>Starts listening.</summary>
    /// <exception cref="InvalidOperationException">The server has already been started.</exception>
    /// <exception cref="SocketException">The endpoint cannot be bound, for example because it is in use.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_stopping.IsCancellationRequested, this);
        if (_listener is not null)
        {
            throw new InvalidOperationException("The server has already been started.");
        }

        _listener = new TcpListener(_options.EndPoint);
        _listener.Start();
        _accepting = AcceptAsync(_listener, _stopping.Token);
    }

    /// <summary>Stops listening and closes every connection. The hub is not disposed.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();
        if (_accepting is not null)
        {
            await _accepting.ConfigureAwait(false);
        }

        await Task.WhenAll(_connections.Keys).ConfigureAwait(false);
        _listener?.Dispose();
        _stopping.Dispose();
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await listener.AcceptTcpClientAsync(stopping).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            var run = new Task<Task>(() => ServeAsync(tcp, stopping));
            var task = run.Unwrap();
            _connections[task] = 0;
            _ = task.ContinueWith(t => _connections.TryRemove(t, out _), TaskScheduler.Default);
            run.Start(TaskScheduler.Default);
        }
    }

    private async Task ServeAsync(TcpClient tcp, CancellationToken stopping)
    {
        using (tcp)
        {
            var stream = tcp.GetStream();
            try
            {
                var request = await ReadRequestAsync(stream, stopping).ConfigureAwait(false);
                if (request is null)
                {
                    await RespondAsync(stream, 400, "Bad Request", stopping).ConfigureAwait(false);
                    return;
                }

                await HandleAsync(stream, request, stopping).ConfigureAwait(false);
            }
            catch (Exception e)
                when (e is OperationCanceledException or IOException or SocketException or WebSocketException)
            {
                // The client left, or the server is stopping.
            }
        }
    }

    private async Task HandleAsync(NetworkStream stream, HttpRequest request, CancellationToken stopping)
    {
        if (request.Method != "GET")
        {
            await RespondAsync(stream, 405, "Method Not Allowed", stopping).ConfigureAwait(false);
            return;
        }

        if (_options.Health is { } health && request.Path == _options.HealthPath)
        {
            await RespondHealthAsync(stream, health.Check(), stopping).ConfigureAwait(false);
            return;
        }

        if (_hub is null || request.Path != _options.WebSocketPath)
        {
            await RespondAsync(stream, 404, "Not Found", stopping).ConfigureAwait(false);
            return;
        }

        if (
            !request.HeaderHas("Upgrade", "websocket")
            || !request.HeaderHas("Connection", "upgrade")
            || request.Header("Sec-WebSocket-Key") is not { Length: > 0 } key
        )
        {
            await RespondAsync(
                    stream,
                    426,
                    "Upgrade Required",
                    stopping,
                    "Upgrade: websocket\r\nSec-WebSocket-Version: 13\r\n"
                )
                .ConfigureAwait(false);
            return;
        }

        if (request.Header("Sec-WebSocket-Version") != "13")
        {
            await RespondAsync(stream, 426, "Upgrade Required", stopping, "Sec-WebSocket-Version: 13\r\n")
                .ConfigureAwait(false);
            return;
        }

        if (
            _options.AllowedOrigins is { } allowed
            && request.Header("Origin") is { } origin
            && !allowed.Contains(origin, StringComparer.OrdinalIgnoreCase)
        )
        {
            await RespondAsync(stream, 403, "Forbidden", stopping).ConfigureAwait(false);
            return;
        }

        if (!_hub.HasRoom)
        {
            GatewayMetrics.Disconnected(_hub.Server.Name, "server_full", wasConnected: false, GatewayMetrics.WebSocket);
            await RespondAsync(stream, 503, "Service Unavailable", stopping).ConfigureAwait(false);
            return;
        }

        // RFC 6455 defines the handshake with SHA-1; it proves the server speaks WebSocket and protects nothing.
#pragma warning disable CA5350
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
#pragma warning restore CA5350
        await stream
            .WriteAsync(
                Encoding.ASCII.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                        + $"Sec-WebSocket-Accept: {accept}\r\n\r\n"
                ),
                stopping
            )
            .ConfigureAwait(false);
        using var socket = WebSocket.CreateFromStream(
            stream,
            new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = _options.KeepAliveInterval }
        );
        await _hub.HandleAsync(socket, request.Query("filter"), request.Query("call"), stopping).ConfigureAwait(false);
    }

    // 200 while anything is served, 503 when nothing is, so an orchestrator restarts only a gateway that is down.
    private Task RespondHealthAsync(NetworkStream stream, GatewayHealthReport report, CancellationToken stopping)
    {
        using var buffer = new MemoryStream();
        using (var json = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("status", report.Status.ToString().ToLowerInvariant());
            json.WriteString("checkedAt", Utc(report.CheckedAt));
            if (_hub is not null)
            {
                json.WriteStartObject("clients");
                json.WriteNumber("aprsIs", _hub.Server.ClientCount);
                json.WriteNumber("webSocket", _hub.ClientCount);
                json.WriteEndObject();
            }

            json.WriteStartObject("sources");
            foreach (var (name, source) in report.Sources)
            {
                json.WriteStartObject(name);
                json.WriteBoolean("connected", source.IsConnected);
                json.WriteBoolean("quiet", source.IsQuiet);
                json.WriteBoolean("protocolMismatch", source.IsProtocolMismatch);
                if (source.LastPacket is { } last)
                {
                    json.WriteString("lastPacket", Utc(last));
                }
                else
                {
                    json.WriteNull("lastPacket");
                }

                json.WriteEndObject();
            }

            json.WriteEndObject();
            if (report.Counters.Count > 0)
            {
                json.WriteStartObject("counters");
                foreach (var (name, value) in report.Counters)
                {
                    json.WriteNumber(name, value);
                }

                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        var body = buffer.ToArray();
        var (status, reason) =
            report.Status == GatewayHealthStatus.Unhealthy ? (503, "Service Unavailable") : (200, "OK");
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nCache-Control: no-store\r\n"
                + $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n"
        );
        return stream.WriteAsync(head.Concat(body).ToArray(), stopping).AsTask();
    }

    private static string Utc(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken stopping)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(_options.RequestTimeout);
        var buffer = new byte[MaxRequestBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
            {
                return null;
            }

            if (read == 0)
            {
                return null;
            }

            length += read;
            var end = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
            if (end >= 0)
            {
                // A client sends nothing more before the 101 response, so anything after the head is refused.
                return end + 4 == length ? HttpRequest.Parse(Encoding.Latin1.GetString(buffer, 0, end)) : null;
            }
        }

        return null;
    }

    private static Task RespondAsync(
        NetworkStream stream,
        int status,
        string reason,
        CancellationToken stopping,
        string headers = ""
    ) =>
        stream
            .WriteAsync(
                Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status} {reason}\r\n{headers}Content-Type: text/plain\r\n"
                        + $"Content-Length: {reason.Length + 2}\r\nConnection: close\r\n\r\n{reason}\r\n"
                ),
                stopping
            )
            .AsTask();

    /// <summary>The parts of an HTTP request head this server reads.</summary>
    private sealed class HttpRequest
    {
        private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _query = new(StringComparer.Ordinal);

        public string Method { get; private init; } = "";

        public string Path { get; private init; } = "";

        public string? Header(string name) => _headers.GetValueOrDefault(name);

        public bool HeaderHas(string name, string token) =>
            Header(name) is { } value
            && value.Split(',', StringSplitOptions.TrimEntries).Contains(token, StringComparer.OrdinalIgnoreCase);

        public string? Query(string name) => _query.GetValueOrDefault(name);

        public static HttpRequest? Parse(string head)
        {
            var lines = head.Split("\r\n");
            var start = lines[0].Split(' ');
            if (start.Length != 3 || !start[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            {
                return null;
            }

            var target = start[1];
            var question = target.IndexOf('?', StringComparison.Ordinal);
            var request = new HttpRequest
            {
                Method = start[0],
                Path = Unescape(question < 0 ? target : target[..question]),
            };
            if (question >= 0)
            {
                foreach (var pair in target[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var equals = pair.IndexOf('=', StringComparison.Ordinal);
                    var name = Unescape(equals < 0 ? pair : pair[..equals]);
                    request._query.TryAdd(name, equals < 0 ? "" : Unescape(pair[(equals + 1)..]));
                }
            }

            foreach (var line in lines.AsSpan(1))
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0)
                {
                    return null;
                }

                request._headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            return request;
        }

        private static string Unescape(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
    }
}

/// <summary>Settings for <see cref="GatewayWebServer"/>.</summary>
public sealed class GatewayWebServerOptions
{
    /// <summary>
    /// Where to listen. Defaults to 127.0.0.1:14590, so nothing is exposed unless asked for; use
    /// <c>0.0.0.0</c> to serve the network.
    /// </summary>
    public IPEndPoint EndPoint { get; set; } = new(IPAddress.Loopback, 14590);

    /// <summary>The path WebSocket clients connect to. Defaults to <c>/aprs</c>.</summary>
    public string WebSocketPath { get; set; } = "/aprs";

    /// <summary>
    /// The health check to answer on <see cref="HealthPath"/>: <c>200</c> while healthy or degraded, <c>503</c> when
    /// unhealthy, with the details as JSON. <see langword="null"/>, the default, answers 404 there.
    /// </summary>
    public GatewayHealth? Health { get; set; }

    /// <summary>The path health checks are answered on. Defaults to <c>/health</c>.</summary>
    public string HealthPath { get; set; } = "/health";

    /// <summary>
    /// The web page origins allowed to connect, such as <c>https://map.example.org</c>. <see langword="null"/>, the
    /// default, allows any: the feed is read-only. Clients that are not browsers send no origin and are always allowed.
    /// </summary>
    public IReadOnlyList<string>? AllowedOrigins { get; set; }

    /// <summary>How long a client has to send its request. Defaults to 10 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How often to ping a WebSocket client to keep the connection open. Defaults to 30 seconds.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Throws <see cref="ArgumentException"/> if a setting is unusable.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(EndPoint, nameof(EndPoint));
        if (!WebSocketPath.StartsWith('/'))
        {
            throw new ArgumentException("The WebSocket path must start with '/'.", nameof(WebSocketPath));
        }

        if (!HealthPath.StartsWith('/') || HealthPath == WebSocketPath)
        {
            throw new ArgumentException(
                "The health path must start with '/' and differ from the WebSocket path.",
                nameof(HealthPath)
            );
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(RequestTimeout, TimeSpan.Zero, nameof(RequestTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThan(KeepAliveInterval, TimeSpan.Zero, nameof(KeepAliveInterval));
    }
}
