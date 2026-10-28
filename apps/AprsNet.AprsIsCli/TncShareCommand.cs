using System.CommandLine;
using System.Globalization;
using System.Net;
using AprsNet.Ax25;
using AprsNet.Gateway;
using AprsNet.Kiss;

namespace AprsNet.AprsIsCli;

/// <summary>
/// <c>tnc share</c>: serves one TNC to several KISS apps over TCP, as Dire Wolf serves its own on port 8001, so a
/// hardware TNC on a serial port can be used from anywhere on the network. Unlike a byte-for-byte serial bridge such
/// as ser2net, it reads each app's frames whole and writes each one to the TNC in one go, then waits for the TNC to
/// have sent it before writing the next, so a TNC with a small buffer gets one complete frame at a time.
/// </summary>
internal sealed class TncShareCommand(CliEnvironment environment)
{
    private const string DefaultListen = "127.0.0.1:8001";

    private readonly Option<string> _tnc = new("--tnc", "-t")
    {
        Description =
            $"The TNC to share: serial:PORT[@BAUD], such as serial:/dev/ttyUSB0@1200 (default "
            + $"{KissSerialOptions.DefaultBaudRate} baud), host[:port] for a TNC over TCP that takes one "
            + "connection, or agw:host[:port] for an AGWPE server. Add ,setup=th-d72 to switch a radio's built-in "
            + "TNC into KISS.",
        Required = true,
    };

    private readonly Option<string> _listen = new("--listen", "-l")
    {
        Description =
            $"Where apps connect, as address:port. Default: {DefaultListen}, this machine only; use 0.0.0.0:8001 for "
            + "the network.",
        DefaultValueFactory = _ => DefaultListen,
    };

    private readonly Option<int> _maxClients = new("--max-clients")
    {
        Description = "How many apps may connect at once. Default: 16.",
        DefaultValueFactory = _ => 16,
    };

    private readonly Option<int> _radioPort = new("--radio-port")
    {
        Description = "The radio port on a multi-port TNC, 0 to 15. Default: 0.",
        DefaultValueFactory = _ => 0,
    };

    private readonly Option<int> _airRate = new("--air-rate")
    {
        Description =
            "The radio's data rate in bits per second, to tell how long the TNC takes to send a frame. Default: 1200.",
        DefaultValueFactory = _ => 1200,
    };

    private readonly Option<string> _gap = new("--gap")
    {
        Description =
            "How much longer to wait after each frame's time on the air before writing the next, for the TNC's "
            + "keyup delay and a busy channel, for example 500ms or 1s; 0 for none. Default: 500ms.",
        DefaultValueFactory = _ => "500ms",
    };

    private readonly Option<bool> _transmitLimit = new("--transmit-limit")
    {
        Description =
            "Limit what every app sends together to 3 frames at once, then one each 10s. Default: off, as apps pace "
            + "themselves.",
    };

    private readonly Option<bool> _quiet = new("--quiet", "-q")
    {
        Description = "Don't print each frame heard and sent; still report apps and the TNC on stderr.",
    };

    private readonly Option<string?> _duration = new("--duration", "-d")
    {
        Description = "Stop after this long, for example 90s, 10m, 2h or 1d. Default: until Ctrl+C.",
    };

    private readonly Option<string?> _quietWarning = new("--quiet-warning")
    {
        Description =
            "Warn when the TNC sends nothing at all for this long, for example 10m: it may be switched off behind a "
            + "port that stays open. Default: off.",
    };

    private readonly Option<string?> _http = new("--http")
    {
        Description =
            "Also answer health checks at http://address:port/health, for example 127.0.0.1:8002: whether the TNC is "
            + "connected, quiet or not in KISS mode, as JSON, with 503 when it isn't up. A TNC quiet for "
            + "--quiet-warning is down. Default: off.",
    };

    private readonly Option<bool> _logPackets = new("--log-packets")
    {
        Description =
            $"Also log each frame heard and sent to OpenTelemetry (needs {Logging.OtlpEndpointVariable}); never "
            + "printed by this.",
    };

    private readonly Option<string?> _config = ConfigFile.CreateOption();

    private readonly Option<bool> _check = ConfigFile.CreateCheckOption();

