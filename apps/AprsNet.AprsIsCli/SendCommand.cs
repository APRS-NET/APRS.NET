using System.CommandLine;
using System.Globalization;
using AprsNet.AprsIsTcpClient;
using AprsNet.Packets;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The <c>send</c> command: build a status report, message, position or object and send it to APRS-IS on a verified
/// login. <c>--dry-run</c> prints the packet without connecting.
/// </summary>
internal sealed class SendCommand(CliEnvironment environment)
{
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
        Description = "Digipeater path, comma-separated, for example WIDE1-1. Default: none (APRS-IS only).",
        Recursive = true,
    };

    private readonly Option<bool> _dryRun = new("--dry-run", "-n")
    {
        Description = "Print the packet that would be sent, without connecting.",
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

    private readonly Option<bool> _kill = new("--kill") { Description = "Send the object as killed, to remove it." };

    public Command Build()
    {
        var command = new Command(
            "send",
            "Send a status report, message, position or object to APRS-IS. Needs your callsign and its passcode."
        );
        foreach (var option in new Option[] { _callsign, _passcode, _server, _region, _tocall, _path, _dryRun })
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
            "Send a message and wait for the recipient's acknowledgement, resending at 30 s, 1, 2, 4 and 8 minutes."
        )
        {
            addressee,
            messageText,
            _waitReplies,
        };
        message.SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    parseResult,
                    "message",
                    new MessagePayload
                    {
                        Addressee = parseResult.GetValue(addressee)!,
                        Text = parseResult.GetValue(messageText)!,
                    },
                    cancellationToken
                )
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

        command.Subcommands.Add(status);
        command.Subcommands.Add(message);
        command.Subcommands.Add(position);
        command.Subcommands.Add(@object);
        return command;
    }

    private void SetAction(Command command, Func<ParseResult, AprsPayload> payload) =>
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                AprsPayload built;
                try
                {
                    built = payload(parseResult);
                }
                catch (ArgumentException ex)
                {
                    return await UsageAsync(command.Name, ex.Message);
                }

                return await RunAsync(parseResult, command.Name, built, cancellationToken);
            }
        );

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
        AprsPayload payload,
        CancellationToken cancellationToken
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
        AprsPacket packet;
        try
        {
            address = PacketAddress.Parse(
                callsign,
                parseResult.GetValue(_tocall)!.Trim().ToUpperInvariant(),
                parseResult.GetValue(_path) is { } path
                    ? path.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    : []
            );
            packet = address.Create(payload);
        }
        catch (ArgumentException ex)
        {
            return await UsageAsync(commandName, ex.Message);
        }

        if (parseResult.GetValue(_dryRun))
        {
            await environment.Out.WriteLineAsync(packet.ToString());
            return ExitCodes.Success;
        }

        var waitReplies = TimeSpan.Zero;
        if (payload is MessagePayload)
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
            Callsign = callsign,
            Passcode = passcode,
            // Messages addressed to us, so acknowledgements reach the messenger.
            Filter = payload is MessagePayload ? "g/" + callsign : null,
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

            if (payload is not MessagePayload message)
            {
                await connection.SendAsync(packet, run.Token);
                await environment.Out.WriteLineAsync(packet.ToString());
                return ExitCodes.Success;
            }

            return await SendMessageAsync(connection, address, message, waitReplies, run.Token);
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

    private async Task<int> SendMessageAsync(
        AprsIsConnection connection,
        PacketAddress address,
        MessagePayload message,
        TimeSpan waitReplies,
        CancellationToken cancellationToken
    )
    {
        using var messenger = new AprsMessenger(
            address,
            async (packet, token) =>
            {
                await connection.SendAsync(packet, token);
                await environment.Out.WriteLineAsync(packet.ToString());
            },
            environment.TimeProvider
        );

        // Reads for the whole run, so replies are acknowledged both while the message is pending and afterwards.
        using var stopReading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reading = Task.Run(
            async () =>
            {
                await foreach (var line in connection.ReadLinesAsync(stopReading.Token))
                {
                    var packet = AprsPacket.Parse(line);
                    if (await messenger.HandleAsync(packet, stopReading.Token) is { } reply)
                    {
                        await environment.Out.WriteLineAsync($"Reply from {packet.Source}: {reply.Text}");
                    }
                }
            },
            CancellationToken.None
        );

        var delivery = await messenger.SendAsync(message.Addressee, message.Text, cancellationToken);
        var attempts = delivery.Attempts == 1 ? "1 attempt" : $"{delivery.Attempts} attempts";
        int exitCode;
        switch (delivery.Status)
        {
            case MessageDeliveryStatus.Acknowledged:
                await environment.Out.WriteLineAsync($"Acknowledged by {message.Addressee} after {attempts}.");
                exitCode = ExitCodes.Success;
                break;
            case MessageDeliveryStatus.Rejected:
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
            stopReading.CancelAfter(waitReplies);
        }
        else
        {
            await stopReading.CancelAsync();
        }

        try
        {
            await reading;
        }
        catch (OperationCanceledException) { } // the wait ended, or Ctrl+C: the message's outcome stands

        return exitCode;
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

        if (server is null)
        {
            endpoint = AprsIsServerPreset.TryFind(region, out var preset) ? preset.Endpoint : AprsIsEndpoint.Rotate;
            return true;
        }

        try
        {
            endpoint = AprsIsEndpoint.Parse(server);
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
        await environment.Error.WriteLineAsync($"Try 'aprscli send {commandName} --help'.");
        return ExitCodes.Usage;
    }
}
