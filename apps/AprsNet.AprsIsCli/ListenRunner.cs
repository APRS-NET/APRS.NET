using System.Net.Sockets;
using System.Text;
using AprsNet.AprsIsTcpClient;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli;

/// <summary>Runs <c>aprscli listen</c>: connect, stream lines to the console and an optional capture file, stop on count, duration or Ctrl+C.</summary>
internal static class ListenRunner
{
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

        await using var client = environment.CreateClient(clientOptions, logger);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Duration is { } duration)
        {
            run.CancelAfter(duration);
        }

        try
        {
            try
            {
                await client.ConnectAsync(run.Token);
            }
            catch (OperationCanceledException) when (run.IsCancellationRequested)
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
                return ExitCodes.LoginFailed;
            }
            catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
            {
                await environment.Error.WriteLineAsync(
                    $"aprscli: could not connect to {options.Endpoint}: {ex.Message}"
                );
                return ExitCodes.ConnectionFailed;
            }

            var packetLog = options.LogPackets ? environment.LoggerFactory.CreateLogger(Logging.PacketCategory) : null;
            return await StreamAsync(client, options, environment, capture, packetLog, run.Token);
        }
        finally
        {
            if (capture is not null)
            {
                await capture.DisposeAsync();
            }
        }
    }

    private static async Task<int> StreamAsync(
        AprsIsClient client,
        ListenOptions options,
        CliEnvironment environment,
        StreamWriter? capture,
        ILogger? packetLog,
        CancellationToken cancellationToken
    )
    {
        var received = 0;
        try
        {
            await foreach (var line in client.ReadLinesAsync(cancellationToken))
            {
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

                if (options.Count is { } count && ++received >= count)
                {
                    return ExitCodes.Success;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }
        catch (Exception ex) when (ex is SocketException or IOException)
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

internal static partial class PacketLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Packet from {Source}: {Packet}")]
    public static partial void PacketReceived(this ILogger logger, string source, string packet);
}
