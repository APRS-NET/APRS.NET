using System.CommandLine;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using AprsNet.AprsIsTcpClient;
using AprsNet.Filters;
using AprsNet.Gateway;
using AprsNet.IGate;
using AprsNet.Kiss;
using AprsNet.Packets;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The <c>serve</c> command: read packets from APRS-IS, KISS TNCs or both, and serve them to local APRS-IS clients,
/// each with its own filter. A packet heard on several sources is served once.
/// </summary>
internal sealed class ServeCommand(CliEnvironment environment)
{
    private const string _defaultListen = "127.0.0.1:14580";
    private const string _defaultName = "APRSNET";
    private const string _passcodeVariable = "APRS_PASSCODE";
    private const string _defaultIGateSymbol = "R&";
    private const string _defaultBeaconInterval = "30m";
    private const string _receiveOnly = "RX-only IGate";

    private readonly Option<string?> _callsign = new("--callsign", "-c")
    {
        Description =
            "Callsign for the upstream login, with optional SSID: read-only, or the IGate's own with --igate. "
            + "Default: a listener ID such as SWL482913.",
    };

    private readonly Option<bool> _igate = new("--igate")
    {
        Description =
            "Also gate what the --tnc sources hear to APRS-IS as a receive-only IGate, and beacon its position "
            + $"there. Needs --callsign, its passcode in {_passcodeVariable}, --lat and --lon. Receive-only IGates "
            + "break messaging for stations nearby: see the gateway guide.",
    };

    private readonly Option<int?> _passcode = new("--passcode")
    {
        Description =
            $"APRS-IS passcode for the callsign, for --igate. Prefer the {_passcodeVariable} environment variable; an "
            + "option value is visible in the process list and your shell history. Never printed or logged.",
    };

    private readonly Option<double?> _latitude = new("--lat")
    {
        Description = "The IGate's latitude for its beacon, in degrees, north positive.",
    };

    private readonly Option<double?> _longitude = new("--lon")
    {
        Description = "The IGate's longitude for its beacon, in degrees, east positive.",
    };

    private readonly Option<string> _symbol = new("--symbol")
    {
        Description = $"The IGate's symbol, as table and code. Default: {_defaultIGateSymbol} (receive-only IGate).",
        DefaultValueFactory = _ => _defaultIGateSymbol,
    };

    private readonly Option<string?> _comment = new("--comment")
    {
        Description = $"Text for the beacon's comment, after \"{_receiveOnly}\".",
    };

    private readonly Option<string> _beaconInterval = new("--beacon-interval")
    {
        Description = $"How often the IGate beacons its position to APRS-IS. Default: {_defaultBeaconInterval}.",
        DefaultValueFactory = _ => _defaultBeaconInterval,
    };

    private readonly Option<string?> _server = new("--server", "-s")
    {
        Description = "Upstream APRS-IS server as host[:port]. Default: rotate.aprs2.net:14580.",
    };

    private readonly Option<string?> _filter = new("--filter", "-f")
    {
        Description =
            "Upstream APRS-IS filter: everything local clients can ask for, for example \"r/35/-97/300\". Each "
            + "client then narrows it with its own filter. Without it, only the --tnc sources are served.",
    };

    private readonly Option<string[]> _tnc = new("--tnc", "-t")
    {
        Description =
            $"Also serve what a KISS TNC hears, as host[:port] (default port {KissTcpClient.DefaultPort}, Dire "
            + "Wolf's) or serial:PORT[@BAUD] for a TNC on a serial port. Repeat for several TNCs. A lost or "
            + "unplugged TNC is reconnected.",
        Arity = ArgumentArity.ZeroOrMore,
    };

    private readonly Option<string> _listen = new("--listen", "-l")
    {
        Description =
            $"Address and port to serve on. Default: {_defaultListen} (this computer only); use 0.0.0.0:14580 to "
            + "serve your network.",
        DefaultValueFactory = _ => _defaultListen,
    };

    private readonly Option<string?> _http = new("--http")
    {
        Description =
            "Also serve HTTP on this address:port, for example 127.0.0.1:14590: WebSocket clients at "
            + "ws://address:port/aprs get JSON packets and set their filter in the URL or a message, and /health "
            + "says whether each source is connected (503 when none is). Default: off.",
    };

