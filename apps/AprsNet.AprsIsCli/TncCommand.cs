using System.CommandLine;
using System.Globalization;
using AprsNet.Kiss;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The <c>tnc</c> commands: talk to a KISS TNC over TCP (Dire Wolf, a soundmodem, a network TNC) or on a serial port
/// (a USB TNC). <c>tnc listen</c> prints the APRS packets it hears, in the same formats as <c>listen</c>; <c>tnc
/// ports</c> lists the serial ports present.
/// </summary>
internal sealed class TncCommand(CliEnvironment environment)
{
    private readonly Option<string> _tnc = new("--tnc", "-t")
    {
        Description =
            $"The TNC as host[:port] (default port {KissTcpClient.DefaultPort}, Dire Wolf's KISS port), or a serial "
            + $"TNC as serial:PORT[@BAUD], such as serial:/dev/ttyUSB0 or serial:COM3@57600 (default "
            + $"{KissSerialOptions.DefaultBaudRate} baud). Add ,setup=th-d72 to switch a radio's built-in TNC into "
            + "KISS, or ,text to read a radio in its APRS mode (receive only).",
        Required = true,
    };

    private readonly Option<int> _radioPort = new("--radio-port")
    {
        Description = "The radio port on a multi-port TNC, 0 to 15. Default: 0.",
        DefaultValueFactory = _ => 0,
    };

    private readonly Option<string> _format = new("--format")
    {
        Description = "Output: raw (the TNC2 line), decoded, json or pretty, as for listen.",
        DefaultValueFactory = _ => "raw",
    };

    private readonly Option<int?> _count = new("--count", "-n") { Description = "Stop after this many packets." };

    private readonly Option<bool> _reconnect = new("--reconnect")
    {
        Description =
            "If the TNC closes the connection, is unplugged or cannot be reached, keep trying (waiting 5s, doubling "
            + "to 60s; 2s to 30s for a serial port) until --count, --duration or Ctrl+C. Without it, the first loss "
            + "ends the command.",
    };

    private readonly Option<string?> _duration = new("--duration", "-d")
    {
        Description = "Stop after this long, for example 90s, 10m, 2h or 1d.",
    };

    private readonly Option<string?> _quietWarning = new("--quiet-warning")
    {
        Description =
            "Warn when the TNC sends nothing at all for this long, for example 10m: it may be switched off behind a "
            + "port that stays open. Pick longer than the channel is ever quiet. Needs --reconnect. Default: off.",
    };

    public Command Build()
    {
        _format.AcceptOnlyFromAmong("raw", "decoded", "json", "pretty");
        var listen = new Command("listen", "Connect to a KISS TNC and print the APRS packets it hears.")
        {
            _tnc,
            _radioPort,
            _format,
            _count,
            _duration,
            _reconnect,
            _quietWarning,
        };
        listen.SetAction(ListenAsync);
        var ports = new Command("ports", "List the serial ports present, for --tnc serial:PORT.");
        ports.SetAction(ListPortsAsync);
        var tnc = new Command("tnc", "Talk to a KISS TNC over TCP, such as Dire Wolf, or on a serial port.")
        {
            listen,
            new SendCommand(environment, toTnc: true).Build(),
            new TncShareCommand(environment).Build(),
            ports,
        };
        foreach (var command in new TncConsoleCommand(environment).Build())
        {
            tnc.Subcommands.Add(command);
        }

        return tnc;
    }

    private async Task<int> ListenAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var text = parseResult.GetValue(_tnc)!.Trim();
        if (!TncAddress.TryParse(text, out var address))
        {
            return await UsageAsync(
                $"--tnc '{text}' is not valid. Use {TncAddress.Forms}, such as {TncAddress.Examples}."
            );
        }

        if (parseResult.GetValue(_radioPort) is < 0 or > 15)
        {
            return await UsageAsync("--radio-port must be 0 to 15.");
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
            if (!DurationParser.TryParse(quietText, out var quiet) || quiet <= TimeSpan.Zero)
            {
                return await UsageAsync($"--quiet-warning '{quietText}' is not valid. Use a duration such as 10m.");
            }

            if (!parseResult.GetValue(_reconnect))
            {
                return await UsageAsync("--quiet-warning needs --reconnect.");
            }

            quietWarning = quiet;
        }

        var format = Enum.Parse<OutputFormat>(parseResult.GetValue(_format)!, ignoreCase: true);
        var count = parseResult.GetValue(_count);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (duration is { } limit)
        {
            run.CancelAfter(limit);
        }

