using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AprsNet.Gateway.Tests;

public sealed class PartsTests
{
    [Fact]
    public async Task TheLineReaderSplitsLinesAndDropsLongOnes()
    {
        var input = "first\r\nsecond\n" + new string('x', 40) + "\r\nthird\r\nunterminated";
        var reader = new BoundedLineReader(new MemoryStream(Encoding.Latin1.GetBytes(input)), 16);
        var token = TestContext.Current.CancellationToken;
        Assert.Equal("first", await reader.ReadLineAsync(token));
        Assert.Equal("second", await reader.ReadLineAsync(token));
        Assert.Equal("third", await reader.ReadLineAsync(token));
        Assert.Null(await reader.ReadLineAsync(token));
    }

    [Fact]
    public async Task TheLineReaderKeepsEveryByte()
    {
        var reader = new BoundedLineReader(new MemoryStream([0x41, 0xB0, 0xFF, 0x0A]), 16);
        Assert.Equal("A°ÿ", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(
        "user N0CALL pass 13023 vers aprsdroid 1.6 filter r/1/2/3 t/m",
        "N0CALL",
        true,
        "aprsdroid 1.6",
        "r/1/2/3 t/m"
    )]
    [InlineData("user N0CALL-15 pass 13023", "N0CALL-15", true, "", "")]
    [InlineData("user N0CALL filter p/N0", "N0CALL", false, "", "p/N0")]
    [InlineData("user N0CALL pass", "N0CALL", false, "", "")]
    [InlineData("user N0CALL vers", "N0CALL", false, "", "")]
    [InlineData("user N0CALL pass x13023", "N0CALL", false, "", "")]
    [InlineData("user N0CALL pass 13023 UDP 1234 filter b/N0*", "N0CALL", true, "", "b/N0*")]
    public void LoginsAreRead(string line, string callsign, bool verified, string software, string filter) =>
        Assert.Equal(new LoginLine(callsign, verified, software, filter), LoginLine.TryParse(line));

    [Theory]
    [InlineData("")]
    [InlineData("user")]
    [InlineData("login N0CALL")]
    [InlineData("user N0-CALL-9")]
    [InlineData("user N0CALL-123")]
    [InlineData("user ABCDEFGHIJ")]
    public void BadLoginsAreRefused(string line) => Assert.Null(LoginLine.TryParse(line));

    [Fact]
    public void OptionsAreChecked()
    {
        static AprsIsServerOptions Options(
            string name = "T2TEST",
            int maxClients = 1,
            int queue = 1,
            int loginSeconds = 1,
            int keepAliveSeconds = 1,
            string software = "x"
        ) =>
            new()
            {
                ServerName = name,
                MaxClients = maxClients,
                ClientQueueLength = queue,
                LoginTimeout = TimeSpan.FromSeconds(loginSeconds),
                KeepAliveInterval = TimeSpan.FromSeconds(keepAliveSeconds),
                SoftwareName = software,
            };

        Options().Validate();
        Assert.Throws<ArgumentException>(() => Options(name: "").Validate());
        Assert.Throws<ArgumentException>(() => Options(name: "TOOLONGNAME").Validate());
        Assert.Throws<ArgumentException>(() => Options(name: "T2 TEST").Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(maxClients: 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(queue: 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(loginSeconds: 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(keepAliveSeconds: 0).Validate());
        Assert.Throws<ArgumentException>(() => Options(software: " ").Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AprsIsServerOptions { ServerName = "T2TEST", History = TimeSpan.FromSeconds(-1) }.Validate()
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AprsIsServerOptions { ServerName = "T2TEST", MaxHistoryPackets = 0 }.Validate()
        );
        Assert.Throws<ArgumentException>(() => new AprsIsServer(Options(name: "")));
        Assert.Throws<ArgumentNullException>(() => new AprsIsServer(null!));
    }

    [Fact]
    public async Task AnUnexpectedFaultEndsOnlyThatSession()
    {
        var token = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, token);
        var accepted = await listener.AcceptTcpClientAsync(token);
        using var session = new ClientSession(
            accepted,
            new AprsIsServerOptions { ServerName = "T2TEST" },
            (_, _) => throw new InvalidOperationException("a fault while joining"),
            CancellationToken.None
        );

        var run = session.RunAsync();
        await client.GetStream().WriteAsync("user N0CALL\r\n"u8.ToArray(), token);
        await run.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.True(session.Faulted);
    }

    [Fact]
    public void TheDefaultsAreSafe()
    {
        var options = new AprsIsServerOptions { ServerName = "T2TEST" };
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 14580), options.EndPoint);
        Assert.Matches(@"^\d+\.\d+\.\d+", options.SoftwareVersion);
    }
}
