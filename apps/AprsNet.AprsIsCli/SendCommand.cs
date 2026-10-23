using System.CommandLine;
using System.Globalization;
using AprsNet.AprsIsTcpClient;
using AprsNet.Ax25;
using AprsNet.Beacons;
using AprsNet.Kiss;
using AprsNet.Messaging;
using AprsNet.Packets;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The <c>send</c> command: build a status report, message, position, object, weather or telemetry report and send it to APRS-IS on a verified
/// login. <c>--dry-run</c> prints the packet without connecting. With <paramref name="toTnc"/>, the same commands make
/// <c>tnc send</c>, which sends the packets through a KISS TNC to transmit on the air.
/// </summary>
internal sealed class SendCommand(CliEnvironment environment, bool toTnc = false)
{
    /// <summary>The path <c>tnc send</c> uses unless <c>--path</c> is given: the usual two-hop path.</summary>
    public const string DefaultRfPath = "WIDE1-1,WIDE2-1";

    private const string _passcodeVariable = "APRS_PASSCODE";

    /// <summary>The experimental TOCALL range (APZxxx) until APRS.NET has a registered one.</summary>
    public const string DefaultTocall = "APZNET";

    private readonly Option<string?> _callsign = new("--callsign", "-c")
    {
        Description = "Your callsign, with optional SSID, as the sender and the login. Required.",
        Recursive = true,
    };

    private readonly Option<int?> _passcode = new("--passcode")
    {
        Description =
            $"APRS-IS passcode for the callsign. Prefer the {_passcodeVariable} environment variable; an option value "
            + "is visible in the process list and your shell history. Never printed or logged.",
        Recursive = true,
    };

    private readonly Option<bool> _tls = new("--tls")
    {
        Description =
            $"Connect over TLS, checking the server's certificate. Default server: {ListenCommand.TlsDefault}; a "
            + $"--server without a port uses {AprsIsPorts.Tls}. Cannot be combined with --region.",
        Recursive = true,
    };

    private readonly Option<string?> _server = new("--server", "-s")
    {
        Description = "Server as host[:port]. Default: rotate.aprs2.net:14580.",
        Recursive = true,
    };

    private readonly Option<string?> _region = new("--region", "-r")
    {
        Description = "Regional rotate: noam, soam, euro, asia or aunz. Cannot be combined with --server.",
        Recursive = true,
    };

    private readonly Option<string> _tocall = new("--tocall")
    {
        Description = $"Destination TOCALL that identifies the software. Default: {DefaultTocall} (experimental).",
        DefaultValueFactory = _ => DefaultTocall,
        Recursive = true,
    };

    private readonly Option<string?> _path = new("--path")
    {
        Description = toTnc
            ? $"Digipeater path, comma-separated. Default: {DefaultRfPath}; use \"\" for none (direct only)."
            : "Digipeater path, comma-separated, for example WIDE1-1. Default: none (APRS-IS only).",
        Recursive = true,
    };

    private readonly Option<string?> _tnc = new("--tnc", "-t")
    {
        Description = $"The TNC as host[:port]. Default port: {KissTcpClient.DefaultPort} (Dire Wolf's KISS port).",
        Recursive = true,
    };

    private readonly Option<int> _radioPort = new("--radio-port")
    {
        Description = "The radio port on a multi-port TNC, 0 to 15. Default: 0.",
        DefaultValueFactory = _ => 0,
        Recursive = true,
    };

    private readonly Option<bool> _dryRun = new("--dry-run", "-n")
    {
        Description = toTnc
            ? "Print the packet that would be transmitted, without connecting to the TNC."
            : "Print the packet that would be sent, without connecting.",
        Recursive = true,
    };

    private readonly Option<double?> _latitude = new("--lat") { Description = "Latitude in degrees, north positive." };
    private readonly Option<double?> _longitude = new("--lon") { Description = "Longitude in degrees, east positive." };

    private readonly Option<string> _symbol = new("--symbol")
    {
        Description = "Symbol as table and code, for example /> (car) or /- (house). Default: /- .",
        DefaultValueFactory = _ => "/-",
    };

    private readonly Option<string?> _comment = new("--comment") { Description = "Comment after the position." };

    private readonly Option<string> _waitReplies = new("--wait-replies")
    {
        Description =
            "After the message is answered, keep listening this long for replies, acknowledging and printing each, "
            + "for example 30s or 5m. 0 stops at once. Default: 2m.",
        DefaultValueFactory = _ => "2m",
    };

    private readonly Option<bool> _showReceived = new("--show-received")
    {
        Description =
            "Also print every packet received while the message runs, as '< ' and the packet: what comes back, "
            + "including what isn't a message to you.",
    };

    private readonly Option<string?> _alsoFilter = new("--filter")
    {
        Description =
            "More APRS-IS filter terms while the message runs, added to g/<your callsign>: b/ANSRVR, for example, to "
            + "also see everything a service sends. Use with --show-received.",
    };

