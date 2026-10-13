using System.CommandLine;
using System.Globalization;
using AprsNet.AprsIsTcpClient;

namespace AprsNet.AprsIsCli;

/// <summary>The <c>listen</c> command: connect to APRS-IS with a filter and stream raw packet lines.</summary>
internal sealed class ListenCommand
{
    private const string _passcodeVariable = "APRS_PASSCODE";
    public const string DefaultCallsign = "N0CALL";
    private const string DefaultMaxSilenceText = "10m";

    private static readonly string[] _regions = ["noam", "soam", "euro", "asia", "aunz"];

    private readonly Option<string?> _callsign = new("--callsign", "-c")
    {
        Description = $"Login callsign, with optional SSID. Default: {DefaultCallsign}.",
    };

    private readonly Option<int?> _passcode = new("--passcode")
    {
        Description =
            $"APRS-IS passcode. Default: -1 (read-only). Prefer the {_passcodeVariable} environment variable; "
            + "an option value is visible in the process list and your shell history. Never printed or logged.",
    };

    private readonly Option<string?> _server = new("--server", "-s")
    {
        Description =
            "Server as host[:port]. Cannot be combined with --region, --port or --full-feed when it includes a port. "
            + "Default: rotate.aprs2.net:14580.",
    };

    private readonly Option<string?> _region = new("--region", "-r")
    {
        Description = "Regional rotate: noam, soam, euro, asia or aunz. Cannot be combined with --server.",
    };

    private readonly Option<int?> _port = new("--port")
    {
        Description = "TCP port, for --region or a --server without a port. Default: 14580.",
    };

    private readonly Option<bool> _fullFeed = new("--full-feed")
    {
        Description = "Use the full feed port (10152). All traffic, so expect a heavy stream.",
    };

    private readonly Option<string?> _filter = new("--filter", "-f")
    {
        Description = "APRS-IS filter sent with the login, for example \"r/35/-97/200\" or \"t/m\".",
    };

    private readonly Option<bool> _timestamps = new("--timestamps", "-t")
    {
        Description = "Prefix each printed line with a UTC timestamp.",
    };

    private readonly Option<string?> _capture = new("--capture")
    {
        Description =
            "Append every packet to this file as \"<UTC timestamp> <raw line>\". The directory is created if needed.",
    };

    private readonly Option<bool> _quiet = new("--quiet", "-q")
    {
        Description = "Do not print packets to the console (useful with --capture).",
    };

    private readonly Option<bool> _logPackets = new("--log-packets")
    {
        Description =
            $"Also log every packet to OpenTelemetry. Needs {Logging.OtlpEndpointVariable}. Never written to the console.",
    };

    private readonly Option<int?> _count = new("--count", "-n") { Description = "Stop after this many packets." };

    private readonly Option<string?> _duration = new("--duration", "-d")
    {
        Description = "Stop after this long, for example 90s, 10m, 2h or 1d.",
    };

    private readonly Option<bool> _reconnect = new("--reconnect")
    {
        Description =
            "If the connection drops or the server goes silent for 90s, reconnect (waiting 5s, doubling to 60s) "
            + "and keep going until --count, --duration or Ctrl+C. Only after the first login succeeds.",
    };

    private readonly Option<string?> _maxSilence = new("--max-silence")
    {
        Description =
            $"Treat the connection as failed after this long with no packets, even if the server still sends "
            + $"keepalives (its feed may have stalled). Default: {DefaultMaxSilenceText} with --reconnect, "
            + "otherwise off. Use 'off' to disable.",
    };

    private readonly Option<string> _format = new("--format")
    {
        Description =
            "Console output: raw (the line as received), decoded (one readable line of decoded fields), json (one "
            + "JSON object per line) or pretty (a labelled block per packet). Capture files are always raw.",
        DefaultValueFactory = _ => "raw",
    };

    private readonly Option<string> _color = new("--color")
    {
        Description =
            "Colour for --format pretty: auto (only on a terminal, and not when NO_COLOR is set), always or never.",
        DefaultValueFactory = _ => "auto",
    };

    private readonly CliEnvironment _environment;

    public ListenCommand(CliEnvironment environment)
    {
        _environment = environment;
        _region.AcceptOnlyFromAmong(_regions);
        _format.AcceptOnlyFromAmong("raw", "decoded", "json", "pretty");
        _color.AcceptOnlyFromAmong("auto", "always", "never");
    }