        if (parseResult.GetValue(_reconnect))
        {
            await using var connection = environment.CreateTncConnection(
                address,
                parseResult.GetValue(_radioPort),
                quietWarning
            );
            ReportConnection(connection, address.ToString(), environment.Error);
            try
            {
                await PrintAsync(connection.ReadPacketsAsync(run.Token), format, count);
            }
            catch (OperationCanceledException) when (run.IsCancellationRequested)
            {
                // Duration reached or Ctrl+C.
            }

            return ExitCodes.Success;
        }

        TncLink client;
        try
        {
            client = await TncLink.OpenAsync(address, parseResult.GetValue(_radioPort), null, run.Token);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }
        catch (Exception ex) when (TncLink.IsLinkFailure(ex))
        {
            await environment.Error.WriteLineAsync($"aprscli: could not connect to the TNC at {address}: {ex.Message}");
            return ExitCodes.ConnectionFailed;
        }

        await using var _ = client;
        client.Warning += (_, e) => ReportWarning(address.ToString(), e, environment.Error);

        try
        {
            if (await PrintAsync(client.ReadPacketsAsync(run.Token), format, count))
            {
                return ExitCodes.Success;
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }
        catch (Exception ex) when (TncLink.IsLinkFailure(ex))
        {
            await environment.Error.WriteLineAsync($"aprscli: lost the TNC at {address}: {ex.Message}");
            return ExitCodes.ConnectionFailed;
        }

        await environment.Error.WriteLineAsync($"aprscli: the TNC at {address} closed the connection.");
        return ExitCodes.ConnectionFailed;
    }

    /// <summary>
    /// Prints a line when a TNC is lost, with the wait before the next try, and when it is back, and its warnings: for
    /// <c>tnc listen --reconnect</c> and <c>serve</c>.
    /// </summary>
    internal static void ReportConnection(KissConnection connection, string name, TextWriter error)
    {
        connection.Warning += (_, e) => ReportWarning(name, e, error);
        var lost = false;
        connection.StateChanged += (_, e) =>
        {
            if (e.State == KissConnectionState.Connected && lost)
            {
                lost = false;
                error.WriteLine($"aprscli: reconnected to the TNC at {name}.");
            }
            else if (e.State == KissConnectionState.Reconnecting)
            {
                lost = true;
                var retry = e.RetryIn!.Value.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);
                error.WriteLine(
                    e.Error is null
                        ? $"aprscli: the TNC at {name} closed the connection; reconnecting in {retry}s."
                        : $"aprscli: lost the TNC at {name}: {e.Error.Message}; reconnecting in {retry}s."
                );
            }
        };
    }

    /// <summary>Prints a TNC's warning, or that it is over.</summary>
    internal static void ReportWarning(string name, KissWarningEventArgs warning, TextWriter error) =>
        error.WriteLine(
            warning.IsActive
                ? $"aprscli: warning: the TNC at {name}: {warning.Message}"
                : $"aprscli: the TNC at {name}: {warning.Message}"
        );

    private async Task<int> ListPortsAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var names = environment.GetSerialPortNames();
        if (names.Count == 0)
        {
            await environment.Error.WriteLineAsync("aprscli: no serial ports found.");
        }

        foreach (var name in names)
        {
            await environment.Out.WriteLineAsync(name);
        }

        return ExitCodes.Success;
    }

    /// <summary>Prints packets until the stream ends (false) or <paramref name="count"/> is reached (true).</summary>
    private async Task<bool> PrintAsync(IAsyncEnumerable<AprsPacket> packets, OutputFormat format, int? count)
    {
        var received = 0;
        await foreach (var packet in packets)
        {
            var receivedAt = environment.TimeProvider.GetUtcNow();
            await environment.Out.WriteLineAsync(
                format switch
                {
                    OutputFormat.Decoded => LineFormatter.EscapeForConsole(PacketFormatter.Decoded(packet)),
                    OutputFormat.Json => PacketFormatter.Json(packet, receivedAt),
                    OutputFormat.Pretty => PrettyFormatter.Format(packet, receivedAt, color: false)
                        + Environment.NewLine,
                    _ => LineFormatter.EscapeForConsole(packet.ToString()),
                }
            );
            if (count is { } limit && ++received >= limit)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads a TNC address as host or host:port, the port defaulting to Dire Wolf's KISS port.</summary>
    internal static bool TryParseAddress(string text, out string host, out int port)
    {
        text = text.Trim();
        var colon = text.LastIndexOf(':');
        host = colon < 0 ? text : text[..colon];
        port = KissTcpClient.DefaultPort;
        return host.Length > 0
            && (
                colon < 0
                || int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port)
            )
            && port is >= 1 and <= 65535;
    }

    private async Task<int> UsageAsync(string message)
    {
        await environment.Error.WriteLineAsync($"aprscli: {message}");
        await environment.Error.WriteLineAsync("Try 'aprscli tnc listen --help'.");
        return ExitCodes.Usage;
    }
}