    public Command Build()
    {
        var command = new Command(
            "share",
            "Share a TNC with KISS apps over TCP: each app gets every frame the TNC hears, and each frame an app sends "
                + "goes to the TNC whole, one at a time."
        )
        {
            _tnc,
            _listen,
            _maxClients,
            _radioPort,
            _airRate,
            _gap,
            _transmitLimit,
            _quiet,
            _duration,
            _quietWarning,
            _http,
            _logPackets,
            _config,
            _check,
        };
        command.SetAction(RunAsync);
        return command;
    }

    private async Task<int> RunAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var text = parseResult.GetValue(_tnc)!.Trim();
        if (!TncAddress.TryParse(text, out var address))
        {
            return await UsageAsync(
                $"--tnc '{text}' is not valid. Use {TncAddress.Forms}, such as {TncAddress.Examples}."
            );
        }

        if (address.TextMode)
        {
            return await UsageAsync("--tnc with ,text is receive only and can't be shared; leave ,text off.");
        }

        var listenText = parseResult.GetValue(_listen)!;
        if (
            !IPEndPoint.TryParse(listenText, out var listen)
            || (listen.Port == 0 && !listenText.EndsWith(":0", StringComparison.Ordinal))
        )
        {
            return await UsageAsync(
                $"--listen '{listenText}' is not valid. Use address:port, for example {DefaultListen}."
            );
        }

        var maxClients = parseResult.GetValue(_maxClients);
        if (maxClients < 1)
        {
            return await UsageAsync("--max-clients must be at least 1.");
        }

        var radioPort = parseResult.GetValue(_radioPort);
        if (radioPort is < 0 or > 15)
        {
            return await UsageAsync("--radio-port must be 0 to 15.");
        }

        var airRate = parseResult.GetValue(_airRate);
        if (airRate < 1)
        {
            return await UsageAsync("--air-rate must be at least 1 bit per second.");
        }

        var gapText = parseResult.GetValue(_gap)!;
        if (!TryParseGap(gapText, out var gap))
        {
            return await UsageAsync($"--gap '{gapText}' is not valid. Use a duration such as 500ms or 1s, or 0.");
        }

        TimeSpan? duration = null;
        if (parseResult.GetValue(_duration) is { } durationText)
        {
            if (!DurationParser.TryParse(durationText, out var parsed))
            {
                return await UsageAsync($"--duration '{durationText}' is not valid. Use a duration such as 10m.");
            }

            duration = parsed;
        }

        TimeSpan? quietWarning = null;
        if (parseResult.GetValue(_quietWarning) is { } quietText)
        {
            if (!DurationParser.TryParse(quietText, out var quiet))
            {
                return await UsageAsync($"--quiet-warning '{quietText}' is not valid. Use a duration such as 10m.");
            }

            quietWarning = quiet;
        }

        IPEndPoint? http = null;
        if (parseResult.GetValue(_http) is { } httpText && !IPEndPoint.TryParse(httpText, out http))
        {
            return await UsageAsync($"--http '{httpText}' is not valid. Use address:port, for example 127.0.0.1:8002.");
        }

        var logPackets = parseResult.GetValue(_logPackets);
        if (logPackets && !Logging.IsOtlpConfigured(environment.GetEnvironmentVariable))
        {
            return await UsageAsync(
                $"--log-packets needs {Logging.OtlpEndpointVariable} to be set; frames are only logged to OpenTelemetry."
            );
        }

        if (parseResult.GetValue(_check))
        {
            var baud = address.IsSerial ? $" at {address.BaudRate.ToString(CultureInfo.InvariantCulture)} baud" : "";
            await environment.Error.WriteLineAsync(
                $"aprscli: the settings are good. Would share the TNC at {address}{baud} on {listen} with up to "
                    + $"{maxClients.ToString(CultureInfo.InvariantCulture)} apps, pacing frames for "
                    + $"{airRate.ToString(CultureInfo.InvariantCulture)} bit/s on the air with a "
                    + $"{gap.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)} ms gap"
                    + (http is null ? "." : $", with health checks at http://{http}/health.")
            );
            return ExitCodes.Success;
        }

        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (duration is { } limit)
        {
            run.CancelAfter(limit);
        }

