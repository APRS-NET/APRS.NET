using System.Net;
using System.Net.Sockets;
using AprsNet.Ax25;
using AprsNet.Kiss;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli.Tests;

/// <summary><c>aprscli tnc send</c> against a fake TNC on loopback; nothing is transmitted.</summary>
public sealed class TncSendTests : IDisposable
{
    private const string Callsign = "KD9XYZ-9";
    private readonly TcpListener _tnc = new(IPAddress.Loopback, 0);

    public TncSendTests() => _tnc.Start();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Address => $"127.0.0.1:{((IPEndPoint)_tnc.LocalEndpoint).Port}";

    public void Dispose() => _tnc.Dispose();

    [Theory]
    [InlineData("status|Testing")]
    [InlineData("position|--lat|49.05833|--lon|-72.02917|--symbol|/>|--comment|mobile")]
    [InlineData("object|LEADER|--lat|49.05833|--lon|-72.02917|--symbol|/>")]
    [InlineData("weather|--temp|68|--humidity|50|--lat|49.05833|--lon|-72.02917")]
    [InlineData("telemetry|--seq|12|--analog|138,42")]
    public async Task EachKindSendsOneFrameToTheTnc(string kind)
    {
        var args = kind.Split('|');
        var dryRun = await new CliHarness().RunAsync(["tnc", "send", .. args, "-c", Callsign, "-n"], Token);
        var line = dryRun.Out.TrimEnd();
        Assert.Equal(0, dryRun.ExitCode);
        Assert.DoesNotContain('\n', line);

        var (result, received) = await SendAsync(["tnc", "send", .. args, "-c", Callsign, "--tnc", Address]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(line, result.Out.TrimEnd());
        Assert.Equal(KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line))).Encode(), received);
    }

    [Fact]
    public async Task UsesTheUsualRfPathUnlessToldOtherwise()
    {
        var (usual, _) = await SendAsync(["tnc", "send", "status", "Testing", "-c", Callsign, "--tnc", Address]);
        var direct = await new CliHarness().RunAsync(
            ["tnc", "send", "status", "Testing", "-c", Callsign, "--path", "", "-n"],
            Token
        );
        var custom = await new CliHarness().RunAsync(
            ["tnc", "send", "status", "Testing", "-c", Callsign, "--path", "WIDE2-2", "-n"],
            Token
        );

        Assert.Equal($"KD9XYZ-9>APZNET,WIDE1-1,WIDE2-1:>Testing{Environment.NewLine}", usual.Out);
        Assert.Equal($"KD9XYZ-9>APZNET:>Testing{Environment.NewLine}", direct.Out);
        Assert.Equal($"KD9XYZ-9>APZNET,WIDE2-2:>Testing{Environment.NewLine}", custom.Out);
    }

    [Fact]
    public async Task AMessageGoesOutWithAnIdAndAReplyFromTheRadioAcknowledgesIt()
    {
        var run = new CliHarness().RunAsync(
            [
                "tnc",
                "send",
                "message",
                "N1ABC-7",
                "Hello",
                "-c",
                Callsign,
                "--tnc",
                Address,
                "--wait-replies",
                "0",
                "--show-received",
            ],
            Token
        );
        using var tnc = await _tnc.AcceptTcpClientAsync(Token);
        var heard = new Heard(tnc.GetStream());

        Assert.Equal("KD9XYZ-9>APZNET,WIDE1-1,WIDE2-1::N1ABC-7  :Hello{01}", await heard.NextAsync());

        // The radio answers with a reply-ack: it delivers the message, and the reply is acked in turn.
        await tnc.GetStream().WriteAsync(Kiss($"N1ABC-7>APRS,WIDE1-1::{Callsign} :Hi back{{AB}}01"), Token);
        Assert.Equal("KD9XYZ-9>APZNET,WIDE1-1,WIDE2-1::N1ABC-7  :ackAB}01", await heard.NextAsync());
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Reply from N1ABC-7: Hi back", result.Out, StringComparison.Ordinal);
        Assert.Contains("Acknowledged by N1ABC-7 after 1 attempt.", result.Out, StringComparison.Ordinal);
        Assert.Contains($"< N1ABC-7>APRS,WIDE1-1::{Callsign} :Hi back{{AB}}01", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMessageNobodyAcknowledgesOnTheAirFailsWhenItsRetriesRunOut()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var run = new CliHarness { Clock = clock }.RunAsync(
            [
                "tnc",
                "send",
                "message",
                "N1ABC-7",
                "Hello",
                "-c",
                Callsign,
                "--tnc",
                Address,
                "--retry",
                "none",
                "--wait-replies",
                "0",
            ],
            Token
        );
        using var tnc = await _tnc.AcceptTcpClientAsync(Token);
        var heard = new Heard(tnc.GetStream());
        Assert.EndsWith(":Hello{01}", await heard.NextAsync(), StringComparison.Ordinal);

        clock.Advance(TimeSpan.FromMinutes(2));
        var result = await run;

        Assert.Equal(5, result.ExitCode);
        Assert.Contains("no acknowledgement after 1 attempt.", result.Error, StringComparison.Ordinal);
    }

    private static byte[] Kiss(string line) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line))).Encode();

    /// <summary>The TNC's side of the link: each frame aprscli sent, as a TNC2 line.</summary>
    private sealed class Heard(NetworkStream stream)
    {
        private readonly KissDecoder _decoder = new();
        private readonly Queue<string> _lines = new();
        private readonly byte[] _buffer = new byte[4096];

        public async Task<string> NextAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (_lines.Count == 0)
            {
                var read = await stream.ReadAsync(_buffer, timeout.Token);
                Assert.NotEqual(0, read);
                foreach (var frame in _decoder.Decode(_buffer.AsSpan(0, read)))
                {
                    Assert.True(frame.TryGetAx25(out var ax25));
                    _lines.Enqueue(ax25!.ToAprsPacket().ToString());
                }
            }

            return _lines.Dequeue();
        }
    }

    [Fact]
    public async Task TelemetryDefinitionsGoFirstOnTheChosenRadioPort()
    {
        var (result, received) = await SendAsync([
            "tnc",
            "send",
            "telemetry",
            "--seq",
            "1",
            "--analog",
            "1,2",
            "--names",
            "Vbat,Temp",
            "--units",
            "V,C",
            "-c",
            Callsign,
            "--tnc",
            Address,
            "--radio-port",
            "2",
        ]);

        Assert.Equal(0, result.ExitCode);
        var frames = new KissDecoder().Decode(received);
        Assert.Equal(3, frames.Count);
        Assert.All(frames, f => Assert.Equal(2, f.Port));
        var lines = frames.Select(f => f.TryGetAx25(out var ax25) ? ax25!.ToTnc2() : "").ToArray();
        Assert.Equal(result.Out.TrimEnd().Split(Environment.NewLine), lines);
        Assert.Contains(":PARM.", lines[0], StringComparison.Ordinal);
        Assert.Contains(":UNIT.", lines[1], StringComparison.Ordinal);
        Assert.Contains("T#001", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenTheTransmitLimitSpreadsFramesOut()
    {
        var harness = new CliHarness { TransmitLimit = () => new KissTransmitLimit(1, TimeSpan.FromMilliseconds(200)) };

        var (result, received) = await SendAsync(
            [
                "tnc",
                "send",
                "telemetry",
                "--seq",
                "1",
                "--analog",
                "1",
                "--names",
                "Vbat",
                "-c",
                Callsign,
                "--tnc",
                Address,
            ],
            harness
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, new KissDecoder().Decode(received).Count);
        Assert.Contains("sending the rest one every 0.2s, the transmit limit", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "status", "x", "-c", Callsign }, "--tnc is required")]
    [InlineData(new[] { "status", "x", "-c", Callsign, "--tnc", "tnc:x" }, "--tnc 'tnc:x' is not valid")]
    [InlineData(
        new[] { "status", "x", "-c", Callsign, "--tnc", "tnc", "--radio-port", "16" },
        "--radio-port must be 0 to 15"
    )]
    [InlineData(new[] { "status", "x", "-c", "N0CALL", "--tnc", "tnc" }, "N0CALL is a placeholder")]
    [InlineData(new[] { "status", "x", "-c", "KD9XYZ-L", "-n" }, "")]
    [InlineData(
        new[] { "message", "N1ABC", "hi", "-c", Callsign, "--tnc", "tnc", "--wait-replies", "soon" },
        "--wait-replies"
    )]
    [InlineData(new[] { "message", "N1ABC", "hi", "-c", Callsign, "--tnc", "tnc", "--filter", "b/ANSRVR" }, "--filter")]
    public async Task RefusesWhatItCannotSend(string[] args, string message)
    {
        var result = await new CliHarness().RunAsync(["tnc", "send", .. args], Token);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
        Assert.Equal("", result.Out);
    }

    [Fact]
    public async Task ReportsAnUnreachableTnc()
    {
        var port = ((IPEndPoint)_tnc.LocalEndpoint).Port;
        _tnc.Stop();

        var result = await new CliHarness().RunAsync(
            ["tnc", "send", "status", "x", "-c", Callsign, "--tnc", $"127.0.0.1:{port}"],
            Token
        );

        Assert.Equal(2, result.ExitCode);
        Assert.Contains($"could not send through the TNC at 127.0.0.1:{port}", result.Error, StringComparison.Ordinal);
    }

    /// <summary>Runs the command while the fake TNC records every byte it is sent.</summary>
    private async Task<(CliResult Result, byte[] Received)> SendAsync(string[] args, CliHarness? harness = null)
    {
        var receiving = Task.Run(
            async () =>
            {
                using var tnc = await _tnc.AcceptTcpClientAsync(Token);
                using var received = new MemoryStream();
                await tnc.GetStream().CopyToAsync(received, Token);
                return received.ToArray();
            },
            Token
        );
        var result = await (harness ?? new CliHarness()).RunAsync(args, Token);
        return (result, await receiving.WaitAsync(TimeSpan.FromSeconds(10), Token));
    }
}