    private readonly Option<string> _retry = new("--retry")
    {
        Description =
            "How the message is resent until it is acknowledged: default (30 s, 1, 2, 4 and 8 minutes), aprs-is "
            + "(30 s, 2 and 5 minutes), slow-hf (2, 4, 8 and 16 minutes) or none (once). Default: default.",
        DefaultValueFactory = _ => "default",
    };

    private readonly Option<string> _every = new("--every")
    {
        Description = "How often to beacon, 1m or more. With --decay, the longest wait. Default: 30m.",
        DefaultValueFactory = _ => "30m",
    };

    private readonly Option<string?> _decay = new("--decay")
    {
        Description =
            "Beacon on a decay schedule instead: wait this long after the first beacon, then double the wait up to "
            + "--every, for example 1m (1, 2, 4, 8 … minutes).",
    };

    private readonly Option<string?> _beaconStatus = new("--status")
    {
        Description = "Also send this status text with each beacon.",
    };

    private readonly Option<int?> _beaconCount = new("--count") { Description = "Stop after this many beacons." };

    private readonly Option<string?> _beaconDuration = new("--duration", "-d")
    {
        Description = "Stop after this long, for example 2h.",
    };

    private readonly Option<bool> _kill = new("--kill") { Description = "Send the object as killed, to remove it." };

    private readonly Option<double?> _weatherLatitude = new("--lat")
    {
        Description = "Station latitude. With --lon, sends a complete report; without, a positionless one.",
    };

    private readonly Option<double?> _weatherLongitude = new("--lon") { Description = "Station longitude." };

    private readonly Option<string> _weatherSymbol = new("--symbol")
    {
        Description = "Weather station symbol, /_ or \\_ with an overlay. Default: /_ .",
        DefaultValueFactory = _ => "/_",
    };

    private readonly Option<int?> _windDirection = new("--wind-dir") { Description = "Wind direction, degrees." };
    private readonly Option<double?> _windSpeed = new("--wind-speed") { Description = "Sustained wind speed, mph." };
    private readonly Option<int?> _gust = new("--gust") { Description = "Peak wind in the last 5 minutes, mph." };
    private readonly Option<int?> _temperature = new("--temp") { Description = "Temperature, °F." };
    private readonly Option<double?> _rainHour = new("--rain-1h") { Description = "Rain in the last hour, inches." };
    private readonly Option<double?> _rainDay = new("--rain-24h")
    {
        Description = "Rain in the last 24 hours, inches.",
    };

    private readonly Option<double?> _rainMidnight = new("--rain-midnight")
    {
        Description = "Rain since local midnight, inches.",
    };

    private readonly Option<int?> _humidity = new("--humidity") { Description = "Relative humidity, 1 to 100 %." };
    private readonly Option<double?> _pressure = new("--pressure") { Description = "Barometric pressure, millibars." };
    private readonly Option<int?> _luminosity = new("--luminosity") { Description = "Luminosity, W/m²." };

    private readonly Option<int?> _sequence = new("--seq")
    {
        Description = "Telemetry sequence number, 0 to 999. Required.",
    };

    private readonly Option<string?> _analog = new("--analog")
    {
        Description = "One to five raw analog values, whole numbers 0 to 999, comma-separated: 199,0,255,73,123.",
    };

    private readonly Option<string?> _digital = new("--digital")
    {
        Description = "The eight digital channels as 0s and 1s: 01101001. Needs all five analog values.",
    };

    private readonly Option<string?> _names = new("--names")
    {
        Description = "Also send PARM.: channel names, comma-separated, analog then digital.",
    };

    private readonly Option<string?> _units = new("--units")
    {
        Description = "Also send UNIT.: channel units or labels, comma-separated, analog then digital.",
    };

    private readonly Option<string?> _equations = new("--equations")
    {
        Description =
            "Also send EQNS.: a,b,c per analog channel (reading = a·v² + b·v + c), channels separated by ';'.",
    };

    private readonly Option<string?> _bits = new("--bits")
    {
        Description = "Also send BITS.: the 'on' state of each digital channel as eight 0s and 1s.",
    };

    private readonly Option<string?> _project = new("--project") { Description = "Project name sent with --bits." };

    private readonly Option<double?> _snow = new("--snow")
    {
        Description = "Snowfall in the last 24 hours, inches. Needs --lat and --lon.",
    };

