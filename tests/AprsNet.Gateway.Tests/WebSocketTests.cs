using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Gateway.Tests;

public sealed class WebSocketTests : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 7, 30, 0, TimeSpan.Zero));
    private AprsIsServer _server = null!;
    private AprsWebSocketHub _hub = null!;
    private GatewayWebServer _web = null!;

    public ValueTask InitializeAsync()
    {
        Start(new AprsWebSocketHubOptions(), history: TimeSpan.FromMinutes(10));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _web.DisposeAsync();
        await _hub.DisposeAsync();
        await _server.DisposeAsync();
    }

    private void Start(
        AprsWebSocketHubOptions hubOptions,
        TimeSpan history = default,
        StationStore? stations = null,
        GatewayWebServerOptions? webOptions = null
    )
    {
        _server = new AprsIsServer(
            new AprsIsServerOptions
            {
                ServerName = "T2TEST",
                EndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                History = history,
                Stations = stations,
                SoftwareName = "APRS.NET",
                SoftwareVersion = "9.9",
                TimeProvider = _time,
            }
        );
        _server.Start();
        _hub = new AprsWebSocketHub(_server, hubOptions);
        webOptions ??= new GatewayWebServerOptions();
        webOptions.EndPoint = new(IPAddress.Loopback, 0);
        _web = new GatewayWebServer(_hub, webOptions);
        _web.Start();
    }

    // A WebSocket client that completes the handshake and then never reads or sends again, with a small receive
    // buffer so the server's sends back up soon.
    private async Task<TcpClient> ConnectWithoutReadingAsync(string query = "?filter=t/s")
    {
        var tcp = new TcpClient { ReceiveBufferSize = 4096 };
        await tcp.ConnectAsync(_web.LocalEndPoint!, TestContext.Current.CancellationToken);
        var stream = tcp.GetStream();
        await stream.WriteAsync(
            Encoding.ASCII.GetBytes(
                $"GET /aprs{query} HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                    + "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n"
            ),
            TestContext.Current.CancellationToken
        );
        var head = new List<byte>();
        while (head.Count < 4 || !head[^4..].SequenceEqual("\r\n\r\n"u8.ToArray()))
        {
            var one = new byte[1];
            var read = await stream
                .ReadAsync(one, TestContext.Current.CancellationToken)
                .AsTask()
                .WaitAsync(Wait, TestContext.Current.CancellationToken);
            Assert.Equal(1, read);
            head.Add(one[0]);
        }

        Assert.StartsWith("HTTP/1.1 101 ", Encoding.ASCII.GetString([.. head]), StringComparison.Ordinal);
        return tcp;
    }

    // Publishes long packets until the predicate holds, or far more than any network buffer takes.
    private void PublishUntil(Func<bool> done)
    {
        var text = new string('x', 2000);
        for (var i = 0; i < 20_000 && !done(); i++)
        {
            _server.Publish(AprsPacket.Parse($"N0CALL>APRS:>{i} {text}"));
        }
    }

    // Whether the server has closed the connection: reading what it sent ends, or the read is reset.
    private static async Task<bool> ServerClosedAsync(TcpClient tcp)
    {
        var buffer = new byte[65536];
        try
        {
            while (
                await tcp.GetStream()
                    .ReadAsync(buffer, TestContext.Current.CancellationToken)
                    .AsTask()
                    .WaitAsync(Wait, TestContext.Current.CancellationToken) > 0
            ) { }
        }
        catch (IOException)
        {
            // Reset.
        }

        return true;
    }

    private async Task WaitForPending(int count)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (_web.PendingConnections != count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"{_web.PendingConnections} pending, not {count}");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private Uri Url(string query = "") => new($"ws://127.0.0.1:{_web.LocalEndPoint!.Port}/aprs{query}");

    private async Task<ClientWebSocket> ConnectAsync(string query = "", string? origin = null)
    {
        var socket = new ClientWebSocket();
        if (origin is not null)
        {
            socket.Options.SetRequestHeader("Origin", origin);
        }

        await socket
            .ConnectAsync(Url(query), TestContext.Current.CancellationToken)
            .WaitAsync(Wait, TestContext.Current.CancellationToken);
        return socket;
    }

    private static async Task<JsonElement> ReceiveAsync(ClientWebSocket socket)
    {
        var buffer = new byte[16384];
        var length = 0;
        WebSocketReceiveResult result;
        do
        {
            result = await socket
                .ReceiveAsync(
                    new ArraySegment<byte>(buffer, length, buffer.Length - length),
                    TestContext.Current.CancellationToken
                )
                .WaitAsync(Wait, TestContext.Current.CancellationToken);
            length += result.Count;
        } while (!result.EndOfMessage);

        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        return JsonDocument.Parse(buffer.AsMemory(0, length)).RootElement.Clone();
    }

    private static Task SendAsync(ClientWebSocket socket, string text) =>
        socket.SendAsync(
            Encoding.UTF8.GetBytes(text),
            WebSocketMessageType.Text,
            true,
            TestContext.Current.CancellationToken
        );

    private static async Task<WebSocketCloseStatus?> ClosedAsync(ClientWebSocket socket)
    {
        var buffer = new byte[16384];
        while (true)
        {
            var result = await socket
                .ReceiveAsync(buffer, TestContext.Current.CancellationToken)
                .WaitAsync(Wait, TestContext.Current.CancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return result.CloseStatus;
            }
        }
    }

    private async Task WaitForClients(int count)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (_hub.ClientCount != count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"{_hub.ClientCount} clients, not {count}");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task AClientGetsAGreetingThenThePacketsItsFilterPasses()
    {
        using var socket = await ConnectAsync("?filter=t/s");
        var greeting = await ReceiveAsync(socket);
        Assert.Equal("T2TEST", greeting.GetProperty("server").GetString());
        Assert.Equal("APRS.NET 9.9", greeting.GetProperty("software").GetString());
        Assert.Equal("t/s", greeting.GetProperty("filter").GetString());
        await WaitForClients(1);

        _server.Publish(AprsPacket.Parse("N0CALL>APRS:!4903.50N/07201.75W>Not this"));
        _server.Publish(AprsPacket.Parse("N0CALL>APRS:>This one"));
        var packet = await ReceiveAsync(socket);
        Assert.Equal("N0CALL>APRS:>This one", packet.GetProperty("raw").GetString());
        Assert.Equal("status", packet.GetProperty("type").GetString());
        Assert.Equal("This one", packet.GetProperty("status").GetProperty("text").GetString());
    }

    [Fact]
    public async Task HistoryReplaysOnConnect()
    {
        _server.Publish(AprsPacket.Parse("N0CALL>APRS:>Earlier"));
        using var socket = await ConnectAsync("?filter=t/s");
        await ReceiveAsync(socket);
        Assert.Equal("N0CALL>APRS:>Earlier", (await ReceiveAsync(socket)).GetProperty("raw").GetString());
    }

    [Fact]
    public async Task APacketPublishedOnceTheGreetingArrivesIsNotMissed()
    {
        // Without a history to replay, a client that has read the greeting must already be subscribed.
        await DisposeAsync();
        Start(new AprsWebSocketHubOptions());
        using var socket = await ConnectAsync("?filter=t/s");
        await ReceiveAsync(socket);
        _server.Publish(AprsPacket.Parse("N0CALL>APRS:>Right after"));
        Assert.Equal("N0CALL>APRS:>Right after", (await ReceiveAsync(socket)).GetProperty("raw").GetString());
    }

    [Fact]
    public async Task AClientChangesItsFilter()
    {
        using var socket = await ConnectAsync();
        Assert.Equal(JsonValueKind.Null, (await ReceiveAsync(socket)).GetProperty("filter").ValueKind);

        await SendAsync(socket, """{"filter":"t/p"}""");
        var reply = await ReceiveAsync(socket);
        Assert.Equal("t/p", reply.GetProperty("filter").GetString());
        Assert.True(reply.GetProperty("active").GetBoolean());

        _server.Publish(AprsPacket.Parse("N0CALL>APRS:>status"));
        _server.Publish(AprsPacket.Parse("N0CALL>APRS:!4903.50N/07201.75W>position"));
        Assert.Equal("position", (await ReceiveAsync(socket)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task AnOversizedFilterIsRefusedAndTheOldOneKept()
    {
        using var socket = await ConnectAsync("?filter=t/p");
        await ReceiveAsync(socket);
        var text = string.Join(' ', Enumerable.Range(0, 51).Select(i => $"b/N{i}CALL"));
        await SendAsync(socket, $$"""{"filter":"{{text}}"}""");
        var reply = await ReceiveAsync(socket);
        Assert.Equal(text, reply.GetProperty("filter").GetString());
        Assert.Equal(
            "This server accepts at most 50 terms in a filter; this filter has 51.",
            reply.GetProperty("error").GetString()
        );

        _server.Publish(AprsPacket.Parse("N0CALL>APRS:>status"));
        _server.Publish(AprsPacket.Parse("N0CALL>APRS:!4903.50N/07201.75W>position"));
        Assert.Equal("position", (await ReceiveAsync(socket)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task AnOversizedStartingFilterIsReported()
    {
        var text = string.Join(' ', Enumerable.Range(0, 10).Select(i => $"r/{i}/0/1"));
        using var socket = await ConnectAsync("?filter=" + Uri.EscapeDataString(text));
        Assert.Equal(JsonValueKind.Null, (await ReceiveAsync(socket)).GetProperty("filter").ValueKind);
        var reply = await ReceiveAsync(socket);
        Assert.Equal(text, reply.GetProperty("filter").GetString());
        Assert.Equal(
            "APRS-IS servers accept at most 9 range (r/) terms; this filter has 10.",
            reply.GetProperty("error").GetString()
        );
    }

    [Theory]
    [InlineData("""{"filter":"x/nonsense"}""", "filter")]
    [InlineData("not json", "error")]
    [InlineData("""{"send":"N0CALL>APRS:>hi"}""", "error")]
    public async Task OtherMessagesGetAnError(string message, string field)
    {
        using var socket = await ConnectAsync();
        await ReceiveAsync(socket);
        await SendAsync(socket, message);
        var reply = await ReceiveAsync(socket);
        Assert.True(reply.TryGetProperty(field, out _));
        Assert.True(reply.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task AnInvalidStartingFilterIsReported()
    {
        using var socket = await ConnectAsync("?filter=x%2Fnonsense");
        Assert.Equal(JsonValueKind.Null, (await ReceiveAsync(socket)).GetProperty("filter").ValueKind);
        var reply = await ReceiveAsync(socket);
        Assert.Equal("x/nonsense", reply.GetProperty("filter").GetString());
        Assert.True(reply.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task MessagesToTheClientsCallsignAlwaysPass()
    {
        using var socket = await ConnectAsync("?call=n0call-9");
        await ReceiveAsync(socket);
        await WaitForClients(1);
        _server.Publish(AprsPacket.Parse("N1ABC>APRS::N0CALL-9 :Hello{1"));
        Assert.Equal("message", (await ReceiveAsync(socket)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task ASlowClientIsClosed()
    {
        await DisposeAsync();
        Start(new AprsWebSocketHubOptions { ClientQueueLength = 1 });
        using var socket = await ConnectAsync("?filter=t/s");
        await ReceiveAsync(socket);
        await WaitForClients(1);

        // The client doesn't read, so the queue fills.
        for (var i = 0; i < 20_000 && _hub.ClientsDroppedForOverflow == 0; i++)
        {
            _server.Publish(AprsPacket.Parse($"N0CALL>APRS:>{i} {new string('x', 200)}"));
        }

        // Dropped like a slow APRS-IS client: the connection is cut, without waiting to send a close.
        Assert.Equal(1, _hub.ClientsDroppedForOverflow);
        await WaitForClients(0);
        await Assert.ThrowsAsync<WebSocketException>(() => ClosedAsync(socket));
    }

    [Fact]
    public async Task ClientsOverTheLimitAreRefused()
    {
        await DisposeAsync();
        Start(new AprsWebSocketHubOptions { MaxClients = 1 });
        using var first = await ConnectAsync();
        await ReceiveAsync(first);
        await WaitForClients(1);

        using var second = new ClientWebSocket();
        var refused = await Assert.ThrowsAsync<WebSocketException>(() =>
            second
                .ConnectAsync(Url(), TestContext.Current.CancellationToken)
                .WaitAsync(Wait, TestContext.Current.CancellationToken)
        );
        Assert.Contains("503", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyAllowedOriginsMayConnect()
    {
        await _web.DisposeAsync();
        _web = new GatewayWebServer(
            _hub,
            new GatewayWebServerOptions
            {
                EndPoint = new(IPAddress.Loopback, 0),
                AllowedOrigins = ["https://map.example.org"],
            }
        );
        _web.Start();

        using var allowed = await ConnectAsync(origin: "https://map.example.org");
        using var noOrigin = await ConnectAsync();
        using var other = new ClientWebSocket();
        other.Options.SetRequestHeader("Origin", "https://evil.example.com");
        var refused = await Assert.ThrowsAsync<WebSocketException>(() =>
            other
                .ConnectAsync(Url(), TestContext.Current.CancellationToken)
                .WaitAsync(Wait, TestContext.Current.CancellationToken)
        );
        Assert.Contains("403", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET /other HTTP/1.1\r\nHost: x\r\n\r\n", "404")]
    [InlineData("GET /aprs HTTP/1.1\r\nHost: x\r\n\r\n", "426")]
    [InlineData("POST /aprs HTTP/1.1\r\nHost: x\r\n\r\n", "405")]
    [InlineData("nonsense\r\n\r\n", "400")]
    public async Task OtherRequestsGetAnErrorResponse(string request, string status)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(_web.LocalEndPoint!, TestContext.Current.CancellationToken);
        await tcp.GetStream().WriteAsync(Encoding.ASCII.GetBytes(request), TestContext.Current.CancellationToken);
        using var reader = new StreamReader(tcp.GetStream(), Encoding.ASCII);
        var line = await reader
            .ReadLineAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.StartsWith($"HTTP/1.1 {status} ", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposingTheHubClosesItsClients()
    {
        using var socket = await ConnectAsync();
        await ReceiveAsync(socket);
        await WaitForClients(1);
        await _hub.DisposeAsync();
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await ClosedAsync(socket));
    }

    [Fact]
    public async Task AClientThatStopsReadingIsClosedOnceItsQueueFills()
    {
        await DisposeAsync();
        Start(new AprsWebSocketHubOptions { ClientQueueLength = 4 });
        using var tcp = await ConnectWithoutReadingAsync();
        await WaitForClients(1);

        // Enough to fill the network buffers, so the server's send is stuck when the queue fills behind it.
        PublishUntil(() => _hub.ClientsDroppedForOverflow > 0);

        await WaitForClients(0);
        Assert.Equal(1, _hub.ClientsDroppedForOverflow);
        Assert.True(await ServerClosedAsync(tcp));
    }

    [Fact]
    public async Task AClientWhoseSendIsHeldUpTooLongIsClosed()
    {
        // Its queue never fills, but one message waits on it for longer than the send timeout.
        await DisposeAsync();
        Start(
            new AprsWebSocketHubOptions { ClientQueueLength = 1_000_000, SendTimeout = TimeSpan.FromMilliseconds(200) }
        );
        using var tcp = await ConnectWithoutReadingAsync();
        await WaitForClients(1);

        PublishUntil(() => _hub.ClientsDroppedForOverflow > 0);

        await WaitForClients(0);
        Assert.Equal(1, _hub.ClientsDroppedForOverflow);
    }

    [Fact]
    public async Task AClientThatDoesNotAnswerPingsIsClosed()
    {
        await DisposeAsync();
        Start(
            new AprsWebSocketHubOptions(),
            webOptions: new GatewayWebServerOptions
            {
                KeepAliveInterval = TimeSpan.FromMilliseconds(100),
                KeepAliveTimeout = TimeSpan.FromMilliseconds(300),
            }
        );
        using var silent = await ConnectWithoutReadingAsync();
        await WaitForClients(1);
        await WaitForClients(0);
        Assert.True(await ServerClosedAsync(silent));
    }

    [Fact]
    public async Task AClientThatAnswersPingsStaysConnected()
    {
        await DisposeAsync();
        Start(
            new AprsWebSocketHubOptions(),
            webOptions: new GatewayWebServerOptions
            {
                KeepAliveInterval = TimeSpan.FromMilliseconds(100),
                KeepAliveTimeout = TimeSpan.FromMilliseconds(300),
            }
        );
        using var socket = await ConnectAsync("?filter=t/s");
        await ReceiveAsync(socket);
        await WaitForClients(1);

        // Reading answers the pings while it waits.
        var packet = ReceiveAsync(socket);
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        _server.Publish(AprsPacket.Parse("N0CALL>APRS:>Still here"));
        Assert.Equal("N0CALL>APRS:>Still here", (await packet).GetProperty("raw").GetString());
        Assert.Equal(1, _hub.ClientCount);
    }

    [Fact]
    public async Task DisposingTheHubFinishesWithAClientThatStopsReading()
    {
        await DisposeAsync();
        Start(new AprsWebSocketHubOptions { ClientQueueLength = 1_000_000 });
        using var tcp = await ConnectWithoutReadingAsync();
        await WaitForClients(1);
        PublishUntil(() => false);

        await _hub.DisposeAsync().AsTask().WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal(0, _hub.ClientCount);
    }

    [Fact]
    public async Task DisposingTheWebServerFinishesWithAClientThatStopsReading()
    {
        await DisposeAsync();
        Start(new AprsWebSocketHubOptions { ClientQueueLength = 1_000_000 });
        using var tcp = await ConnectWithoutReadingAsync();
        await WaitForClients(1);
        PublishUntil(() => false);

        await _web.DisposeAsync().AsTask().WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal(0, _hub.ClientCount);
    }

    [Fact]
    public async Task ConnectionsBeyondThePendingLimitAreClosedAtOnce()
    {
        await DisposeAsync();
        Start(new AprsWebSocketHubOptions(), webOptions: new GatewayWebServerOptions { MaxPendingConnections = 2 });
        using var socket = await ConnectAsync("?filter=t/s");
        await ReceiveAsync(socket);
        await WaitForClients(1);

        using var idle1 = new TcpClient();
        using var idle2 = new TcpClient();
        await idle1.ConnectAsync(_web.LocalEndPoint!, TestContext.Current.CancellationToken);
        await idle2.ConnectAsync(_web.LocalEndPoint!, TestContext.Current.CancellationToken);
        await WaitForPending(2);

        using var extra = new TcpClient();
        await extra.ConnectAsync(_web.LocalEndPoint!, TestContext.Current.CancellationToken);
        Assert.True(await ServerClosedAsync(extra));
        Assert.Equal(2, _web.PendingConnections);

        // The WebSocket client, connected already, doesn't count and keeps streaming.
        _server.Publish(AprsPacket.Parse("N0CALL>APRS:>Still streaming"));
        Assert.Equal("N0CALL>APRS:>Still streaming", (await ReceiveAsync(socket)).GetProperty("raw").GetString());
    }

    [Fact]
    public async Task HealthStillAnswersAfterIdleConnectionsTimeOut()
    {
        await DisposeAsync();
        Start(
            new AprsWebSocketHubOptions(),
            webOptions: new GatewayWebServerOptions
            {
                Health = new GatewayHealth(),
                MaxPendingConnections = 2,
                RequestTimeout = TimeSpan.FromMilliseconds(200),
            }
        );
        using var idle1 = new TcpClient();
        using var idle2 = new TcpClient();
        await idle1.ConnectAsync(_web.LocalEndPoint!, TestContext.Current.CancellationToken);
        await idle2.ConnectAsync(_web.LocalEndPoint!, TestContext.Current.CancellationToken);
        Assert.True(await ServerClosedAsync(idle1));
        Assert.True(await ServerClosedAsync(idle2));
        await WaitForPending(0);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(_web.LocalEndPoint!, TestContext.Current.CancellationToken);
        await tcp.GetStream()
            .WriteAsync("GET /health HTTP/1.1\r\nHost: x\r\n\r\n"u8.ToArray(), TestContext.Current.CancellationToken);
        using var reader = new StreamReader(tcp.GetStream(), Encoding.ASCII);
        var line = await reader
            .ReadLineAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal("HTTP/1.1 200 OK", line);
    }

    [Fact]
    public async Task ClientsThatConnectInTurnStreamAndCloseCleanlyUnderAPendingLimitOfOne()
    {
        await DisposeAsync();
        Start(new AprsWebSocketHubOptions(), webOptions: new GatewayWebServerOptions { MaxPendingConnections = 1 });
        using var first = await ConnectAsync("?filter=t/s");
        await ReceiveAsync(first);
        using var second = await ConnectAsync("?filter=t/s");
        await ReceiveAsync(second);
        await WaitForClients(2);

        _server.Publish(AprsPacket.Parse("N0CALL>APRS:>To both"));
        Assert.Equal("N0CALL>APRS:>To both", (await ReceiveAsync(first)).GetProperty("raw").GetString());
        Assert.Equal("N0CALL>APRS:>To both", (await ReceiveAsync(second)).GetProperty("raw").GetString());

        await _hub.DisposeAsync();
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await ClosedAsync(first));
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await ClosedAsync(second));
    }
}
