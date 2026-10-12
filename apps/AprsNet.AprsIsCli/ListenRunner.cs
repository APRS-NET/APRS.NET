using System.Net.Sockets;
using System.Text;
using AprsNet.AprsIsTcpClient;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli;

// ReSharper disable once GrammarMistakeInComment
/// <summary>Runs <c>aprscli listen</c>: connect, stream lines to the console and an optional capture file, stop on count, duration or Ctrl+C.</summary>
internal static class ListenRunner
{
    /// <summary>Not an exit code: <see cref="ConnectAsync"/> succeeded.</summary>
    private const int _connected = -1;

    private static readonly TimeSpan _maxReconnectDelay = TimeSpan.FromSeconds(60);

    public static async Task<int> RunAsync(
        ListenOptions options,
        CliEnvironment environment,
        CancellationToken cancellationToken
    )
    {
        var logger = environment.LoggerFactory.CreateLogger("aprscli");
        var clientOptions = new AprsIsClientOptions
        {
            Endpoint = options.Endpoint,
            Callsign = options.Callsign,
            Passcode = options.Passcode,
            Filter = options.Filter,
            SoftwareName = "aprscli",
            TimeProvider = environment.TimeProvider,
        };

        StreamWriter? capture = null;
        try
        {
            if (options.Capture is not null)
            {
                capture = OpenCapture(options.Capture);
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await environment.Error.WriteLineAsync(
                $"aprscli: cannot open capture file '{options.Capture}': {ex.Message}"
            );
            return ExitCodes.Error;
        }

        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Duration is { } duration)
        {
            run.CancelAfter(duration);
        }

        var packetLog = options.LogPackets ? environment.LoggerFactory.CreateLogger(Logging.PacketCategory) : null;
        var progress = new Progress();
        var connectedOnce = false;
        var delay = environment.ReconnectDelay;

        try
        {
            while (true)
            {
                int code;
                await using (var client = environment.CreateClient(clientOptions, logger))
                {
                    code = await ConnectAsync(client, options, environment, connectedOnce, run.Token);
                    if (code == _connected)
                    {
                        connectedOnce = true;
                        delay = environment.ReconnectDelay;
                        code = await StreamAsync(client, options, environment, capture, packetLog, progress, run.Token);
                    }
                }

                // Only a dropped or unreachable server is retried, and only after the first login worked, so a
                // wrong server or callsign still fails fast.
                if (code != ExitCodes.ConnectionFailed || !options.Reconnect || !connectedOnce)
                {
                    return code;
                }

                await environment.Error.WriteLineAsync(
                    $"aprscli: reconnecting to {options.Endpoint} in {delay.TotalSeconds:0.#}s."
                );
                try
                {
                    await Task.Delay(delay, environment.TimeProvider, run.Token);
                }
                catch (OperationCanceledException) when (run.IsCancellationRequested)
                {
                    return ExitCodes.Success;
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxReconnectDelay.Ticks));
            }
        }
        finally
        {
            if (capture is not null)
            {
                await capture.DisposeAsync();
            }
        }
    }

    /// <summary>Connects and logs in. Returns <see cref="_connected"/> on success, otherwise the exit code.</summary>
    private static async Task<int> ConnectAsync(
        AprsIsClient client,
        ListenOptions options,
        CliEnvironment environment,
        bool reconnecting,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await client.ConnectAsync(cancellationToken);
            return _connected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }
        catch (LoginRejectedException ex)
        {
            await environment.Error.WriteLineAsync(
                $"aprscli: login rejected by {options.Endpoint}: {ex.ServerMessage}"
            );
            if (options.Callsign == ListenCommand.DefaultCallsign)
            {
                await environment.Error.WriteLineAsync(
                    $"Some servers refuse the placeholder callsign {ListenCommand.DefaultCallsign}. "
                        + "Run again (the rotate may pick another server) or pass --callsign."
                );
            }

            return ExitCodes.LoginFailed;
        }
        catch (LoginException ex)
        {
            await environment.Error.WriteLineAsync($"aprscli: login failed on {options.Endpoint}: {ex.Message}");

            // A login that times out or is cut off after an earlier success is a flaky server, not bad settings.
            return reconnecting ? ExitCodes.ConnectionFailed : ExitCodes.LoginFailed;
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
        {
            await environment.Error.WriteLineAsync($"aprscli: could not connect to {options.Endpoint}: {ex.Message}");
            return ExitCodes.ConnectionFailed;
        }
    }

    private static async Task<int> StreamAsync(
        AprsIsClient client,
        ListenOptions options,
        CliEnvironment environment,
        StreamWriter? capture,
        ILogger? packetLog,
        Progress progress,
        CancellationToken cancellationToken
    )
    {
        // Packet watchdog for --max-silence. It is separate from the client's idle timeout because a server can keep
        // sending '#' keepalives after its own feed has stalled, so the connection looks alive but delivers nothing.
        using var silence = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var keepalivesAtLastPacket = client.ServerCommentsReceived;
        try
        {
            if (options.MaxSilence is { } armed)
            {
                silence.CancelAfter(armed);
            }

            await foreach (var line in client.ReadLinesAsync(silence.Token))
            {
                if (options.MaxSilence is { } maxSilence)
                {
                    silence.CancelAfter(maxSilence); // restart the wait for the next packet
                }

                keepalivesAtLastPacket = client.ServerCommentsReceived;
                var stamp = LineFormatter.Timestamp(environment.TimeProvider.GetUtcNow());

                capture?.Write($"{stamp} {line}\n");
                if (packetLog is not null && packetLog.IsEnabled(LogLevel.Information))
                {
                    packetLog.PacketReceived(Source(line), LineFormatter.EscapeForConsole(line));
                }

                if (!options.Quiet)
                {
                    try
                    {
                        var text = LineFormatter.EscapeForConsole(line);
                        await environment.Out.WriteLineAsync(options.Timestamps ? $"{stamp} {text}" : text);
                    }
                    catch (IOException)
                    {
                        return ExitCodes.Success; // output closed, for example `aprscli listen | head`
                    }
                }

                if (options.Count is { } count && ++progress.Received >= count)
                {
                    return ExitCodes.Success;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }
        catch (OperationCanceledException) when (silence.IsCancellationRequested)
        {
            // Keepalives in the gap tell the two failures apart: some means the server's feed stalled, none means
            // the link itself died (which the client's idle timeout normally reports first).
            var keepalives = client.ServerCommentsReceived - keepalivesAtLastPacket;
            await environment.Error.WriteLineAsync(
                $"aprscli: no packets from {client.Login?.Server ?? options.Endpoint.ToString()} for "
                    + $"{DurationParser.Format(options.MaxSilence!.Value)} "
                    + $"(server keepalives in that time: {keepalives})."
            );
            return ExitCodes.ConnectionFailed;
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
        {
            await environment.Error.WriteLineAsync($"aprscli: connection lost: {ex.Message}");
            return ExitCodes.ConnectionFailed;
        }

        await environment.Error.WriteLineAsync($"aprscli: {options.Endpoint} closed the connection.");
        return ExitCodes.ConnectionFailed;
    }

    /// <summary>The sending station: everything before the first '&gt;'.</summary>
    private static string Source(string line)
    {
        var end = line.IndexOf('>', StringComparison.Ordinal);
        return end > 0 ? line[..end] : "";
    }

    private static StreamWriter OpenCapture(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Path.GetDirectoryName(fullPath) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        var stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read);

        // Latin-1 writes every byte of the raw line back out unchanged. AutoFlush keeps an interrupted capture intact.
        return new StreamWriter(stream, Encoding.Latin1) { AutoFlush = true };
    }
}

/// <summary>Packets delivered across every connection of one run, so <c>--count</c> spans reconnects.</summary>
internal sealed class Progress
{
    public int Received;
}

internal static partial class PacketLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Packet from {Source}: {Packet}")]
    public static partial void PacketReceived(this ILogger logger, string source, string packet);
}