    public Command Build()
    {
        var command = toTnc
            ? new Command(
                "send",
                "Transmit a status report, message, position, object, weather or telemetry report on the air "
                    + "through a KISS TNC. Needs your callsign and a licence to transmit; check the path first "
                    + "with --dry-run."
            )
            : new Command(
                "send",
                "Send a status report, message, position, object, weather or telemetry report to APRS-IS. Needs "
                    + "your callsign and its passcode."
            );
        Option[] options = toTnc
            ? [_callsign, _tnc, _radioPort, _tocall, _path, _dryRun]
            : [_callsign, _passcode, _server, _region, _tls, _tocall, _path, _dryRun];
        foreach (var option in options)
        {
            command.Options.Add(option);
        }

        _region.AcceptOnlyFromAmong([
            .. AprsIsServerPreset.All.Where(p => p.Endpoint != AprsIsEndpoint.Rotate).Select(p => p.Key),
        ]);

        var statusText = new Argument<string>("text") { Description = "Status text." };
        var status = new Command("status", "Send a status report.") { statusText };
        SetAction(status, parseResult => new StatusReport { Text = parseResult.GetValue(statusText)! });

        var addressee = new Argument<string>("addressee") { Description = "Callsign to send the message to." };
        var messageText = new Argument<string>("text") { Description = "Message text, at most 67 characters." };
        var message = new Command(
            "message",
            (toTnc ? "Transmit a message through the TNC" : "Send a message")
                + " and wait for the recipient's acknowledgement, resending it until then (see --retry). Replies "
                + "are acknowledged and printed."
        )
        {
            addressee,
            messageText,
            _waitReplies,
            _retry,
            _showReceived,
        };
        if (!toTnc)
        {
            message.Options.Add(_alsoFilter); // a TNC hears everything on the air, so there is nothing to filter
        }

        _retry.AcceptOnlyFromAmong([.. RetryProfiles.Keys]);
        SetAction(
            message,
            parseResult => new MessagePayload
            {
                Addressee = parseResult.GetValue(addressee)!,
                Text = parseResult.GetValue(messageText)!,
            }
        );

        var position = new Command("position", "Send a position report.") { _latitude, _longitude, _symbol, _comment };
        SetAction(
            position,
            parseResult => new PositionReport
            {
                Position = Position(parseResult),
                Symbol = Symbol(parseResult),
                Comment = parseResult.GetValue(_comment) ?? "",
            }
        );

        if (!toTnc)
        {
            var beacon = new Command(
                "beacon",
                "Beacon a position to APRS-IS on a schedule until --count, --duration or Ctrl+C: every --every, or on "
                    + "a decay schedule with --decay. The first beacon goes at once."
            )
            {
                _latitude,
                _longitude,
                _symbol,
                _comment,
                _beaconStatus,
                _every,
                _decay,
                _beaconCount,
                _beaconDuration,
            };
            beacon.SetAction(
                async (parseResult, cancellationToken) =>
                {
                    if (!TryBeaconPlan(parseResult, out var plan, out var error))
                    {
                        return await UsageAsync(beacon.Name, error);
                    }

                    return await RunAsync(
                        parseResult,
                        beacon.Name,
                        _ =>
                        {
                            List<AprsPayload> payloads =
                            [
                                new PositionReport
                                {
                                    Position = Position(parseResult),
                                    Symbol = Symbol(parseResult),
                                    Comment = parseResult.GetValue(_comment) ?? "",
                                },
                            ];
                            if (parseResult.GetValue(_beaconStatus) is { Length: > 0 } status)
                            {
                                payloads.Add(new StatusReport { Text = status });
                            }

                            return payloads;
                        },
                        cancellationToken,
                        plan
                    );
                }
            );
            command.Subcommands.Add(beacon);
        }

        var objectName = new Argument<string>("name") { Description = "Object name, 1 to 9 characters." };
        var @object = new Command("object", "Send an object, timestamped now; --kill removes it.")
        {
            objectName,
            _latitude,
            _longitude,
            _symbol,
            _comment,
            _kill,
        };
        SetAction(
            @object,
            parseResult => new ObjectReport
            {
                Name = parseResult.GetValue(objectName)!,
                IsLive = !parseResult.GetValue(_kill),
                Timestamp = AprsTimestamp.FromDateTimeOffset(environment.TimeProvider.GetUtcNow()),
                Position = Position(parseResult),
                Symbol = Symbol(parseResult),
                Comment = parseResult.GetValue(_comment) ?? "",
            }
        );

        var weather = new Command(
            "weather",
            "Send weather readings: a complete report with --lat and --lon, otherwise a positionless one. Readings "
                + "left out are sent as unknown (wind, gust, temperature) or not at all."
        )
        {
            _weatherLatitude,
            _weatherLongitude,
            _weatherSymbol,
            _windDirection,
            _windSpeed,
            _gust,
            _temperature,
            _rainHour,
            _rainDay,
            _rainMidnight,
            _humidity,
            _pressure,
            _luminosity,
            _snow,
            _comment,
        };
        SetAction(weather, Weather);

        var telemetry = new Command(
            "telemetry",
            "Send a telemetry report (T#), after any PARM./UNIT./EQNS./BITS. definitions given, which go to your "
                + "own callsign."
        )
        {
            _sequence,
            _analog,
            _digital,
            _comment,
            _names,
            _units,
            _equations,
            _bits,
            _project,
        };
        SetAction(telemetry, Telemetry);

        command.Subcommands.Add(status);
        command.Subcommands.Add(message);
        command.Subcommands.Add(position);
        command.Subcommands.Add(@object);
        command.Subcommands.Add(weather);
        command.Subcommands.Add(telemetry);
        return command;
    }

    private void SetAction(Command command, Func<ParseResult, AprsPayload> payload) =>
        SetAction(command, (parseResult, _) => [payload(parseResult)]);

