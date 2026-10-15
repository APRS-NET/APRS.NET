using System.Net;
using System.Net.Sockets;
using System.Text;
using AprsNet.AprsIsTcpClient;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Gateway.Tests;

public sealed class AprsIsServerTests : IAsyncLifetime
{
    // N0CALL's passcode is 13023.
    private const string Login = "user N0CALL pass 13023 vers test 1.0";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 7, 30, 0, TimeSpan.Zero));
    private AprsIsServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = Start();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    private AprsIsServer Start(int maxClients = 100, TimeSpan history = default, int maxHistory = 100_000)
    {
        var server = new AprsIsServer(
            new AprsIsServerOptions
            {
                ServerName = "T2TEST",
                EndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                MaxClients = maxClients,
                History = history,
                MaxHistoryPackets = maxHistory,
                SoftwareName = "APRS.NET",
                SoftwareVersion = "9.9",
                TimeProvider = _time,
            }
        );
        server.Start();
        return server;
    }

    [Fact]
    public async Task AVerifiedLoginIsAnswered()
    {
        await using var client = await Client.ConnectAsync(_server);
        Assert.Equal("# APRS.NET 9.9", await client.ReadAsync());
        await client.WriteAsync(Login);
        Assert.Equal("# logresp N0CALL verified, server T2TEST", await client.ReadAsync());
    }

    [Theory]
    [InlineData("user N0CALL-9 pass -1 vers test 1.0", "# logresp N0CALL-9 unverified, server T2TEST")]
    [InlineData("user N0CALL pass 12345", "# logresp N0CALL unverified, server T2TEST")]
    [InlineData("USER n0call PASS 13023", "# logresp n0call verified, server T2TEST")]
    [InlineData("user N0CALL", "# logresp N0CALL unverified, server T2TEST")]
    public async Task OtherLoginsAreUnverified(string login, string response)
    {
        await using var client = await Client.ConnectAsync(_server);
        await client.ReadAsync();
        await client.WriteAsync(login);
        Assert.Equal(response, await client.ReadAsync());
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("user")]
    [InlineData("user N0CALL!")]
    [InlineData("user TOOLONGCALL")]
    public async Task ABadLoginIsDisconnected(string login)
    {
        await using var client = await Client.ConnectAsync(_server);
        await client.ReadAsync();
        await client.WriteAsync(login);
        Assert.Null(await client.ReadAsync());
    }

    [Fact]
    public async Task AClientThatNeverLogsInIsDisconnected()
    {
        await using var client = await Client.ConnectAsync(_server);
        await client.ReadAsync();

        // The login wait starts as the banner goes out; keep the fake clock moving until it has fired.
        var closed = client.ReadAsync();
        var deadline = DateTime.UtcNow + Wait;
        while (!closed.IsCompleted)
        {
            Assert.True(DateTime.UtcNow < deadline, "The client was never disconnected.");
            _time.Advance(TimeSpan.FromSeconds(31));
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Null(await closed);
    }

    [Fact]
    public async Task PacketsArriveWhenTheLoginFilterPassesThem()
    {
        await using var client = await LoggedInAsync("filter p/N1");
        Assert.Equal("# filter p/N1 active", await client.ReadAsync());

        Publish("N0OTHER>APRS,TCPIP*:>not for this client");
        Publish("N1ABC>APRS,TCPIP*:>for this client");
        Assert.Equal("N1ABC>APRS,TCPIP*:>for this client", await client.ReadAsync());
    }

    [Fact]
    public async Task PacketsPublishedBeforeTheLoginAreNotSent()
    {
        await using var client = await Client.ConnectAsync(_server);
        await client.ReadAsync();
        await WaitUntil(() => _server.ClientCount == 1);
        Publish("N1ABC>APRS,TCPIP*:>before the login");
        await client.WriteAsync($"{Login} filter p/N1");
        await client.ReadAsync();
        await client.ReadAsync();
        Publish("N1ABC>APRS,TCPIP*:>after the login");
        Assert.Equal("N1ABC>APRS,TCPIP*:>after the login", await client.ReadAsync());
    }

    [Fact]
    public async Task MessagesToTheClientPassAnyFilter()
    {
        await using var client = await LoggedInAsync("filter p/N1");
        await client.ReadAsync();

        Publish("N9XYZ>APRS,TCPIP*::N0CALL-9 :not this SSID");
        Publish("N9XYZ>APRS,TCPIP*::n0call   :Hello{01");
        Assert.Equal("N9XYZ>APRS,TCPIP*::n0call   :Hello{01", await client.ReadAsync());
    }

    [Fact]
    public async Task WithoutAFilterOnlyMessagesToTheClientArrive()
    {
        await using var client = await LoggedInAsync("");
        Publish("N1ABC>APRS,TCPIP*:>status");
        Publish("N9XYZ>APRS,TCPIP*::N0CALL   :Hello");
        Assert.Equal("N9XYZ>APRS,TCPIP*::N0CALL   :Hello", await client.ReadAsync());
    }

    [Fact]
    public async Task AFilterCommandReplacesTheFilter()
    {
        await using var client = await LoggedInAsync("filter p/N1");
        await client.ReadAsync();
        await client.WriteAsync("#filter b/N2* -p/N2X");
        Assert.Equal("# filter b/N2* -p/N2X active", await client.ReadAsync());

        Publish("N1ABC>APRS,TCPIP*:>old filter");
        Publish("N2XAB>APRS,TCPIP*:>excluded");
        Publish("N2ABC>APRS,TCPIP*:>new filter");
        Assert.Equal("N2ABC>APRS,TCPIP*:>new filter", await client.ReadAsync());
    }

    [Fact]
    public async Task AnInvalidFilterIsReportedAndTheOldOneKept()
    {
        await using var client = await LoggedInAsync("filter p/N1");
        await client.ReadAsync();
        await client.WriteAsync("#filter r/91/0/10");
        Assert.Equal(
            "# filter r/91/0/10 is invalid: 'r/91/0/10' is not a valid filter: '91' is not a latitude.",
            await client.ReadAsync()
        );

        Publish("N1ABC>APRS,TCPIP*:>still the old filter");
        Assert.Equal("N1ABC>APRS,TCPIP*:>still the old filter", await client.ReadAsync());
    }

    [Fact]
    public async Task PacketsFromClientsAreIgnored()
    {
        await using var client = await LoggedInAsync("filter p/N0");
        await client.ReadAsync();
        await client.WriteAsync("N0CALL>APRS,TCPIP*:>from the client");
        await client.WriteAsync("# a comment");
        Publish("N0ABC>APRS,TCPIP*:>from the server");
        Assert.Equal("N0ABC>APRS,TCPIP*:>from the server", await client.ReadAsync());
    }

    [Fact]
    public async Task KeepalivesArriveOnSchedule()
    {
        await using var client = await LoggedInAsync("");
        _time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal("# APRS.NET 9.9 15 Oct 2026 07:30:20 GMT T2TEST", await client.ReadAsync());
        _time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal("# APRS.NET 9.9 15 Oct 2026 07:30:40 GMT T2TEST", await client.ReadAsync());
    }

    [Fact]
    public async Task EachClientHasItsOwnFilter()
    {
        await using var one = await LoggedInAsync("filter p/N1");
        await using var two = await LoggedInAsync("filter p/N2");
        await one.ReadAsync();
        await two.ReadAsync();

        Publish("N2ABC>APRS,TCPIP*:>two");
        Publish("N1ABC>APRS,TCPIP*:>one");
        Assert.Equal("N1ABC>APRS,TCPIP*:>one", await one.ReadAsync());
        Assert.Equal("N2ABC>APRS,TCPIP*:>two", await two.ReadAsync());
    }

    [Fact]
    public async Task ExtraClientsAreToldTheServerIsFull()
    {
        await using var server = Start(maxClients: 1);
        await using var first = await Client.ConnectAsync(server);
        await first.ReadAsync();
        await using var second = await Client.ConnectAsync(server);
        Assert.Equal("# Server full", await second.ReadAsync());
        Assert.Null(await second.ReadAsync());
    }

    [Fact]
    public async Task ClientsAreCountedAndRemovedWhenTheyLeave()
    {
        await using (var client = await LoggedInAsync(""))
        {
            Assert.Equal(1, _server.ClientCount);
        }

        await WaitUntil(() => _server.ClientCount == 0);
    }

    [Fact]
    public async Task DisposingDisconnectsClients()
    {
        await using var client = await LoggedInAsync("");
        await _server.DisposeAsync();
        Assert.Null(await client.ReadAsync());
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task StartingTwiceOrAfterDisposingFails()
    {
        Assert.Throws<InvalidOperationException>(() => _server.Start());
        await _server.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => _server.Start());
    }

    [Fact]
    public async Task OurOwnClientLogsInAndReceivesPackets()
    {
        var port = _server.LocalEndPoint!.Port;
        await using var client = new AprsIsClient(
            new AprsIsClientOptions
            {
                Endpoint = new AprsIsEndpoint("127.0.0.1", port),
                Callsign = "N0CALL",
                Passcode = AprsIsPasscode.Compute("N0CALL"),
                Filter = "t/s",
            }
        );
        var login = await client
            .ConnectAsync(TestContext.Current.CancellationToken)
            .WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal(new LoginResult("N0CALL", LoginStatus.Verified, "T2TEST"), login);

        using var cancel = new CancellationTokenSource(Wait);
        await using var lines = client.ReadLinesAsync(cancel.Token).GetAsyncEnumerator(cancel.Token);
        await WaitUntil(() => _server.ClientCount == 1);
        Publish("N1ABC>APRS,TCPIP*:!3858.80N/09440.20W>position");
        Publish("N1ABC>APRS,TCPIP*:>status");
        string? line;
        do
        {
            Assert.True(await lines.MoveNextAsync());
            line = lines.Current;
        } while (line.StartsWith('#'));

        Assert.Equal("N1ABC>APRS,TCPIP*:>status", line);
    }

    [Fact]
    public async Task AClientThatStopsReadingIsDisconnected()
    {
        await using var server = new AprsIsServer(
            new AprsIsServerOptions
            {
                ServerName = "T2TEST",
                EndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                ClientQueueLength = 10,
                TimeProvider = _time,
            }
        );
        server.Start();
        await using var client = await Client.ConnectAsync(server);
        await client.ReadAsync();
        await client.WriteAsync($"{Login} filter p/N1");
        Assert.StartsWith("# logresp", await client.ReadAsync());

        // The client reads nothing more, so once the socket buffers fill, the queue does too.
        var packet = AprsPacket.Parse("N1ABC>APRS,TCPIP*:>" + new string('x', 400));
        var deadline = DateTime.UtcNow + Wait;
        while (server.ClientsDroppedForOverflow == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The slow client was never dropped.");
            for (var i = 0; i < 1000; i++)
            {
                server.Publish(packet);
            }

            await Task.Yield();
        }

        await WaitUntil(() => server.ClientCount == 0);
    }

    [Fact]
    public async Task ANewClientGetsTheRecentPacketsItsFilterPasses()
    {
        await using var server = Start(history: TimeSpan.FromMinutes(1));
        server.Publish(AprsPacket.Parse("N1OLD>APRS:>too old"));
        _time.Advance(TimeSpan.FromSeconds(61));
        server.Publish(AprsPacket.Parse("N1ABC>APRS:>first"));
        server.Publish(AprsPacket.Parse("N2ABC>APRS:>filtered out"));
        server.Publish(AprsPacket.Parse("N9XYZ>APRS::N0CALL   :a message"));
        server.Publish(AprsPacket.Parse("N1ABC>APRS:>second"));

        await using var client = await Client.ConnectAsync(server);
        await client.ReadAsync();
        await client.WriteAsync($"{Login} filter p/N1");
        Assert.StartsWith("# logresp", await client.ReadAsync());
        Assert.Equal("# filter p/N1 active", await client.ReadAsync());
        server.Publish(AprsPacket.Parse("N1ABC>APRS:>live"));

        Assert.Equal("N1ABC>APRS:>first", await client.ReadAsync());
        Assert.Equal("N9XYZ>APRS::N0CALL   :a message", await client.ReadAsync());
        Assert.Equal("N1ABC>APRS:>second", await client.ReadAsync());
        Assert.Equal("N1ABC>APRS:>live", await client.ReadAsync());
    }

    [Fact]
    public async Task TheHistoryKeepsOnlyTheNewestPackets()
    {
        await using var server = Start(history: TimeSpan.FromHours(1), maxHistory: 2);
        foreach (var n in new[] { "1", "2", "3" })
        {
            server.Publish(AprsPacket.Parse($"N1ABC>APRS:>packet {n}"));
        }

        await using var client = await Client.ConnectAsync(server);
        await client.ReadAsync();
        await client.WriteAsync($"{Login} filter p/N1");
        await client.ReadAsync();
        await client.ReadAsync();
        Assert.Equal("N1ABC>APRS:>packet 2", await client.ReadAsync());
        Assert.Equal("N1ABC>APRS:>packet 3", await client.ReadAsync());
    }

    [Fact]
    public async Task WithoutHistoryOnlyNewPacketsArrive()
    {
        Publish("N1ABC>APRS:>before");
        await using var client = await LoggedInAsync("filter p/N1");
        await client.ReadAsync();
        Publish("N1ABC>APRS:>after");
        Assert.Equal("N1ABC>APRS:>after", await client.ReadAsync());
    }

    private void Publish(string line) => _server.Publish(AprsPacket.Parse(line));

    private async Task<Client> LoggedInAsync(string filter)
    {
        var client = await Client.ConnectAsync(_server);
        await client.ReadAsync();
        await client.WriteAsync($"{Login} {filter}");
        Assert.StartsWith("# logresp", await client.ReadAsync());
        return client;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting.");
            await Task.Delay(10);
        }
    }

    private sealed class Client : IAsyncDisposable
    {
        private readonly TcpClient _tcp;
        private readonly StreamReader _reader;
        private readonly NetworkStream _stream;

        private Client(TcpClient tcp)
        {
            _tcp = tcp;
            _stream = tcp.GetStream();
            _reader = new StreamReader(_stream, Encoding.Latin1);
        }

        public static async Task<Client> ConnectAsync(AprsIsServer server)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(server.LocalEndPoint!).WaitAsync(Wait);
            return new Client(tcp);
        }

        public async Task<string?> ReadAsync()
        {
            try
            {
                return await _reader.ReadLineAsync().WaitAsync(Wait);
            }
            catch (IOException)
            {
                return null; // reset by the server
            }
        }

        public async Task WriteAsync(string line) => await _stream.WriteAsync(Encoding.Latin1.GetBytes(line + "\r\n"));

        public ValueTask DisposeAsync()
        {
            _reader.Dispose();
            _tcp.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