    private readonly Option<string> _name = new("--name")
    {
        Description = $"Server name sent to clients: 1 to 9 letters, digits or '-'. Default: {_defaultName}.",
        DefaultValueFactory = _ => _defaultName,
    };

    private readonly Option<string?> _history = new("--history")
    {
        Description = "Send each client the packets from this long before it connected, for example 10m. Default: off.",
    };

    private readonly Option<string?> _duration = new("--duration", "-d")
    {
        Description = "Stop after this long, for example 2h. Default: until Ctrl+C.",
    };

    private readonly Option<string?> _quietWarning = new("--quiet-warning")
    {
        Description =
            "Warn when a --tnc sends nothing at all for this long, for example 10m: it may be switched off behind a "
            + "port that stays open. Pick longer than the channel is ever quiet. /health shows it as quiet. Default: off.",
    };

    public Command Build()
    {
        var command = new Command(
            "serve",
            "Serve an APRS-IS feed, KISS TNCs or both to local APRS-IS clients, each with its own filter. A packet "
                + "heard on several sources is served once. Clients are read-only."
        );
        foreach (
            var option in new Option[]
            {
                _callsign,
                _server,
                _filter,
                _tnc,
                _listen,
                _http,
                _name,
                _history,
                _duration,
                _quietWarning,
                _igate,
                _passcode,
                _latitude,
                _longitude,
                _symbol,
                _comment,
                _beaconInterval,
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
                    await environment.Error.WriteLineAsync($"aprscli: {error}");
                    await environment.Error.WriteLineAsync("Try 'aprscli serve --help'.");
                    return ExitCodes.Usage;
                }

                return await RunAsync(options, cancellationToken);
            }
        );
        return command;
    }

    private sealed record ServeOptions(
        AprsIsClientOptions? Upstream,
        IReadOnlyList<TncAddress> Tncs,
        AprsIsServerOptions Local,
        IPEndPoint? Http,
        TimeSpan? Duration,
        TimeSpan? QuietWarning,
        IGateOptions? IGate
    );

    /// <summary>What <c>--igate</c> gates as and beacons.</summary>
    private sealed record IGateOptions(Callsign Callsign, AprsPacket Beacon, TimeSpan BeaconInterval);

    private bool TryBind(ParseResult parseResult, out ServeOptions options, out string error)
    {
        options = null!;
        var listenOnly = parseResult.GetValue(_callsign) is null;
        var callsign = (parseResult.GetValue(_callsign) ?? environment.NewListenerId()).Trim().ToUpperInvariant();
        var endpoint = AprsIsEndpoint.Rotate;
        if (parseResult.GetValue(_server) is { } server && !AprsIsEndpoint.TryParse(server, out endpoint!))
        {
            error = $"--server '{server}' is not valid. Use host or host:port.";
            return false;
        }

        var tncs = new List<TncAddress>();
        foreach (var text in parseResult.GetValue(_tnc) ?? [])
        {
            if (!TncAddress.TryParse(text, out var address))
            {
                error = $"--tnc '{text}' is not valid. Use {TncAddress.Forms}, such as {TncAddress.Examples}.";
                return false;
            }

            if (tncs.Any(t => t.ToString() == address.ToString()))
            {
                error = $"--tnc '{text}' is given twice.";
                return false;
            }

            tncs.Add(address);
        }

        AprsFilter? parsedFilter = null;
        if (parseResult.GetValue(_filter) is { } filter)
        {
            if (!AprsFilter.TryParse(filter, out parsedFilter, out var filterError) || parsedFilter.IsEmpty)
            {
                error = $"--filter: {filterError ?? "an upstream filter is required."}";
                return false;
            }
        }
        else if (tncs.Count == 0)
        {
            error = "nothing to serve: give --filter for an APRS-IS feed, --tnc for a TNC, or both.";
            return false;
        }

        var igate = parseResult.GetValue(_igate);
        var passcode = -1;
        TimeSpan beaconInterval = default;
        AprsPosition? position = null;
        AprsSymbol? symbol = null;
        if (igate)
        {
            if (
                !TryBindIGate(
                    parseResult,
                    listenOnly,
                    tncs.Count,
                    out passcode,
                    out beaconInterval,
                    out var place,
                    out error
                )
            )
            {
                return false;
            }

            (position, symbol) = (place.Position, place.Symbol);
        }
        else if (
            new Option[] { _passcode, _latitude, _longitude, _symbol, _comment, _beaconInterval }.FirstOrDefault(
                option => parseResult.GetResult(option) is { Implicit: false }
            ) is
            { } stray
        )
        {
            error = $"{stray.Name} only applies with --igate.";
            return false;
        }

        var listenText = parseResult.GetValue(_listen)!;
        if (
            !IPEndPoint.TryParse(listenText, out var listen)
            || (listen.Port == 0 && !listenText.EndsWith(":0", StringComparison.Ordinal))
        )
        {
            error = $"--listen '{listenText}' is not valid. Use address:port, for example {_defaultListen}.";
            return false;
        }

        IPEndPoint? http = null;
        if (parseResult.GetValue(_http) is { } httpText && !IPEndPoint.TryParse(httpText, out http))
        {
            error = $"--http '{httpText}' is not valid. Use address:port, for example 127.0.0.1:14590.";
            return false;
        }

        var history = TimeSpan.Zero;
        if (parseResult.GetValue(_history) is { } historyText && !DurationParser.TryParse(historyText, out history))
        {
            error = $"--history '{historyText}' is not valid. Use a number and a unit: 90s, 10m or 2h.";
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

        TimeSpan? quietWarning = null;
        if (parseResult.GetValue(_quietWarning) is { } quietText)
        {
            if (!DurationParser.TryParse(quietText, out var quiet) || quiet <= TimeSpan.Zero)
            {
                error = $"--quiet-warning '{quietText}' is not valid. Use a number and a unit: 90s, 10m or 2h.";
                return false;
            }

            quietWarning = quiet;
        }

        var upstream =
            parsedFilter is null && !igate
                ? null
                : new AprsIsClientOptions
                {
                    Endpoint = endpoint,
                    Callsign = callsign,
                    ListenOnly = listenOnly,
                    Passcode = passcode,
                    Filter = parsedFilter?.ToString(),
                    SoftwareName = "aprscli",
                    TimeProvider = environment.TimeProvider,
                    ReconnectDelay = environment.ReconnectDelay,
                };
        if (igate)
        {
            // A busy channel brings several packets a minute, far more than one station's own sends.
            upstream!.SendBurst = 20;
            upstream.SendInterval = TimeSpan.FromMilliseconds(200);
        }

        var local = new AprsIsServerOptions
        {
            ServerName = parseResult.GetValue(_name)!,
            EndPoint = listen,
            History = history,
            SoftwareName = "aprscli",
            TimeProvider = environment.TimeProvider,
        };
        IGateOptions? igateOptions = null;
        try
        {
            upstream?.Validate();
            local.Validate();
            if (igate)
            {
                var igateCall = Callsign.Parse(callsign);
                var comment = parseResult.GetValue(_comment) is { Length: > 0 } extra
                    ? $"{_receiveOnly} {extra}"
                    : _receiveOnly;
                var beacon = AprsPacket.Create(
                    igateCall,
                    Callsign.Parse(SendCommand.DefaultTocall),
                    [],
                    new PositionReport
                    {
                        Position = position!.Value,
                        Symbol = symbol!.Value,
                        Comment = comment,
                    }
                );
                igateOptions = new IGateOptions(igateCall, beacon, beaconInterval);
            }
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        options = new ServeOptions(upstream, tncs, local, http, duration, quietWarning, igateOptions);
        error = "";
        return true;
    }

    private bool TryBindIGate(
        ParseResult parseResult,
        bool listenOnly,
        int tncCount,
        out int passcode,
        out TimeSpan beaconInterval,
        out (AprsPosition Position, AprsSymbol Symbol) place,
        out string error
    )
    {
        passcode = -1;
        beaconInterval = default;
        place = default;
        if (listenOnly)
        {
            error = "--igate needs --callsign: the IGate's callsign, which it logs in and gates as.";
            return false;
        }

        if (tncCount == 0)
        {
            error = "--igate needs a --tnc to gate from.";
            return false;
        }

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
                $"--igate needs a verified login: set {_passcodeVariable} to the passcode for your callsign "
                + "(aprscli passcode <callsign>).";
            return false;
        }

        if (
            parseResult.GetValue(_latitude) is not { } latitude
            || parseResult.GetValue(_longitude) is not { } longitude
        )
        {
            error = "--igate needs --lat and --lon: the IGate's position, which it beacons to APRS-IS.";
            return false;
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            error = "--lat must be -90 to 90 and --lon -180 to 180.";
            return false;
        }

        if (
            parseResult.GetValue(_symbol) is not [var table, var code]
            || !AprsSymbol.TryCreate(table, code, out var symbol)
        )
        {
            error = $"--symbol must be a table and a code, for example {_defaultIGateSymbol} or I&.";
            return false;
        }

        var intervalText = parseResult.GetValue(_beaconInterval)!;
        if (!DurationParser.TryParse(intervalText, out beaconInterval) || beaconInterval < TimeSpan.FromMinutes(1))
        {
            error = $"--beacon-interval '{intervalText}' is not valid. Use 1m or more, for example 30m.";
            return false;
        }

        place = (new AprsPosition(latitude, longitude), symbol);
        error = "";
        return true;
    }

    private async Task<int> RunAsync(ServeOptions options, CancellationToken cancellationToken)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Duration is { } duration)
        {
            run.CancelAfter(duration);
        }

        await using var server = new AprsIsServer(options.Local);
        try
        {
            server.Start();
        }
        catch (SocketException ex)
        {
            await environment.Error.WriteLineAsync($"aprscli: cannot serve on {options.Local.EndPoint}: {ex.Message}");
            return ExitCodes.Error;
        }

        await using var feed = new GatewayFeed(server, timeProvider: environment.TimeProvider);
        var health = new GatewayHealth(feed, environment.TimeProvider);
        await using var hub = options.Http is null ? null : new AprsWebSocketHub(server);
        await using var web = hub is null
            ? null
            : new GatewayWebServer(hub, new GatewayWebServerOptions { EndPoint = options.Http!, Health = health });
        try
        {
            web?.Start();
        }
        catch (SocketException ex)
        {
            await environment.Error.WriteLineAsync($"aprscli: cannot serve on {options.Http}: {ex.Message}");
            return ExitCodes.Error;
        }

        var logger = environment.LoggerFactory.CreateLogger("aprscli");
        await using var connection = options.Upstream is null
            ? null
            : environment.CreateConnection(options.Upstream, logger);
        using var reporter = connection is null
            ? null
            : new ConnectionReporter(connection, environment.Error, options.Upstream!.Endpoint, true);
        if (connection is not null)
        {
            try
            {
                var login = await connection.StartAsync(run.Token);
                if (options.IGate is not null && login.Status != LoginStatus.Verified)
                {
                    await environment.Error.WriteLineAsync(
                        $"aprscli: the server did not accept the passcode for {options.Upstream!.Callsign}, so it "
                            + "would drop what the IGate gates. Check APRS_PASSCODE (aprscli passcode <callsign>)."
                    );
                    return ExitCodes.LoginFailed;
                }
            }
            catch (OperationCanceledException) when (run.IsCancellationRequested)
            {
                return ExitCodes.Success;
            }
            catch (KissTncLoginException)
            {
                await environment.Error.WriteLineAsync(ConnectionReporter.KissTnc(options.Upstream!.Endpoint));
                return ExitCodes.LoginFailed;
            }
            catch (LoginException ex)
            {
                await environment.Error.WriteLineAsync(
                    $"aprscli: login failed on {options.Upstream!.Endpoint}: {ex.Message}"
                );
                return ExitCodes.LoginFailed;
            }
            catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
            {
                await environment.Error.WriteLineAsync(
                    $"aprscli: could not connect to {options.Upstream!.Endpoint}: {ex.Message}"
                );
                return ExitCodes.ConnectionFailed;
            }
        }

        var sources = new List<Task>();
        var described = new List<string>();
        if (connection is not null)
        {
            // Without a filter (an IGate that only uploads) the server sends little, but its lines must still be read.
            sources.Add(feed.AddSource(AprsIsSource, PacketsAsync(connection.ReadLinesAsync(run.Token))));
            health.AddSource(AprsIsSource, () => connection.State == AprsIsConnectionState.Connected);
            if (options.Upstream!.Filter is { } upstreamFilter)
            {
                described.Add($"{options.Upstream.Endpoint} ({upstreamFilter})");
            }
        }

        var uplink = options.IGate is null
            ? null
            : new IGateUplink(
                connection!,
                new RfToInternetGate(
                    options.IGate.Callsign,
                    new RfToInternetGateOptions { TimeProvider = environment.TimeProvider }
                ),
                options.IGate.Beacon,
                options.IGate.BeaconInterval,
                environment.TimeProvider,
                logger
            );
        uplink?.AddTo(health);
        var uploading = uplink?.RunAsync(run.Token) ?? Task.CompletedTask;

        var tncs = new List<KissConnection>();
        try
        {
            foreach (var address in options.Tncs)
            {
                var tnc = environment.CreateTncConnection(address, quietWarning: options.QuietWarning);
                tncs.Add(tnc);
                var name = address.ToString();
                TncCommand.ReportConnection(tnc, name, environment.Error);
                var heard = tnc.ReadPacketsAsync(run.Token);
                sources.Add(feed.AddSource(name, uplink is null ? heard : uplink.TapAsync(heard)));
                health.AddSource(name, () => tnc.IsConnected, () => tnc.IsQuiet, () => tnc.IsSendingText);
                described.Add($"the TNC at {name}");
            }

            await environment.Error.WriteLineAsync(
                $"aprscli: serving {string.Join(" and ", described)} on {server.LocalEndPoint} as "
                    + $"{options.Local.ServerName}"
                    + (
                        web is null
                            ? ""
                            : $", and on http://{web.LocalEndPoint} (WebSocket clients at /aprs, health at /health)"
                    )
            );
            if (options.IGate is { } igate)
            {
                await environment.Error.WriteLineAsync(
                    $"aprscli: gating what the TNCs hear to {options.Upstream!.Endpoint} as {igate.Callsign} "
                        + $"(receive-only, qAO), beaconing every {DurationParser.Format(igate.BeaconInterval)}"
                );
            }

            try
            {
                // Sources end only when the run does: APRS-IS and the TNCs reconnect on their own.
                await Task.WhenAll(sources).WaitAsync(run.Token);
            }
            catch (OperationCanceledException) when (run.IsCancellationRequested)
            {
                // Duration reached or Ctrl+C.
            }

            await feed.DisposeAsync();
            await uploading;
        }
        finally
        {
            foreach (var tnc in tncs)
            {
                await tnc.DisposeAsync();
            }
        }

        await environment.Error.WriteLineAsync(
            $"aprscli: served {feed.Published} packets ({feed.Duplicates} duplicates dropped); "
                + $"{server.ClientsDroppedForOverflow + (hub?.ClientsDroppedForOverflow ?? 0)} clients dropped for "
                + "falling behind"
        );
        var counts = feed.GetSourceCounts();
        if (counts.Count > 1)
        {
            await environment.Error.WriteLineAsync(
                "aprscli: by source: "
                    + string.Join(
                        ", ",
                        counts
                            .OrderBy(c => c.Key == AprsIsSource ? 0 : 1)
                            .ThenBy(c => c.Key, StringComparer.Ordinal)
                            .Select(c => $"{c.Key} {c.Value.Packets} ({c.Value.Duplicates} duplicates)")
                    )
            );
        }

        if (uplink is not null)
        {
            var refused = uplink.RefusedCounts().ToList();
            await environment.Error.WriteLineAsync(
                $"aprscli: gated {uplink.Gated} packets to APRS-IS ({uplink.Sent} sent, {uplink.Dropped} dropped), "
                    + $"{uplink.Beacons} beacons; refused "
                    + (
                        refused.Count == 0
                            ? "none"
                            : string.Join(", ", refused.Select(r => $"{IGateUplink.ReasonName(r.Reason)} {r.Count}"))
                    )
            );
        }

        return ExitCodes.Success;
    }

    private const string AprsIsSource = "aprs-is";

    /// <summary>The packets among APRS-IS lines; server comments such as keepalives are skipped.</summary>
    private static async IAsyncEnumerable<AprsPacket> PacketsAsync(IAsyncEnumerable<string> lines)
    {
        await foreach (var line in lines)
        {
            if (!line.StartsWith('#'))
            {
                yield return AprsPacket.Parse(line);
            }
        }
    }
}
