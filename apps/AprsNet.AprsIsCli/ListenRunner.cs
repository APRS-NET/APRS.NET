using System.Net.Sockets;
using System.Text;
using AprsNet.AprsIsTcpClient;
using AprsNet.Corpus;
using AprsNet.Packets;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli;

// ReSharper disable once GrammarMistakeInComment
/// <summary>Runs <c>aprscli listen</c>: connect, stream lines to the console and an optional capture file, stop on count, duration or Ctrl+C.</summary>
internal static class ListenRunner
{
    private static readonly TimeSpan _maxReconnectDelay = new AprsIsClientOptions().MaxReconnectDelay;
    private static readonly TimeSpan _noRetryDelay = TimeSpan.FromHours(1);

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
            UseTls = options.UseTls,
            Callsign = options.Callsign,
            Passcode = options.Passcode,
            ListenOnly = options.ListenOnly,
            Filter = options.Filter,
            SoftwareName = "aprscli",
            TimeProvider = environment.TimeProvider,
            // Without --reconnect the reporter closes the connection at its first drop. A first retry as late as the
            // longest backoff makes sure no reconnect attempt can start before that close lands.
            ReconnectDelay = options.Reconnect ? environment.ReconnectDelay : _noRetryDelay,
            MaxReconnectDelay = options.Reconnect ? _maxReconnectDelay : _noRetryDelay,
            MaxPacketSilence = options.MaxSilence,
        };

        CorpusWriter? capture = null;
        try
        {
            if (options.Capture is not null)
            {
                // The corpus format: Latin-1, so every byte of the raw line is kept, and flushed line by line.
                capture = CorpusFile.OpenAppend(options.Capture);
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
        try
        {
            await using var connection = environment.CreateConnection(clientOptions, logger);
            using var reporter = new ConnectionReporter(
                connection,
                environment.Error,
                options.Endpoint,
                options.Reconnect
            );
            if (await StartAsync(connection, options, environment, run.Token) is { } failed)
            {
                return failed;
            }

            var stats = options.StatsInterval is null ? null : new ListenStats(options.Callsign);
            var started = environment.TimeProvider.GetTimestamp();
            var code = await StreamAsync(connection, options, environment, capture, packetLog, stats, run.Token);
            await reporter.Closing;
            if (stats is not null)
            {
                var elapsed = environment.TimeProvider.GetElapsedTime(started);
                await environment.Error.WriteLineAsync($"aprscli: {stats.Summary(elapsed)}");
            }

            return reporter.ExitCode ?? code;
        }
        finally
        {
            if (capture is not null)
            {
                await capture.DisposeAsync();
            }
        }
    }

    /// <summary>Connects and logs in for the first time. Returns null on success, otherwise the exit code.</summary>
    private static async Task<int?> StartAsync(
        AprsIsConnection connection,
        ListenOptions options,
        CliEnvironment environment,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await connection.StartAsync(cancellationToken);
            return null;
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
            if (options.ListenOnly)
            {
                await environment.Error.WriteLineAsync(
                    $"This server refused the listener ID {options.Callsign}. "
                        + "Run again (the rotate may pick another server) or pass --callsign."
                );
            }
            else if (options.Callsign == ListenCommand.DefaultCallsign)
            {
                await environment.Error.WriteLineAsync(
                    $"Some servers refuse the placeholder callsign {ListenCommand.DefaultCallsign}. "
                        + "Run again (the rotate may pick another server) or pass --callsign."
                );
            }

            return ExitCodes.LoginFailed;
        }
        catch (KissTncLoginException)
        {
            await environment.Error.WriteLineAsync(ConnectionReporter.KissTnc(options.Endpoint));
            return ExitCodes.LoginFailed;
        }
        catch (LoginException ex)
        {
            await environment.Error.WriteLineAsync($"aprscli: login failed on {options.Endpoint}: {ex.Message}");
            return ExitCodes.LoginFailed;
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
        {
            await environment.Error.WriteLineAsync($"aprscli: could not connect to {options.Endpoint}: {ex.Message}");
            return ExitCodes.ConnectionFailed;
        }
    }

    /// <summary>
    /// Prints lines until the count, duration or Ctrl+C. Drops are the <see cref="ConnectionReporter"/>'s: with
    /// <c>--reconnect</c> the stream carries on, otherwise it ends after the lines already received.
    /// </summary>
    private static async Task<int> StreamAsync(
        AprsIsConnection connection,
        ListenOptions options,
        CliEnvironment environment,
        CorpusWriter? capture,
        ILogger? packetLog,
        ListenStats? stats,
        CancellationToken cancellationToken
    )
    {
        var received = 0;
        var started = environment.TimeProvider.GetTimestamp();
        var nextSummary = options.StatsInterval;
        try
        {
            await foreach (var line in connection.ReadLinesAsync(cancellationToken))
            {
                var receivedAt = environment.TimeProvider.GetUtcNow();
                var stamp = LineFormatter.Timestamp(receivedAt);
                if (stats is not null)
                {
                    // Checked as packets arrive, so a summary needs no timer of its own; a silent feed prints none.
                    stats.Add(AprsPacket.Parse(line));
                    var elapsed = environment.TimeProvider.GetElapsedTime(started);
                    if (elapsed >= nextSummary)
                    {
                        await environment.Error.WriteLineAsync($"aprscli: {stats.Summary(elapsed)}");
                        nextSummary = elapsed + options.StatsInterval;
                    }
                }

                if (capture is not null)
                {
                    await capture.WriteAsync(new CorpusLine(receivedAt, line), CancellationToken.None); // a line read is kept
                }
                if (packetLog is not null && packetLog.IsEnabled(LogLevel.Information))
                {
                    packetLog.PacketReceived(Source(line), LineFormatter.EscapeForConsole(line));
                }

                if (!options.Quiet)
                {
                    try
                    {
                        var text = options.Format switch
                        {
                            OutputFormat.Decoded => LineFormatter.EscapeForConsole(
                                PacketFormatter.Decoded(AprsPacket.Parse(line))
                            ),
                            OutputFormat.Json => PacketFormatter.Json(AprsPacket.Parse(line), receivedAt),
                            OutputFormat.Pretty => PrettyFormatter.Format(
                                AprsPacket.Parse(line),
                                receivedAt,
                                options.Color
                            ) + Environment.NewLine,
                            _ => LineFormatter.EscapeForConsole(line),
                        };

                        // JSON and pretty carry their own received time, so --timestamps only prefixes raw and decoded.
                        await environment.Out.WriteLineAsync(
                            options is { Timestamps: true, Format: OutputFormat.Raw or OutputFormat.Decoded }
                                ? $"{stamp} {text}"
                                : text
                        );
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

        // The line stream only ends when the connection closes, which the reporter does after a failure.
        return ExitCodes.ConnectionFailed;
    }

    /// <summary>The sending station: everything before the first '&gt;'.</summary>
    private static string Source(string line)
    {
        var end = line.IndexOf('>', StringComparison.Ordinal);
        return end > 0 ? line[..end] : "";
    }
}

internal static partial class PacketLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Packet from {Source}: {Packet}")]
    public static partial void PacketReceived(this ILogger logger, string source, string packet);
}