    public Command Build()
    {
        var command = new Command("listen", "Connect to APRS-IS and stream raw packet lines.");
        foreach (
            var option in new Option[]
            {
                _callsign,
                _passcode,
                _server,
                _region,
                _port,
                _fullFeed,
                _filter,
                _timestamps,
                _capture,
                _quiet,
                _logPackets,
                _count,
                _duration,
                _reconnect,
                _maxSilence,
                _format,
                _color,
            }
        )
        {
            command.Options.Add(option);
        }

        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                if (!TryBind(parseResult, out var options, out var error))
                {
                    await _environment.Error.WriteLineAsync($"aprscli: {error}");
                    await _environment.Error.WriteLineAsync("Try 'aprscli listen --help'.");
                    return ExitCodes.Usage;
                }

                return await ListenRunner.RunAsync(options, _environment, cancellationToken);
            }
        );

        return command;
    }

    private bool TryBind(ParseResult parseResult, out ListenOptions options, out string error)
    {
        options = null!;

        var callsign = (parseResult.GetValue(_callsign) ?? DefaultCallsign).Trim().ToUpperInvariant();

        var passcode = -1;
        if (parseResult.GetValue(_passcode) is { } explicitPasscode)
        {
            passcode = explicitPasscode;
        }
        else if (_environment.GetEnvironmentVariable(_passcodeVariable) is { Length: > 0 } fromEnvironment)
        {
            if (
                !int.TryParse(
                    fromEnvironment,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out passcode
                )
            )
            {
                error = $"{_passcodeVariable} must be an integer.";
                return false;
            }
        }

        if (!TryResolveEndpoint(parseResult, out var endpoint, out error))
        {
            return false;
        }

        var count = parseResult.GetValue(_count);
        if (count is < 1)
        {
            error = "--count must be at least 1.";
            return false;
        }

        TimeSpan? duration = null;
        if (parseResult.GetValue(_duration) is { } durationText)
        {
            if (!DurationParser.TryParse(durationText, out var parsed))
            {
                error = $"--duration '{durationText}' is not valid. Use a number and a unit: 90s, 10m, 2h or 1d.";
                return false;
            }

            duration = parsed;
        }

        var reconnect = parseResult.GetValue(_reconnect);
        TimeSpan? maxSilence = null;
        var maxSilenceText = parseResult.GetValue(_maxSilence) ?? (reconnect ? DefaultMaxSilenceText : null);
        if (maxSilenceText is not null && !maxSilenceText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            if (!DurationParser.TryParse(maxSilenceText, out var parsedSilence))
            {
                error = $"--max-silence '{maxSilenceText}' is not valid. Use 'off' or a duration such as 5m.";
                return false;
            }

            maxSilence = parsedSilence;
        }

        var capture = parseResult.GetValue(_capture);
        if (capture is not null && string.IsNullOrWhiteSpace(capture))
        {
            error = "--capture needs a file path.";
            return false;
        }

        var quiet = parseResult.GetValue(_quiet);
        if (quiet && capture is null && count is null && duration is null)
        {
            error = "--quiet without --capture, --count or --duration would run silently with no result.";
            return false;
        }

        var logPackets = parseResult.GetValue(_logPackets);
        if (logPackets && !Logging.IsOtlpConfigured(_environment.GetEnvironmentVariable))
        {
            error =
                $"--log-packets needs {Logging.OtlpEndpointVariable} to be set; packets are only logged to OpenTelemetry.";
            return false;
        }

        var filter = parseResult.GetValue(_filter);

        // Surface the client's own validation (callsign shape, control characters in the filter) as usage errors.
        try
        {
            new AprsIsClientOptions
            {
                Endpoint = endpoint,
                Callsign = callsign,
                Passcode = passcode,
                Filter = filter,
            }.Validate();
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        options = new ListenOptions
        {
            Endpoint = endpoint,
            Callsign = callsign,
            Passcode = passcode,
            Filter = filter,
            Timestamps = parseResult.GetValue(_timestamps),
            Format = Enum.Parse<OutputFormat>(parseResult.GetValue(_format)!, ignoreCase: true),
            Color = parseResult.GetValue(_color) switch
            {
                "always" => true,
                "never" => false,
                _ => _environment.OutIsTerminal
                    && string.IsNullOrEmpty(_environment.GetEnvironmentVariable("NO_COLOR"))
                    && _environment.GetEnvironmentVariable("TERM") != "dumb",
            },
            Capture = capture,
            Quiet = quiet,
            LogPackets = logPackets,
            Count = count,
            Duration = duration,
            Reconnect = reconnect,
            MaxSilence = maxSilence,
        };
        error = "";
        return true;
    }

    private bool TryResolveEndpoint(ParseResult parseResult, out AprsIsEndpoint endpoint, out string error)
    {
        endpoint = null!;
        error = "";

        var server = parseResult.GetValue(_server);
        var region = parseResult.GetValue(_region);
        var port = parseResult.GetValue(_port);
        var fullFeed = parseResult.GetValue(_fullFeed);

        if (server is not null && region is not null)
        {
            error = "--server and --region cannot be used together.";
            return false;
        }

        if (port is not null && fullFeed)
        {
            error = "--port and --full-feed cannot be used together.";
            return false;
        }

        if (port is not (null or (>= 1 and <= 65535)))
        {
            error = "--port must be between 1 and 65535.";
            return false;
        }

        var chosenPort = port ?? (fullFeed ? AprsIsPorts.FullFeed : AprsIsPorts.UserDefinedFilter);
        var hasPortOverride = port is not null || fullFeed;

        if (server is not null)
        {
            if (!AprsIsEndpoint.TryParse(server, out var parsed))
            {
                error = $"--server '{server}' is not valid. Use host or host:port.";
                return false;
            }

            if (HasExplicitPort(server))
            {
                if (hasPortOverride)
                {
                    error = "--server already includes a port; do not combine it with --port or --full-feed.";
                    return false;
                }

                endpoint = parsed;
            }
            else
            {
                endpoint = parsed.WithPort(chosenPort);
            }

            return true;
        }

        var rotate = region switch
        {
            "noam" => AprsIsEndpoint.NorthAmerica,
            "soam" => AprsIsEndpoint.SouthAmerica,
            "euro" => AprsIsEndpoint.Europe,
            "asia" => AprsIsEndpoint.Asia,
            "aunz" => AprsIsEndpoint.Oceania,
            _ => AprsIsEndpoint.Rotate,
        };
        endpoint = rotate.WithPort(chosenPort);
        return true;
    }

    private static bool HasExplicitPort(string server)
    {
        server = server.Trim();
        return server.StartsWith('[')
            ? server.Contains("]:", StringComparison.Ordinal)
            : server.Contains(':', StringComparison.Ordinal);
    }
}