    /// <summary>
    /// Runs a command that sends one or more payloads, built once the sender's address is known (telemetry
    /// definitions are addressed to the sender). An <see cref="ArgumentException"/> from building is a usage error.
    /// </summary>
    private void SetAction(Command command, Func<ParseResult, PacketAddress, IReadOnlyList<AprsPayload>> payloads) =>
        command.SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(parseResult, command.Name, address => payloads(parseResult, address), cancellationToken)
        );

    /// <summary>The definitions given, in PARM, UNIT, EQNS, BITS order, then the report.</summary>
    private List<AprsPayload> Telemetry(ParseResult parseResult, PacketAddress address)
    {
        var station = address.Source.Value;
        var payloads = new List<AprsPayload>();
        if (parseResult.GetValue(_names) is { } names)
        {
            payloads.Add(Labels(station, TelemetryDefinitionKind.ParameterNames, names));
        }

        if (parseResult.GetValue(_units) is { } units)
        {
            payloads.Add(Labels(station, TelemetryDefinitionKind.Units, units));
        }

        if (parseResult.GetValue(_equations) is { } equations)
        {
            payloads.Add(
                new TelemetryDefinition
                {
                    Addressee = station,
                    Kind = TelemetryDefinitionKind.Equations,
                    Equations = [.. equations.Split(';', StringSplitOptions.TrimEntries).Select(Equation)],
                }
            );
        }

        if (parseResult.GetValue(_bits) is { } bits)
        {
            payloads.Add(
                new TelemetryDefinition
                {
                    Addressee = station,
                    Kind = TelemetryDefinitionKind.BitSense,
                    BitSense = bits.Trim(),
                    ProjectName = parseResult.GetValue(_project),
                }
            );
        }
        else if (parseResult.GetValue(_project) is not null)
        {
            throw new ArgumentException("--project goes with --bits.");
        }

        payloads.Add(
            new TelemetryReport
            {
                SequenceNumber = parseResult.GetValue(_sequence) ?? throw new ArgumentException("--seq is required."),
                Analog = parseResult.GetValue(_analog) is { } analog
                    ? [.. analog.Split(',', StringSplitOptions.TrimEntries).Select(Analog)]
                    : throw new ArgumentException("--analog is required: one to five values, such as 199,0,255."),
                Digital = parseResult.GetValue(_digital)?.Trim() switch
                {
                    null => [],
                    { Length: 8 } d when d.All(c => c is '0' or '1') => [.. d.Select(c => c == '1')],
                    _ => throw new ArgumentException("--digital is eight 0s and 1s, such as 01101001."),
                },
                Comment = parseResult.GetValue(_comment) ?? "",
            }
        );
        return payloads;

        static TelemetryDefinition Labels(string station, TelemetryDefinitionKind kind, string text) =>
            new()
            {
                Addressee = station,
                Kind = kind,
                Labels = text.Split(',', StringSplitOptions.TrimEntries),
            };

        static double? Analog(string value) =>
            value.Length == 0 ? null
            : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number
            : throw new ArgumentException($"--analog value '{value}' is not a number.");

        static TelemetryEquation Equation(string text)
        {
            var parts = text.Split(',', StringSplitOptions.TrimEntries);
            var numbers = parts
                .Select(p =>
                    double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : (double?)null
                )
                .ToArray();
            return numbers is [{ } a, { } b, { } c]
                ? new TelemetryEquation(a, b, c)
                : throw new ArgumentException($"--equations needs three numbers per channel, a,b,c; got '{text}'.");
        }
    }

    private AprsPayload Weather(ParseResult parseResult)
    {
        var readings = new WeatherData
        {
            WindDirection = parseResult.GetValue(_windDirection),
            WindSpeedMph = parseResult.GetValue(_windSpeed),
            WindGustMph = parseResult.GetValue(_gust),
            TemperatureF = parseResult.GetValue(_temperature),
            RainLastHourInches = parseResult.GetValue(_rainHour),
            RainLast24HoursInches = parseResult.GetValue(_rainDay),
            RainSinceMidnightInches = parseResult.GetValue(_rainMidnight),
            HumidityPercent = parseResult.GetValue(_humidity),
            PressureMillibars = parseResult.GetValue(_pressure),
            LuminosityWattsPerSquareMeter = parseResult.GetValue(_luminosity),
            SnowLast24HoursInches = parseResult.GetValue(_snow),
        };
        if (readings == new WeatherData())
        {
            throw new ArgumentException("Give at least one reading, for example --temp 68.");
        }

        var comment = parseResult.GetValue(_comment) ?? "";
        var (latitude, longitude) = (parseResult.GetValue(_weatherLatitude), parseResult.GetValue(_weatherLongitude));
        if (latitude is null && longitude is null)
        {
            return new WeatherReport
            {
                Timestamp = AprsTimestamp.FromDateTimeOffset(
                    environment.TimeProvider.GetUtcNow(),
                    AprsTimestampFormat.MonthDayHourMinute
                ),
                Weather = readings,
                Comment = comment,
            };
        }

        return new PositionReport
        {
            Position =
                latitude is { } lat && longitude is { } lon
                    ? new AprsPosition(lat, lon)
                    : throw new ArgumentException("--lat and --lon go together."),
            Symbol =
                parseResult.GetValue(_weatherSymbol) is [var table, var code]
                && AprsSymbol.TryCreate(table, code, out var symbol)
                    ? symbol
                    : throw new ArgumentException("--symbol must be a table and a code, for example /_."),
            Weather = readings,
            Comment = comment,
        };
    }

    private AprsPosition Position(ParseResult parseResult) =>
        parseResult.GetValue(_latitude) is { } latitude && parseResult.GetValue(_longitude) is { } longitude
            ? new AprsPosition(latitude, longitude)
            : throw new ArgumentException("--lat and --lon are required.");

    private AprsSymbol Symbol(ParseResult parseResult) =>
        parseResult.GetValue(_symbol) is [var table, var code] && AprsSymbol.TryCreate(table, code, out var symbol)
            ? symbol
            : throw new ArgumentException("--symbol must be a table and a code, for example /> or /-.");

    private async Task<int> RunAsync(
        ParseResult parseResult,
        string commandName,
        Func<PacketAddress, IReadOnlyList<AprsPayload>> build,
        CancellationToken cancellationToken,
        BeaconPlan? beacon = null
    )
    {
        if (parseResult.GetValue(_callsign)?.Trim().ToUpperInvariant() is not { Length: > 0 } callsign)
        {
            return await UsageAsync(commandName, "--callsign is required: packets are sent as your callsign.");
        }

        if (callsign.Split('-')[0] == ListenCommand.DefaultCallsign)
        {
            return await UsageAsync(commandName, "N0CALL is a placeholder; send as your own callsign.");
        }

        PacketAddress address;
        AprsPacket[] packets;
        Ax25Frame[] frames = [];
        MessagePayload? message;
        try
        {
            address = PacketAddress.Parse(
                callsign,
                parseResult.GetValue(_tocall)!.Trim().ToUpperInvariant(),
                (parseResult.GetValue(_path) ?? (toTnc ? DefaultRfPath : "")).Split(
                    ',',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                )
            );
            var payloads = build(address);
            packets = [.. payloads.Select(address.Create)];
            message = payloads is [MessagePayload single] ? single : null;

            // Checked before connecting, so a callsign or path AX.25 cannot carry is a usage error, even on --dry-run.
            if (toTnc)
            {
                frames = [.. packets.Select(Ax25Frame.FromAprsPacket)];
            }
        }
        catch (ArgumentException ex)
        {
            return await UsageAsync(commandName, ex.Message);
        }

        if (parseResult.GetValue(_dryRun))
        {
            if (message is not null)
            {
                // As the station sends it, with the message ID a real send would get.
                using var station = new MessagingStation(address.Source);
                station.Send(Callsign.Parse(message.Addressee), message.Text);
                await foreach (var outgoing in station.ReadOutgoingAsync(cancellationToken))
                {
                    packets = [address.Create(outgoing.Payload)];
                    break;
                }
            }

            foreach (var packet in packets)
            {
                await environment.Out.WriteLineAsync(packet.ToString());
            }

            return ExitCodes.Success;
        }

        var waitReplies = TimeSpan.Zero;
        if (message is not null)
        {
            var waitText = parseResult.GetValue(_waitReplies)!.Trim();
            if (waitText != "0" && !DurationParser.TryParse(waitText, out waitReplies))
            {
                return await UsageAsync(
                    commandName,
                    $"--wait-replies '{waitText}' is not valid. Use 0 or a duration such as 30s or 5m."
                );
            }
        }

        if (toTnc)
        {
            return await TransmitAsync(
                parseResult,
                commandName,
                address,
                packets,
                frames,
                message,
                waitReplies,
                cancellationToken
            );
        }

        if (!TryReadPasscode(parseResult, out var passcode, out var passcodeError))
        {
            return await UsageAsync(commandName, passcodeError);
        }

        if (!TryResolveEndpoint(parseResult, out var endpoint, out var serverError))
        {
            return await UsageAsync(commandName, serverError);
        }

        var logger = environment.LoggerFactory.CreateLogger("aprscli");
        var options = new AprsIsClientOptions
        {
            Endpoint = endpoint,
            UseTls = parseResult.GetValue(_tls),
            Callsign = callsign,
            Passcode = passcode,
            // Messages addressed to us, so acknowledgements reach the messenger.
            Filter = message is not null
                ? ("g/" + callsign + " " + parseResult.GetValue(_alsoFilter)?.Trim()).TrimEnd()
                : null,
            SoftwareName = "aprscli",
            TimeProvider = environment.TimeProvider,
        };
        // A message waits minutes for its acknowledgement, so a dropped connection is reconnected rather than fatal.
        await using var connection = environment.CreateConnection(options, logger);
        using var reporter = new ConnectionReporter(connection, environment.Error, endpoint, reconnect: true);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, reporter.Stopped);
        try
        {
            var login = await connection.StartAsync(cancellationToken);
            if (login.Status != LoginStatus.Verified)
            {
                await environment.Error.WriteLineAsync(
                    $"aprscli: the server did not accept the passcode for {callsign}, so it would drop the packet."
                );
                return ExitCodes.LoginFailed;
            }

            if (beacon is not null)
            {
                return await BeaconAsync(connection, packets, beacon, reporter, run.Token);
            }

            if (message is null)
            {
                // Sent in order and spread out by the connection's rate limit: definitions before the report.
                foreach (var packet in packets)
                {
                    await connection.SendAsync(packet, run.Token);
                    await environment.Out.WriteLineAsync(packet.ToString());
                }

                return ExitCodes.Success;
            }

            return await SendMessageAsync(
                (packet, token) => connection.SendAsync(packet, token),
                ParseLines(connection.ReadLinesAsync(run.Token), CancellationToken.None), // stopped by the reader
                "aprs-is",
                address,
                message,
                RetryProfiles[parseResult.GetValue(_retry)!],
                waitReplies,
                parseResult.GetValue(_showReceived),
                run.Token
            );
        }
        catch (LoginException ex)
        {
            await environment.Error.WriteLineAsync($"aprscli: login failed: {ex.Message}");
            return ExitCodes.LoginFailed;
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or TimeoutException)
        {
            await environment.Error.WriteLineAsync($"aprscli: connection failed: {ex.Message}");
            return ExitCodes.ConnectionFailed;
        }
        catch (OperationCanceledException) when (reporter.Stopped.IsCancellationRequested)
        {
            return reporter.ExitCode!.Value; // the reporter has said why
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await environment.Error.WriteLineAsync("aprscli: cancelled.");
            return ExitCodes.Success;
        }
    }

    /// <summary>
    /// Sends each frame through the TNC, in order, waiting for the transmit limit between them; or holds a message
    /// conversation through it, as on APRS-IS.
    /// </summary>
    private async Task<int> TransmitAsync(
        ParseResult parseResult,
        string commandName,
        PacketAddress source,
        AprsPacket[] packets,
        Ax25Frame[] frames,
        MessagePayload? message,
        TimeSpan waitReplies,
        CancellationToken cancellationToken
    )
    {
        if (parseResult.GetValue(_tnc) is not { } tncText)
        {
            return await UsageAsync(
                commandName,
                $"--tnc is required: the TNC to transmit through, as {TncAddress.Forms}."
            );
        }

        if (!TncAddress.TryParse(tncText, out var address))
        {
            return await UsageAsync(
                commandName,
                $"--tnc '{tncText.Trim()}' is not valid. Use {TncAddress.Forms}, such as {TncAddress.Examples}."
            );
        }

        if (address.TextMode)
        {
            return await UsageAsync(
                commandName,
                "A TNC read as text (,text: a radio in APRS mode) takes nothing to transmit. Switch it to KISS mode, "
                    + "with ,setup= if it has one."
            );
        }

        var radioPort = parseResult.GetValue(_radioPort);
        if (radioPort is < 0 or > 15)
        {
            return await UsageAsync(commandName, "--radio-port must be 0 to 15.");
        }

        var limit = environment.CreateTransmitLimit();
        TncLink? client = null;
        try
        {
            client = await TncLink.OpenAsync(address, radioPort, limit, environment.OpenTncPort, cancellationToken);
            if (message is not null)
            {
                var link = client;
                link.Warning += (_, e) => TncCommand.ReportWarning(address.ToString(), e, environment.Error);
                return await SendMessageAsync(
                    (packet, token) => link.SendFrameAsync(Ax25Frame.FromAprsPacket(packet), token),
                    link.ReadPacketsAsync(cancellationToken),
                    "rf",
                    source,
                    message,
                    RetryProfiles[parseResult.GetValue(_retry)!],
                    waitReplies,
                    parseResult.GetValue(_showReceived),
                    cancellationToken
                );
            }

            for (var i = 0; i < frames.Length; i++)
            {
                if (i == limit.Burst && limit.IsOn)
                {
                    await environment.Error.WriteLineAsync(
                        $"aprscli: sending the rest one every {DurationParser.Format(limit.Interval)}, the transmit "
                            + "limit."
                    );
                }

                await client.SendFrameAsync(frames[i], cancellationToken);
                await environment.Out.WriteLineAsync(packets[i].ToString());
            }
        }
        catch (Exception ex) when (TncLink.IsLinkFailure(ex))
        {
            await environment.Error.WriteLineAsync(
                $"aprscli: could not send through the TNC at {address}: {ex.Message}"
            );
            return ExitCodes.ConnectionFailed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await environment.Error.WriteLineAsync("aprscli: cancelled.");
            return ExitCodes.Success;
        }
        finally
        {
            if (client is not null)
            {
                await client.DisposeAsync();
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>The <c>--retry</c> choices.</summary>
    private static readonly Dictionary<string, RetryProfile> RetryProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = RetryProfile.Default,
        ["aprs-is"] = RetryProfile.AprsIs,
        ["slow-hf"] = RetryProfile.SlowHf,
        ["none"] = RetryProfile.None,
    };

    /// <summary>
    /// Sends <paramref name="message"/> through a <see cref="MessagingStation"/> and waits until it is acknowledged,
    /// rejected or has run out of retries; then listens for <paramref name="waitReplies"/> more.
    /// </summary>
    /// <param name="send">Sends a packet: to APRS-IS, or through the TNC.</param>
    /// <param name="received">Every packet received, for the whole run, so acks and replies reach the station.</param>
    /// <param name="source">Where <paramref name="received"/> comes from, for the station.</param>
    private async Task<int> SendMessageAsync(
        Func<AprsPacket, CancellationToken, ValueTask> send,
        IAsyncEnumerable<AprsPacket> received,
        string source,
        PacketAddress address,
        MessagePayload message,
        RetryProfile retryProfile,
        TimeSpan waitReplies,
        bool showReceived,
        CancellationToken cancellationToken
    )
    {
        // The reader (replies, --show-received) and the sender (each packet) print at the same time.
        var output = TextWriter.Synchronized(environment.Out);

        // A one-off run: a failed message isn't kept for its station being heard again.
        using var station = new MessagingStation(
            address.Source,
            new MessagingStationOptions
            {
                RetryProfile = retryProfile,
                RetryOnHeardWindow = TimeSpan.Zero,
                TimeProvider = environment.TimeProvider,
            }
        );
        station.MessageReceived += (_, e) => output.WriteLine($"Reply from {e.Message.From}: {e.Message.Text}");

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Everything the station hands out is sent and printed: the message, its retries, and acks. It runs until the
        // station is disposed, which ends it once what is queued has gone, so the last ack isn't lost.
        var sending = Task.Run(
            async () =>
            {
                await foreach (var outgoing in station.ReadOutgoingAsync(cancellationToken))
                {
                    var packet = address.Create(outgoing.Payload);
                    await send(packet, cancellationToken);
                    await output.WriteLineAsync(packet.ToString());
                }
            },
            CancellationToken.None
        );

        // Reads for the whole run, so replies are acknowledged while the message is pending and afterwards.
        var reading = Task.Run(
            async () =>
            {
                await foreach (var packet in received.WithCancellation(stop.Token))
                {
                    if (showReceived)
                    {
                        await output.WriteLineAsync("< " + packet);
                    }

                    station.Receive(packet, source);
                }
            },
            CancellationToken.None
        );

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = station.Send(Callsign.Parse(message.Addressee), message.Text);
        sent.StateChanged += (_, _) =>
        {
            if (sent.IsFinished)
            {
                finished.TrySetResult();
            }
        };
        if (sent.IsFinished)
        {
            finished.TrySetResult(); // answered before the handler was attached
        }

        try
        {
            // A connection that gives up ends the reader or the sender with an error, which ends the wait too.
            await Task.WhenAny(finished.Task, reading, sending).Unwrap().WaitAsync(cancellationToken);
            if (!sent.IsFinished)
            {
                await Task.WhenAll(reading, sending); // rethrows why the connection ended
            }
        }
        finally
        {
            if (!sent.IsFinished)
            {
                await stop.CancelAsync();
            }
        }

        var attempts = sent.Attempts == 1 ? "1 attempt" : $"{sent.Attempts} attempts";
        int exitCode;
        switch (sent.State)
        {
            case OutgoingMessageState.Delivered:
                await output.WriteLineAsync($"Acknowledged by {message.Addressee} after {attempts}.");
                exitCode = ExitCodes.Success;
                break;
            case OutgoingMessageState.Rejected:
                await environment.Error.WriteLineAsync($"aprscli: {message.Addressee} rejected the message.");
                exitCode = ExitCodes.NotDelivered;
                break;
            default:
                await environment.Error.WriteLineAsync($"aprscli: no acknowledgement after {attempts}.");
                exitCode = ExitCodes.NotDelivered;
                break;
        }

        if (waitReplies > TimeSpan.Zero && !reading.IsCompleted)
        {
            await environment.Error.WriteLineAsync(
                $"Waiting {DurationParser.Format(waitReplies)} for replies (Ctrl+C to stop)."
            );
            stop.CancelAfter(waitReplies);
        }
        else
        {
            await stop.CancelAsync();
        }

        try
        {
            await reading;
        }
        catch (OperationCanceledException) { } // the wait ended, or Ctrl+C: the message's outcome stands

        station.Dispose(); // the sender sends what is still queued, such as the ack for a last reply, then ends
        try
        {
            await sending;
        }
        catch (OperationCanceledException) { } // Ctrl+C

        return exitCode;
    }

    private static async IAsyncEnumerable<AprsPacket> ParseLines(
        IAsyncEnumerable<string> lines,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var line in lines.WithCancellation(cancellationToken))
        {
            yield return AprsPacket.Parse(line);
        }
    }

    /// <summary>When <c>send beacon</c> sends, and when it stops.</summary>
    private sealed record BeaconPlan(Func<TimeProvider, BeaconSchedule> Schedule, int? Count, TimeSpan? Duration);

    private bool TryBeaconPlan(ParseResult parseResult, out BeaconPlan plan, out string error)
    {
        plan = null!;
        var everyText = parseResult.GetValue(_every)!;
        if (!DurationParser.TryParse(everyText, out var every) || every < TimeSpan.FromMinutes(1))
        {
            error = $"--every '{everyText}' is not valid. Use 1m or more, for example 30m.";
            return false;
        }

        Func<TimeProvider, BeaconSchedule> schedule = clock => new FixedBeaconSchedule(every, TimeSpan.Zero, clock);
        if (parseResult.GetValue(_decay) is { } decayText)
        {
            if (!DurationParser.TryParse(decayText, out var first) || first < TimeSpan.FromMinutes(1) || first > every)
            {
                error = $"--decay '{decayText}' is not valid. Use 1m or more, up to --every ({everyText}).";
                return false;
            }

            schedule = clock => new DecayBeaconSchedule(first, every, clock);
        }

        if (parseResult.GetValue(_beaconCount) is <= 0)
        {
            error = "--count must be at least 1.";
            return false;
        }

        TimeSpan? duration = null;
        if (parseResult.GetValue(_beaconDuration) is { } durationText)
        {
            if (!DurationParser.TryParse(durationText, out var parsed))
            {
                error = $"--duration '{durationText}' is not valid. Use a duration such as 2h.";
                return false;
            }

            duration = parsed;
        }

        plan = new BeaconPlan(schedule, parseResult.GetValue(_beaconCount), duration);
        error = "";
        return true;
    }

    /// <summary>
    /// Sends <paramref name="packets"/> each time the schedule comes due, printing each with the time it went, until
    /// the plan's count or duration, or cancellation. A dropped connection is reconnected meanwhile.
    /// </summary>
    private async Task<int> BeaconAsync(
        AprsIsConnection connection,
        AprsPacket[] packets,
        BeaconPlan plan,
        ConnectionReporter reporter,
        CancellationToken cancellationToken
    )
    {
        var clock = environment.TimeProvider;
        var schedule = plan.Schedule(clock);
        using var limit = plan.Duration is { } duration
            ? new CancellationTokenSource(duration, clock)
            : new CancellationTokenSource();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
        var sent = 0;
        try
        {
            while (plan.Count is not { } count || sent < count)
            {
                await Task.Delay(schedule.DueIn, clock, stop.Token);
                foreach (var packet in packets)
                {
                    await connection.SendAsync(packet, stop.Token);
                    var now = clock.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                    await environment.Out.WriteLineAsync($"{now} {packet}");
                }

                schedule.Sent();
                sent++;
            }
        }
        catch (OperationCanceledException)
            when (limit.IsCancellationRequested && !reporter.Stopped.IsCancellationRequested)
        {
            // --duration reached.
        }

        await environment.Error.WriteLineAsync($"aprscli: sent {sent} beacon{(sent == 1 ? "" : "s")}.");
        return ExitCodes.Success;
    }

    private bool TryReadPasscode(ParseResult parseResult, out int passcode, out string error)
    {
        error = "";
        if (parseResult.GetValue(_passcode) is { } explicitPasscode)
        {
            passcode = explicitPasscode;
        }
        else if (
            environment.GetEnvironmentVariable(_passcodeVariable) is not { Length: > 0 } fromEnvironment
            || !int.TryParse(fromEnvironment, NumberStyles.None, CultureInfo.InvariantCulture, out passcode)
        )
        {
            passcode = -1;
        }

        if (passcode is < 0 or > 0x7FFF)
        {
            error =
                $"sending needs a verified login: set {_passcodeVariable} to the passcode for your callsign "
                + "(aprscli passcode <callsign>).";
            return false;
        }

        return true;
    }

    private bool TryResolveEndpoint(ParseResult parseResult, out AprsIsEndpoint endpoint, out string error)
    {
        error = "";
        var server = parseResult.GetValue(_server);
        var region = parseResult.GetValue(_region);
        if (server is not null && region is not null)
        {
            endpoint = null!;
            error = "--server and --region cannot be used together.";
            return false;
        }

        var tls = parseResult.GetValue(_tls);
        if (tls && region is not null)
        {
            endpoint = null!;
            error = $"--tls cannot be combined with --region; it uses {ListenCommand.TlsDefault} or your --server.";
            return false;
        }

        if (server is null)
        {
            endpoint =
                tls ? ListenCommand.TlsDefault
                : AprsIsServerPreset.TryFind(region, out var preset) ? preset.Endpoint
                : AprsIsEndpoint.Rotate;
            return true;
        }

        try
        {
            endpoint = AprsIsEndpoint.Parse(server);
            if (tls && !ListenCommand.HasExplicitPort(server))
            {
                endpoint = endpoint.WithPort(AprsIsPorts.Tls);
            }

            return true;
        }
        catch (AprsValidationException ex)
        {
            endpoint = null!;
            error = $"--server '{server}' is not valid: {ex.Message}";
            return false;
        }
    }

    private async Task<int> UsageAsync(string commandName, string message)
    {
        await environment.Error.WriteLineAsync($"aprscli: {message}");
        await environment.Error.WriteLineAsync($"Try 'aprscli {(toTnc ? "tnc " : "")}send {commandName} --help'.");
        return ExitCodes.Usage;
    }
}
