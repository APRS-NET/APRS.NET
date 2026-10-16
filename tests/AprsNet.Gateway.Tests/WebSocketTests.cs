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

    private void Start(AprsWebSocketHubOptions hubOptions, TimeSpan history = default, StationStore? stations = null)
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
        _web = new GatewayWebServer(_hub, new GatewayWebServerOptions { EndPoint = new(IPAddress.Loopback, 0) });
        _web.Start();
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
}