        var error = environment.Error;
        var events = environment.LoggerFactory.CreateLogger(Logging.EventCategory);
        var packetLog = logPackets ? environment.LoggerFactory.CreateLogger(Logging.PacketCategory) : null;
        var quietOutput = parseResult.GetValue(_quiet);
        var name = address.ToString();
        await using var tnc = environment.CreateTncConnection(
            address,
            radioPort,
            quietWarning,
            parseResult.GetValue(_transmitLimit)
                ? new KissTransmitLimit(timeProvider: environment.TimeProvider)
                : KissTransmitLimit.Off
        );
        TncCommand.ReportConnection(tnc, name, error, events);

        var pacer = new FramePacer(
            environment.TimeProvider,
            airRate,
            address.IsSerial ? address.BaudRate : null,
            gap,
            name
        );
        long sent = 0;
        long dropped = 0;
        long heard = 0;
        await using var server = new KissTcpServer(listen, maxClients)
        {
            Transmit = async (frame, token) =>
            {
                try
                {
                    await pacer.SendAsync(frame, tnc.SendFrameAsync, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    Interlocked.Increment(ref dropped);
                    lock (error)
                    {
                        error.WriteLine($"aprscli: dropped a frame from an app: {ex.Message}");
                    }

                    events.FrameDropped(ex.Message);

                    throw;
                }

                Interlocked.Increment(ref sent);
                Print("sent", frame);
            },
        };
        server.ClientConnected += (_, e) =>
        {
            Report($"aprscli: an app connected from {e.RemoteEndPoint}.");
            events.AppConnected(e.RemoteEndPoint);
        };
        server.ClientDisconnected += (_, e) =>
        {
            if (e.FellBehind)
            {
                Report($"aprscli: disconnected the app at {e.RemoteEndPoint}: it fell too far behind.");
                events.AppFellBehind(e.RemoteEndPoint);
            }
            else
            {
                Report($"aprscli: the app at {e.RemoteEndPoint} disconnected.");
                events.AppDisconnected(e.RemoteEndPoint);
            }
        };
        server.ClientRefused += (_, e) =>
        {
            Report($"aprscli: turned away {e.RemoteEndPoint}: {maxClients} apps are already connected.");
            events.AppTurnedAway(e.RemoteEndPoint, maxClients);
        };

        // Quiet for --quiet-warning counts as down: likely switched off behind a port that stays open.
        var health = new GatewayHealth(timeProvider: environment.TimeProvider) { CountQuietAsDown = true };
        health.AddSource(name, () => tnc.IsConnected, () => tnc.IsQuiet, () => tnc.IsSendingText);
        health.AddCounter("apps", () => server.ClientCount);
        health.AddCounter("frames.heard", () => Interlocked.Read(ref heard));
        health.AddCounter("frames.sent", () => Interlocked.Read(ref sent));
        health.AddCounter("frames.dropped", () => Interlocked.Read(ref dropped));
        await using var web = http is null
            ? null
            : new GatewayWebServer(new GatewayWebServerOptions { EndPoint = http, Health = health });
        try
        {
            server.Start();
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            await error.WriteLineAsync($"aprscli: could not listen on {listen}: {ex.Message}");
            return ExitCodes.ConnectionFailed;
        }

        try
        {
            web?.Start();
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            await error.WriteLineAsync($"aprscli: could not listen on {http}: {ex.Message}");
            return ExitCodes.ConnectionFailed;
        }

        await error.WriteLineAsync(
            $"aprscli: sharing the TNC at {name} on {server.LocalEndPoint} (KISS over TCP), pacing frames for "
                + $"{airRate.ToString(CultureInfo.InvariantCulture)} bit/s on the air"
                + (web is null ? "" : $", with health checks at http://{web.LocalEndPoint}/health")
        );
        events.ShareStarted(name, server.LocalEndPoint!, airRate);

        try
        {
            // The connection reconnects on its own, so this ends only with the run.
            await foreach (var frame in tnc.ReadFramesAsync(run.Token))
            {
                Interlocked.Increment(ref heard);
                server.Publish(frame);
                Print("heard", frame);
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            // Duration reached or Ctrl+C.
        }

        await server.DisposeAsync();
        var (sentTotal, droppedTotal) = (Interlocked.Read(ref sent), Interlocked.Read(ref dropped));
        var (fellBehind, turnedAway) = (server.ClientsDroppedForOverflow, server.ClientsRefused);
        await error.WriteLineAsync(
            $"aprscli: heard {heard} frames and sent {sentTotal} from apps ({droppedTotal} dropped); {fellBehind} "
                + $"apps dropped for falling behind, {turnedAway} turned away"
        );
        events.ShareStopped(heard, sentTotal, droppedTotal, fellBehind, turnedAway);
        return ExitCodes.Success;

        void Print(string what, Ax25Frame frame)
        {
            if (quietOutput && packetLog is null)
            {
                return;
            }

            var line = LineFormatter.EscapeForConsole(frame.ToTnc2());
            if (packetLog is not null)
            {
                var source = line[..Math.Max(0, line.IndexOf('>', StringComparison.Ordinal))];
                if (what == "heard")
                {
                    packetLog.FrameHeard(source, line);
                }
                else
                {
                    packetLog.FrameSent(source, line);
                }
            }

            if (quietOutput)
            {
                return;
            }

            var at = environment.TimeProvider.GetUtcNow();
            lock (environment.Out)
            {
                environment.Out.WriteLine(
                    $"{at.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)} {what} {line}"
                );
            }
        }

        void Report(string line)
        {
            lock (error)
            {
                error.WriteLine(line);
            }
        }
    }

    /// <summary>Reads <c>--gap</c>: a duration as <c>--duration</c> takes it, milliseconds such as <c>500ms</c>, or 0.</summary>
    internal static bool TryParseGap(string text, out TimeSpan gap)
    {
        text = text.Trim();
        gap = TimeSpan.Zero;
        if (text == "0")
        {
            return true;
        }

        if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
        {
            if (
                double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
                && ms >= 0
                && ms < 3_600_000
            )
            {
                gap = TimeSpan.FromMilliseconds(ms);
                return true;
            }

            return false;
        }

        return DurationParser.TryParse(text, out gap) && gap < TimeSpan.FromHours(1);
    }

    private async Task<int> UsageAsync(string message)
    {
        await environment.Error.WriteLineAsync($"aprscli: {message}");
        await environment.Error.WriteLineAsync("Try 'aprscli tnc share --help'.");
        return ExitCodes.Usage;
    }
}

/// <summary>
/// Hands a TNC one frame at a time: each waits until the one before has had time to reach the TNC over its serial port
/// and go out on the air, plus a gap. A TNC with a small buffer then never holds more than the frame it is sending.
/// </summary>
// The semaphore never creates a wait handle (only WaitAsync is used), so it holds nothing to dispose.
#pragma warning disable CA1001
internal sealed class FramePacer(TimeProvider timeProvider, int airRate, int? serialBaud, TimeSpan gap, string tnc = "")
#pragma warning restore CA1001
{
    private readonly SemaphoreSlim _turn = new(1, 1);
    private DateTimeOffset _next = DateTimeOffset.MinValue;

    /// <summary>
    /// How long the TNC is busy with <paramref name="frame"/>: its KISS bytes over the serial port at 10 bits a byte,
    /// then the AX.25 frame on the air with its checksum and flags, allowing for the worst bit stuffing (one bit in
    /// five), then the gap.
    /// </summary>
    public TimeSpan BusyFor(Ax25Frame frame)
    {
        var serial = serialBaud is { } baud ? KissFrame.ForAx25(frame).Encode().Length * 10.0 / baud : 0;
        var air = (frame.GetBytes().Length + 2 + 2) * 8 * 1.2 / airRate;
        return TimeSpan.FromSeconds(serial + air) + gap;
    }

    /// <summary>Waits for the TNC to be free, sends <paramref name="frame"/>, and marks the TNC busy with it.</summary>
    public async ValueTask SendAsync(
        Ax25Frame frame,
        Func<Ax25Frame, CancellationToken, ValueTask> send,
        CancellationToken cancellationToken
    )
    {
        var arrived = timeProvider.GetTimestamp();
        await _turn.WaitAsync(cancellationToken);
        try
        {
            var wait = _next - timeProvider.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, timeProvider, cancellationToken);
            }

            ShareMetrics.Waited(timeProvider.GetElapsedTime(arrived), tnc);

            // Busy from when the write starts: the frame starts crossing the serial port then.
            var start = timeProvider.GetUtcNow();
            await send(frame, cancellationToken);
            _next = start + BusyFor(frame);
        }
        finally
        {
            _turn.Release();
        }
    }
}
