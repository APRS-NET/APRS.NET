using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using AprsNet.Packets;

namespace AprsNet.Kiss.Tests;

/// <summary>
/// End to end through a real modem, both ways, with no sound card. Receiving: each packet becomes AFSK audio (Dire
/// Wolf's <c>gen_packets</c>), Dire Wolf demodulates it from its standard input and serves the frames over KISS TCP,
/// and <see cref="KissTcpClient"/> must read every packet back unchanged. Transmitting: <see cref="KissTcpClient"/>
/// sends each packet to Dire Wolf over KISS, Dire Wolf writes the audio it would transmit to a file, and Dire Wolf's
/// <c>atest</c> must decode every packet from that audio unchanged.
/// </summary>
/// <remarks>
/// Opt-in: set <c>APRSNET_DIREWOLF_AUDIO=1</c> with <c>direwolf</c>, <c>gen_packets</c> and <c>atest</c> on the PATH.
/// CI does this on Ubuntu. The transmit test writes audio through ALSA's <c>file</c> plugin, so it needs Linux. The
/// audio is clean, so this checks the pipeline (audio, AX.25, KISS, parsing), not modem performance.
/// </remarks>
public sealed partial class KissAudioEndToEndTests
{
    // One of each packet type, with and without a digipeater path; all ASCII, as gen_packets reads text.
    private static readonly string[] Packets =
    [
        "N0CALL-9>APZNET,WIDE1-1,WIDE2-1:!4903.50N/07201.75W>Mobile with a path",
        "N0CALL>APZNET:=4903.50N/07201.75W-Home, messaging",
        "N0CALL>APZNET:=/5L!!<*e7>7P[Compressed",
        "N0CALL-9>S32UVT:`(_fn\"Oj/]Mic-E",
        "N0CALL>APZNET:@092345z4903.50N/07201.75W_220/004g005t077r000p000P000h50b09900",
        "N0CALL>APZNET:;LEADER   *092345z4903.50N/07201.75W>Object",
        "N0CALL>APZNET:)AID #2!4903.50N/07201.75WA",
        "N0CALL>APZNET::N1ABC    :Hello{01",
        "N1ABC>APZNET::N0CALL   :ack01",
        "N0CALL>APZNET:>Status text",
        "N0CALL>APZNET:T#005,199,000,255,073,123,01101001",
        "N0CALL>APZNET::N0CALL   :PARM.Battery,Temp",
        "N0CALL>APZNET:_10090556c220s004g005t077r000p000P000h50b09900wRSW",
        "N0CALL>APZNET:<IGATE,MSG_CNT=30,LOC_CNT=0",
        "N0CALL>APZNET:}N1ABC>APRS,TCPIP,N0CALL*:>Gated from APRS-IS",
        "N0CALL>APZNET:{Q1qwerty",
    ];

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(1200)]
    [InlineData(9600)]
    public async Task PacketsSurviveAudioDireWolfAndKiss(int baud)
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("APRSNET_DIREWOLF_AUDIO") != "1",
            "Set APRSNET_DIREWOLF_AUDIO=1 with direwolf and gen_packets on the PATH to run through a real modem."
        );
        var token = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("aprsnet-audio-");
        Process? direwolf = null;
        try
        {
            var audio = await GenerateAudioAsync(directory.FullName, baud, token);
            var port = FreePort();
            var config = Path.Combine(directory.FullName, "direwolf.conf");
            await File.WriteAllLinesAsync(
                config,
                [
                    "ADEVICE stdin null",
                    "ARATE 44100",
                    "CHANNEL 0",
                    "MYCALL N0CALL",
                    $"MODEM {baud}",
                    $"KISSPORT {port}",
                    "AGWPORT 0",
                ],
                token
            );
            direwolf = Start("direwolf", $"-c {config} -t 0 -q hd", directory.FullName);

            await using var tnc = await ConnectAsync(port, token);
            using var reading = CancellationTokenSource.CreateLinkedTokenSource(token);
            reading.CancelAfter(Wait);
            var received = ReadAsync(tnc, Packets.Length, reading.Token);

            // Standard input stays open until every packet is back: at its end Dire Wolf exits, and under load it can do
            // so before it has decoded the last packet or sent its frame. A second of silence lets the last one finish.
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

    [Theory]
    [InlineData(1200)]
    [InlineData(9600)]
    public async Task PacketsSentThroughDireWolfDecodeFromItsAudio(int baud)
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("APRSNET_DIREWOLF_AUDIO") != "1",
            "Set APRSNET_DIREWOLF_AUDIO=1 with direwolf and atest on the PATH to run through a real modem."
        );
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Dire Wolf writes the transmitted audio through ALSA.");
        var token = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("aprsnet-tx-");
        Process? direwolf = null;
        try
        {
            var port = FreePort();
            var config = Path.Combine(directory.FullName, "direwolf.conf");
            await File.WriteAllLinesAsync(
                config,
                [
                    // Receive from standard input, which stays silent so the channel is clear; transmit into tx.raw
                    // through ALSA's file plugin. Dire Wolf cuts long device names, so the path is relative.
                    "ADEVICE stdin file:FILE=tx.raw,FORMAT=raw",
                    "ARATE 44100",
                    "CHANNEL 0",
                    "MYCALL N0CALL",
                    $"MODEM {baud}",
                    $"KISSPORT {port}",
                    "AGWPORT 0",
                ],
                token
            );
            direwolf = Start("direwolf", $"-c {config} -t 0 -q hd", directory.FullName);

            await using var tnc = await ConnectAsync(port, token, KissTransmitLimit.Off);
            foreach (var packet in Packets)
            {
                await tnc.SendAsync(AprsPacket.Parse(packet), token);
            }

            Assert.Equal(Packets, await DecodeTransmittedAsync(directory.FullName, baud, Packets.Length, token));
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

    /// <summary>
    /// Decodes the audio Dire Wolf has written so far with <c>atest</c>, again and again until it holds
    /// <paramref name="count"/> packets: Dire Wolf transmits in its own time, a frame or a few at once.
    /// </summary>
    private static async Task<string[]> DecodeTransmittedAsync(
        string directory,
        int baud,
        int count,
        CancellationToken token
    )
    {
        var raw = Path.Combine(directory, "tx.raw");
        var wav = Path.Combine(directory, "tx.wav");
        var deadline = DateTime.UtcNow + Wait;
        string[] decoded = [];
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500, token);
            if (!File.Exists(raw))
            {
                continue;
            }

            byte[] samples;
            await using (var file = new FileStream(raw, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                samples = new byte[file.Length & ~1];
                await file.ReadExactlyAsync(samples, token);
            }

            await File.WriteAllBytesAsync(wav, [.. WavHeader(samples.Length), .. samples], token);
            using var atest = Start("atest", $"-B {baud} tx.wav", directory, captureOutput: true);
            var output = await atest.StandardOutput.ReadToEndAsync(token);
            await atest.WaitForExitAsync(token);
            decoded =
            [
                .. output
                    .Split('\n')
                    .Select(line => AnsiEscape().Replace(line, "").Trim())
                    .Where(line => line.StartsWith("[0] ", StringComparison.Ordinal))
                    .Select(line => line[4..]),
            ];
            if (decoded.Length >= count)
            {
                return decoded;
            }
        }

        return decoded;
    }

    /// <summary>A WAV header for 16-bit mono samples at 44.1 kHz, the format Dire Wolf transmits in here.</summary>
    private static byte[] WavHeader(int dataLength)
    {
        using var header = new MemoryStream();
        using var writer = new BinaryWriter(header);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(44100);
        writer.Write(44100 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        writer.Flush();
        return header.ToArray();
    }

    [GeneratedRegex(@"\x1b\[[0-9;]*[A-Za-z]")]
    private static partial Regex AnsiEscape();

    /// <summary>
    /// The audio for every packet, one after another. gen_packets keeps a line ending as part of the packet, so each
    /// packet is generated from its own file with none.
    /// </summary>
    private static async Task<byte[]> GenerateAudioAsync(string directory, int baud, CancellationToken token)
    {
        using var audio = new MemoryStream();
        for (var i = 0; i < Packets.Length; i++)
        {
            var text = Path.Combine(directory, $"packet{i}.txt");
            var wav = Path.Combine(directory, $"packet{i}.wav");
            await File.WriteAllBytesAsync(text, Encoding.ASCII.GetBytes(Packets[i]), token);
            using var generate = Start("gen_packets", $"-r 44100 -B {baud} -o {wav} {text}", directory);
            await generate.WaitForExitAsync(token);
            Assert.Equal(0, generate.ExitCode);
            audio.Write(PcmSamples(await File.ReadAllBytesAsync(wav, token)));
        }

        return audio.ToArray();
    }

    /// <summary>The samples in a WAV file's data chunk; Dire Wolf reads raw samples from standard input.</summary>
    private static ReadOnlySpan<byte> PcmSamples(byte[] wav)
    {
        var at = 12; // after "RIFF", the size and "WAVE"
        while (at + 8 <= wav.Length)
        {
            var size = BitConverter.ToInt32(wav, at + 4);
            if (Encoding.ASCII.GetString(wav, at, 4) == "data")
            {
                return wav.AsSpan(at + 8, Math.Min(size, wav.Length - at - 8));
            }

            at += 8 + size + (size & 1);
        }

        throw new InvalidDataException("The WAV file has no data chunk.");
    }

    private static async Task<string[]> ReadAsync(KissTcpClient tnc, int count, CancellationToken token)
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

    private static async Task<KissTcpClient> ConnectAsync(
        int port,
        CancellationToken token,
        KissTransmitLimit? transmitLimit = null
    )
    {
        var deadline = DateTime.UtcNow + Wait;
        while (true)
        {
            var client = new KissTcpClient("127.0.0.1", port, transmitLimit: transmitLimit);
            try
            {
                await client.ConnectAsync(token);
                return client;
            }
            catch (SocketException) when (DateTime.UtcNow < deadline)
            {
                await client.DisposeAsync();
                await Task.Delay(100, token);
            }
        }
    }

    private static Process Start(string program, string arguments, string directory, bool captureOutput = false)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(program, arguments)
            {
                WorkingDirectory = directory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.Start();
        if (!captureOutput)
        {
            process.BeginOutputReadLine();
        }

        process.BeginErrorReadLine();
        return process;
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
