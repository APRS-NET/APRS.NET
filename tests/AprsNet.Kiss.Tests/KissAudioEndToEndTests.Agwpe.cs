using AprsNet.Packets;

namespace AprsNet.Kiss.Tests;

/// <summary>
/// The same round trips through Dire Wolf's AGW port with <see cref="AgwpeConnection"/>: received packets come from
/// audio Dire Wolf demodulates, and sent ones must decode from the audio it would transmit.
/// </summary>
public sealed partial class KissAudioEndToEndTests
{
    [Fact]
    public async Task PacketsSurviveAudioDireWolfAndAgwpe()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("APRSNET_DIREWOLF_AUDIO") != "1",
            "Set APRSNET_DIREWOLF_AUDIO=1 with direwolf and gen_packets on the PATH to run through a real modem."
        );
        var token = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("aprsnet-agwpe-");
        System.Diagnostics.Process? direwolf = null;
        try
        {
            var audio = await GenerateAudioAsync(directory.FullName, 1200, token);
            var port = FreePort();
            var config = Path.Combine(directory.FullName, "direwolf.conf");
            await File.WriteAllLinesAsync(
                config,
                ["ADEVICE stdin null", "ARATE 44100", "CHANNEL 0", "MYCALL N0CALL", "KISSPORT 0", $"AGWPORT {port}"],
                token
            );
            direwolf = Start("direwolf", $"-c {config} -t 0 -q hd", directory.FullName);

            await using var tnc = Agwpe(port);
            using var reading = CancellationTokenSource.CreateLinkedTokenSource(token);
            reading.CancelAfter(Wait);
            var received = ReadAsync(tnc, Packets.Length, reading.Token);
            // Dire Wolf has answered the port list, so it has the request for raw frames, sent before it.
            await UntilAsync(() => tnc.Ports.Count > 0, token);
            Assert.Equal("2005.127", tnc.ServerVersion);

            await direwolf.StandardInput.BaseStream.WriteAsync(audio, token);
            await direwolf.StandardInput.BaseStream.WriteAsync(new byte[44100 * 2], token);
            await direwolf.StandardInput.BaseStream.FlushAsync(token);

            Assert.Equal(Packets, await received);
        }
        finally
        {
            if (direwolf is { HasExited: false })
            {
                direwolf.Kill();
            }

            direwolf?.Dispose();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PacketsSentThroughAgwpeDecodeFromDireWolfsAudio()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("APRSNET_DIREWOLF_AUDIO") != "1",
            "Set APRSNET_DIREWOLF_AUDIO=1 with direwolf and atest on the PATH to run through a real modem."
        );
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Dire Wolf writes the transmitted audio through ALSA.");
        var token = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("aprsnet-agwpe-tx-");
        System.Diagnostics.Process? direwolf = null;
        try
        {
            var port = FreePort();
            var config = Path.Combine(directory.FullName, "direwolf.conf");
            await File.WriteAllLinesAsync(
                config,
                [
                    "ADEVICE stdin file:FILE=tx.raw,FORMAT=raw",
                    "ARATE 44100",
                    "CHANNEL 0",
                    "MYCALL N0CALL",
                    "KISSPORT 0",
                    $"AGWPORT {port}",
                ],
                token
            );
            direwolf = Start("direwolf", $"-c {config} -t 0 -q hd", directory.FullName);

            await using var tnc = Agwpe(port);
            using var reading = CancellationTokenSource.CreateLinkedTokenSource(token);
            var frames = tnc.ReadFramesAsync(reading.Token).GetAsyncEnumerator(reading.Token);
            var next = frames.MoveNextAsync().AsTask();
            await UntilAsync(() => tnc.IsConnected, token);
            foreach (var packet in Packets)
            {
                await tnc.SendAsync(AprsPacket.Parse(packet), token);
            }

            Assert.Equal(Packets, await DecodeTransmittedAsync(directory.FullName, 1200, Packets.Length, token));
            await tnc.DisposeAsync();
            Assert.False(await next.WaitAsync(Wait, token));
            await frames.DisposeAsync();
        }
        finally
        {
            if (direwolf is { HasExited: false })
            {
                direwolf.Kill();
            }

            direwolf?.Dispose();
            directory.Delete(recursive: true);
        }
    }

    /// <summary>A connection to Dire Wolf's AGW port that retries quickly while it starts.</summary>
    private static AgwpeConnection Agwpe(int port) =>
        new(
            new AgwpeOptions
            {
                Host = "127.0.0.1",
                Port = port,
                ReconnectDelay = TimeSpan.FromMilliseconds(100),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(100),
                TransmitLimit = KissTransmitLimit.Off,
            }
        );

    private static async Task<string[]> ReadAsync(AgwpeConnection tnc, int count, CancellationToken token)
    {
        var packets = new List<string>();
        await foreach (var packet in tnc.ReadPacketsAsync(token))
        {
            packets.Add(packet.ToString());
            if (packets.Count == count)
            {
                break;
            }
        }

        return [.. packets];
    }

    private static async Task UntilAsync(Func<bool> condition, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for Dire Wolf.");
            await Task.Delay(50, token);
        }
    }
}
