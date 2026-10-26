using System.Net;
using System.Net.Sockets;
using AprsNet.Ax25;
using AprsNet.Kiss;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.AprsIsCli.Tests;

/// <summary><c>aprscli tnc share</c> in front of a fake TNC on a local socket, with KISS apps connecting to it.</summary>
public sealed class TncShareTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly TcpListener _tnc = new(IPAddress.Loopback, 0);

    public TncShareTests()
    {
        _tnc.Start();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string TncAddress => $"127.0.0.1:{((IPEndPoint)_tnc.LocalEndpoint).Port}";

    public void Dispose() => _tnc.Dispose();

    private static Ax25Frame Frame(string line) => Ax25Frame.FromAprsPacket(AprsPacket.Parse(line));

    private static byte[] Kiss(string line) => KissFrame.ForAx25(Frame(line)).Encode();

    [Fact]
    public async Task AppsShareTheTnc()
    {
        var port = FreePort();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = new CliHarness().RunAsync(
            ["tnc", "share", "--tnc", TncAddress, "--listen", $"127.0.0.1:{port}", "--gap", "0"],
            stop.Token
        );
        using var radio = await _tnc.AcceptTcpClientAsync(Token);
        await using var first = await App.ConnectAsync(port);
        await using var second = await App.ConnectAsync(port);

        // What the TNC hears goes to every app.
        await Task.Delay(200, Token); // both apps registered with the server
        await radio.GetStream().WriteAsync(Kiss("N1ABC>APRS,WIDE1-1:>heard on RF"), Token);
        Assert.Equal("N1ABC>APRS,WIDE1-1:>heard on RF", await first.ReadAsync());
        Assert.Equal("N1ABC>APRS,WIDE1-1:>heard on RF", await second.ReadAsync());

        // A frame an app sends in pieces reaches the TNC whole.
        var bytes = Kiss("N0CALL>APZNET,WIDE1-1:>from the first app");
        await first.SendAsync(bytes[..5]);
        await Task.Delay(100, Token);
        await first.SendAsync(bytes[5..]);
        var tnc = new App(radio);
        Assert.Equal("N0CALL>APZNET,WIDE1-1:>from the first app", await tnc.ReadAsync());
        Assert.False(second.HasData, "a frame one app sends is not echoed to the others");

        await stop.CancelAsync();
        var result = await run.WaitAsync(Wait, Token);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            $"sharing the TNC at {TncAddress} on 127.0.0.1:{port} (KISS over TCP)",
            result.Error,
            StringComparison.Ordinal
        );
        Assert.Contains("an app connected from 127.0.0.1:", result.Error, StringComparison.Ordinal);
        Assert.Contains("heard N1ABC>APRS,WIDE1-1:>heard on RF", result.Out, StringComparison.Ordinal);
        Assert.Contains("sent N0CALL>APZNET,WIDE1-1:>from the first app", result.Out, StringComparison.Ordinal);
        Assert.Contains("heard 1 frames and sent 1 from apps (0 dropped)", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventsAndFramesAreLoggedForOpenTelemetry()
    {
        var port = FreePort();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var harness = new CliHarness();
        harness.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:4317";
        var run = harness.RunAsync(
            ["tnc", "share", "--tnc", TncAddress, "--listen", $"127.0.0.1:{port}", "--gap", "0", "-q", "--log-packets"],
            stop.Token
        );
        using var radio = await _tnc.AcceptTcpClientAsync(Token);
        string remote;
        await using (var app = await App.ConnectAsync(port))
        {
            remote = app.LocalEndPoint;
            await Task.Delay(200, Token);
            await radio.GetStream().WriteAsync(Kiss("N1ABC>APRS:>heard on RF"), Token);
            Assert.Equal("N1ABC>APRS:>heard on RF", await app.ReadAsync());
            await app.SendAsync(Kiss("N0CALL>APZNET:>from an app"));
            Assert.Equal("N0CALL>APZNET:>from an app", await new App(radio).ReadAsync());
        }

        await Task.Delay(200, Token); // the app's leaving is reported
        await stop.CancelAsync();
        var result = await run.WaitAsync(Wait, Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Out); // -q still prints nothing; --log-packets only logs
        var events = harness.Logs.Entries.Where(e => e.Category == "aprscli.events").Select(e => e.Message).ToList();
        Assert.Equal(
            [
                $"Sharing the TNC at {TncAddress} on 127.0.0.1:{port}, paced for 1200 bit/s",
                $"An app connected from {remote}",
                $"The app at {remote} disconnected",
                "Stopped sharing: heard 1 frames, sent 1 from apps (0 dropped); 0 apps dropped for falling behind, 0 "
                    + "turned away",
            ],
            events
        );
        Assert.Equal(
            ["Heard from N1ABC: N1ABC>APRS:>heard on RF", "Sent for an app from N0CALL: N0CALL>APZNET:>from an app"],
            harness.Logs.Entries.Where(e => e.Category == "aprscli.packets").Select(e => e.Message)
        );
    }

    [Fact]
    public async Task FramesFromAppsArePacedForTheAir()
    {
        var port = FreePort();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = new CliHarness { Clock = clock }.RunAsync(
            ["tnc", "share", "--tnc", TncAddress, "--listen", $"127.0.0.1:{port}", "-q"],
            stop.Token
        );
        using var radio = await _tnc.AcceptTcpClientAsync(Token);
        await using var first = await App.ConnectAsync(port);
        await using var second = await App.ConnectAsync(port);
        var tnc = new App(radio);

        // A frame the TNC hears reaching an app shows the share is connected to the TNC, so it sends what apps do.
        await Task.Delay(200, Token); // both apps registered with the server
        await radio.GetStream().WriteAsync(Kiss("N1ABC>APRS:>heard on RF"), Token);
        Assert.Equal("N1ABC>APRS:>heard on RF", await first.ReadAsync());

        await first.SendAsync(Kiss("N0CALL>APZNET:>one"));
        Assert.Equal("N0CALL>APZNET:>one", await tnc.ReadAsync());
        await second.SendAsync(Kiss("N0CALL-1>APZNET:>two"));

        // Not until the first has had time to go out at 1200 bit/s, and the 500 ms gap after it.
        var busy = new FramePacer(clock, 1200, null, TimeSpan.FromMilliseconds(500)).BusyFor(
            Frame("N0CALL>APZNET:>one")
        );
        await Task.Delay(300, Token); // the frame reaches the pacer before the clock moves
        clock.Advance(busy - TimeSpan.FromMilliseconds(50));
        await Task.Delay(300, Token);
        Assert.False(radio.Available > 0, "the second frame waited for the first to go out");
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal("N0CALL-1>APZNET:>two", await tnc.ReadAsync());

        await stop.CancelAsync();
        var result = await run.WaitAsync(Wait, Token);
        Assert.Empty(result.Out);
    }

    [Fact]
    public async Task AFrameWhileTheTncIsAwayIsDroppedAndTheAppStays()
    {
        var port = FreePort();
        var closed = FreePort(); // nothing listens here
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = new CliHarness().RunAsync(
            ["tnc", "share", "--tnc", $"127.0.0.1:{closed}", "--listen", $"127.0.0.1:{port}"],
            stop.Token
        );
        await using var app = await App.ConnectAsync(port);
        await app.SendAsync(Kiss("N0CALL>APZNET:>nobody home"));
        await Task.Delay(300, Token);
        await app.SendAsync(Kiss("N0CALL>APZNET:>still here"));
        await Task.Delay(300, Token);

        await stop.CancelAsync();
        var result = await run.WaitAsync(Wait, Token);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "dropped a frame from an app: The TNC is not connected",
            result.Error,
            StringComparison.Ordinal
        );
        Assert.Contains("sent 0 from apps (2 dropped)", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppsOverTheLimitAreTurnedAway()
    {
        var port = FreePort();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = new CliHarness().RunAsync(
            ["tnc", "share", "--tnc", TncAddress, "--listen", $"127.0.0.1:{port}", "--max-clients", "1"],
            stop.Token
        );
        using var radio = await _tnc.AcceptTcpClientAsync(Token);
        await using var first = await App.ConnectAsync(port);
        await Task.Delay(200, Token);
        await using var second = await App.ConnectAsync(port);
        Assert.True(await second.ClosedAsync());

        await stop.CancelAsync();
        var result = await run.WaitAsync(Wait, Token);
        Assert.Contains("turned away 127.0.0.1:", result.Error, StringComparison.Ordinal);
        Assert.Contains("1 apps are already connected", result.Error, StringComparison.Ordinal);
        Assert.Contains("1 turned away", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ATncIsBusyWhileTheFrameCrossesTheSerialPortAndGoesOut()
    {
        var frame = Frame("N0CALL>APZNET:>one"); // 14 address bytes, control, PID and 4 of text: 20 bytes
        var kiss = KissFrame.ForAx25(frame).Encode().Length;

        var overTcp = new FramePacer(TimeProvider.System, 1200, null, TimeSpan.Zero).BusyFor(frame);
        var overSerial = new FramePacer(TimeProvider.System, 1200, 1200, TimeSpan.FromMilliseconds(500)).BusyFor(frame);

        Assert.Equal((20 + 4) * 8 * 1.2 / 1200, overTcp.TotalSeconds, 6);
        Assert.Equal(overTcp.TotalSeconds + kiss * 10.0 / 1200 + 0.5, overSerial.TotalSeconds, 6);
    }

    [Fact]
    public async Task ThePacerHoldsTheNextFrameUntilTheTncIsFree()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
        var pacer = new FramePacer(clock, 1200, null, TimeSpan.FromMilliseconds(500));
        var one = Frame("N0CALL>APZNET:>one");
        var sent = new List<(string, DateTimeOffset)>();
        ValueTask Send(Ax25Frame frame, CancellationToken _)
        {
            lock (sent)
            {
                sent.Add((frame.ToTnc2(), clock.GetUtcNow()));
            }

            return ValueTask.CompletedTask;
        }

        await pacer.SendAsync(one, Send, Token);
        var second = pacer.SendAsync(Frame("N0CALL-1>APZNET:>two"), Send, Token).AsTask();
        clock.Advance(pacer.BusyFor(one) - TimeSpan.FromMilliseconds(1));
        await Task.Delay(100, Token);
        Assert.False(second.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await second.WaitAsync(Wait, Token);

        Assert.Equal(clock.GetUtcNow(), sent[1].Item2);
    }

    [Fact]
    public async Task HealthChecksSayWhenTheTncGoesQuietAndComesBack()
    {
        var port = FreePort();
        var web = FreePort();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = new CliHarness { Clock = clock }.RunAsync(
            [
                "tnc",
                "share",
                "--tnc",
                TncAddress,
                "--listen",
                $"127.0.0.1:{port}",
                "--http",
                $"127.0.0.1:{web}",
                "--quiet-warning",
                "30m",
                "-q",
            ],
            stop.Token
        );
        using var radio = await _tnc.AcceptTcpClientAsync(Token);
        var source = $"\"{TncAddress}\":{{\"connected\":true,\"quiet\":";
        await WaitForHealthAsync(web, "200", source + "false");
        await using var app = await App.ConnectAsync(port);
        var body = await WaitForHealthAsync(web, "200", "\"apps\":1");
        Assert.Contains("\"status\":\"healthy\"", body, StringComparison.Ordinal);

        // Nothing from the TNC for 30 minutes: switched off behind a port that stays open.
        clock.Advance(TimeSpan.FromMinutes(30));
        body = await WaitForHealthAsync(web, "503", source + "true");
        Assert.Contains("\"status\":\"unhealthy\"", body, StringComparison.Ordinal);

        // The first frame brings it back.
        await radio.GetStream().WriteAsync(Kiss("N1ABC>APRS:>back on"), Token);
        Assert.Equal("N1ABC>APRS:>back on", await app.ReadAsync());
        body = await WaitForHealthAsync(web, "200", source + "false");
        Assert.Contains("\"frames.heard\":1", body, StringComparison.Ordinal);

        await stop.CancelAsync();
        var result = await run.WaitAsync(Wait, Token);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"with health checks at http://127.0.0.1:{web}/health", result.Error, StringComparison.Ordinal);
    }

    /// <summary>Asks for /health until the answer has <paramref name="status"/> and holds <paramref name="text"/>.</summary>
    private static async Task<string> WaitForHealthAsync(int port, string status, string text)
    {
        var deadline = DateTime.UtcNow + Wait;
        var response = "";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var http = new TcpClient();
                await http.ConnectAsync(IPAddress.Loopback, port, Token);
                await http.GetStream().WriteAsync("GET /health HTTP/1.1\r\nHost: x\r\n\r\n"u8.ToArray(), Token);
                response = await new StreamReader(http.GetStream()).ReadToEndAsync(Token);
                if (
                    response.StartsWith($"HTTP/1.1 {status} ", StringComparison.Ordinal)
                    && response.Contains(text, StringComparison.Ordinal)
                )
                {
                    return response;
                }
            }
            catch (SocketException)
            {
                // Not listening yet.
            }

            await Task.Delay(20, Token);
        }

        Assert.Fail($"/health never answered {status} with {text}; last answer:\n{response}");
        return response;
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("500ms", 500)]
    [InlineData("1s", 1000)]
    [InlineData("1.5", 1500)]
    public void AGapIsADuration(string text, double milliseconds)
    {
        Assert.True(TncShareCommand.TryParseGap(text, out var gap));
        Assert.Equal(milliseconds, gap.TotalMilliseconds);
    }

    [Theory]
    [InlineData(new[] { "--tnc", "serial:/dev/ttyUSB0,text" }, "receive only and can't be shared")]
    [InlineData(new[] { "--tnc", "serial:COM3@fast" }, "--tnc 'serial:COM3@fast' is not valid")]
    [InlineData(new[] { "--tnc", "localhost", "--listen", "localhost" }, "--listen 'localhost' is not valid")]
    [InlineData(new[] { "--tnc", "localhost", "--max-clients", "0" }, "--max-clients must be at least 1")]
    [InlineData(new[] { "--tnc", "localhost", "--radio-port", "16" }, "--radio-port must be 0 to 15")]
    [InlineData(new[] { "--tnc", "localhost", "--air-rate", "0" }, "--air-rate must be at least 1")]
    [InlineData(new[] { "--tnc", "localhost", "--gap", "soon" }, "--gap 'soon' is not valid")]
    [InlineData(new[] { "--tnc", "localhost", "-d", "forever" }, "--duration 'forever' is not valid")]
    [InlineData(new[] { "--tnc", "localhost", "--http", "localhost" }, "--http 'localhost' is not valid")]
    [InlineData(new[] { "--tnc", "localhost", "--log-packets" }, "--log-packets needs OTEL_EXPORTER_OTLP_ENDPOINT")]
    public async Task BadOptionsAreRefused(string[] options, string message)
    {
        var result = await new CliHarness().RunAsync(["tnc", "share", .. options], Token);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    /// <summary>A KISS app connected to the shared TNC, or the TNC's side of its connection.</summary>
    private sealed class App(TcpClient tcp) : IAsyncDisposable
    {
        private readonly KissDecoder _decoder = new();
        private readonly Queue<KissFrame> _frames = new();
        private readonly byte[] _buffer = new byte[4096];

        public bool HasData => tcp.Available > 0;

        public string LocalEndPoint
        {
            get
            {
                var local = (IPEndPoint)tcp.Client.LocalEndPoint!;
                return new IPEndPoint(local.Address.MapToIPv4(), local.Port).ToString();
            }
        }

        public static async Task<App> ConnectAsync(int port)
        {
            var deadline = DateTime.UtcNow + Wait;
            while (true)
            {
                var tcp = new TcpClient();
                try
                {
                    await tcp.ConnectAsync(IPAddress.Loopback, port, Token);
                    return new App(tcp);
                }
                catch (SocketException) when (DateTime.UtcNow < deadline)
                {
                    tcp.Dispose();
                    await Task.Delay(20, Token);
                }
            }
        }

        public async Task<string?> ReadAsync()
        {
            while (_frames.Count == 0)
            {
                var read = await tcp.GetStream().ReadAsync(_buffer, Token).AsTask().WaitAsync(Wait, Token);
                if (read == 0)
                {
                    return null;
                }

                foreach (var frame in _decoder.Decode(_buffer.AsSpan(0, read)))
                {
                    _frames.Enqueue(frame);
                }
            }

            Assert.True(_frames.Dequeue().TryGetAx25(out var ax25));
            return ax25!.ToTnc2();
        }

        public async Task SendAsync(byte[] bytes) => await tcp.GetStream().WriteAsync(bytes, Token);

        public async Task<bool> ClosedAsync()
        {
            try
            {
                return await tcp.GetStream().ReadAsync(_buffer, Token).AsTask().WaitAsync(Wait, Token) == 0;
            }
            catch (IOException)
            {
                return true;
            }
        }

        public ValueTask DisposeAsync()
        {
            tcp.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
