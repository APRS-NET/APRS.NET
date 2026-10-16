using System.CommandLine;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using AprsNet.AprsIsTcpClient;
using AprsNet.Filters;
using AprsNet.Gateway;
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

    private readonly Option<string?> _callsign = new("--callsign", "-c")
    {
        Description =
            $"Callsign for the read-only upstream login, with optional SSID. Default: {ListenCommand.DefaultCallsign}.",
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

    public Command Build()
    {
        var command = new Command(
            "serve",
            "Serve an APRS-IS feed, KISS TNCs or both to local APRS-IS clients, each with its own filter. A packet "
                + "heard on several sources is served once. Clients are read-only."
        );
        foreach (var option in new Option[] { _callsign, _server, _filter, _tnc, _listen, _name, _history, _duration })
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
        TimeSpan? Duration
    );

    private bool TryBind(ParseResult parseResult, out ServeOptions options, out string error)
    {
        options = null!;
        var callsign = (parseResult.GetValue(_callsign) ?? ListenCommand.DefaultCallsign).Trim().ToUpperInvariant();
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

        var listenText = parseResult.GetValue(_listen)!;
        if (
            !IPEndPoint.TryParse(listenText, out var listen)
            || (listen.Port == 0 && !listenText.EndsWith(":0", StringComparison.Ordinal))
        )
        {
            error = $"--listen '{listenText}' is not valid. Use address:port, for example {_defaultListen}.";
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

        var upstream = parsedFilter is null
            ? null
            : new AprsIsClientOptions
            {
                Endpoint = endpoint,
                Callsign = callsign,
                Filter = parsedFilter.ToString(),
                SoftwareName = "aprscli",
                TimeProvider = environment.TimeProvider,
                ReconnectDelay = environment.ReconnectDelay,
            };
        var local = new AprsIsServerOptions
        {
            ServerName = parseResult.GetValue(_name)!,
            EndPoint = listen,
            History = history,
            SoftwareName = "aprscli",
            TimeProvider = environment.TimeProvider,
        };
        try
        {
            upstream?.Validate();
            local.Validate();
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        options = new ServeOptions(upstream, tncs, local, duration);
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
                await connection.StartAsync(run.Token);
            }
            catch (OperationCanceledException) when (run.IsCancellationRequested)
            {
                return ExitCodes.Success;
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

        await using var feed = new GatewayFeed(server, timeProvider: environment.TimeProvider);
        var sources = new List<Task>();
        var described = new List<string>();
        if (connection is not null)
        {
            sources.Add(feed.AddSource(AprsIsSource, PacketsAsync(connection.ReadLinesAsync(run.Token))));
            described.Add($"{options.Upstream!.Endpoint} ({options.Upstream.Filter})");
        }

        var tncs = new List<KissConnection>();
        try
        {
            foreach (var address in options.Tncs)
            {
                var tnc = environment.CreateTncConnection(address);
                tncs.Add(tnc);
                var name = address.ToString();
                tnc.StateChanged += (_, e) =>
                {
                    if (e.State != KissConnectionState.Reconnecting)
                    {
                        return;
                    }

                    var retry = e.RetryIn!.Value.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);
                    environment.Error.WriteLine(
                        e.Error is null
                            ? $"aprscli: the TNC at {name} closed the connection; reconnecting in {retry}s."
                            : $"aprscli: lost the TNC at {name}: {e.Error.Message}; reconnecting in {retry}s."
                    );
                };
                sources.Add(feed.AddSource(name, tnc.ReadPacketsAsync(run.Token)));
                described.Add($"the TNC at {name}");
            }

            await environment.Error.WriteLineAsync(
                $"aprscli: serving {string.Join(" and ", described)} on {server.LocalEndPoint} as "
                    + $"{options.Local.ServerName}"
            );

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
                + $"{server.ClientsDroppedForOverflow} clients dropped for falling behind"
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
