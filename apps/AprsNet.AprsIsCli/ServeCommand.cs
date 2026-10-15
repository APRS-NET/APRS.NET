using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using AprsNet.AprsIsTcpClient;
using AprsNet.Filters;
using AprsNet.Gateway;
using AprsNet.Packets;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The <c>serve</c> command: read packets from APRS-IS and serve them to local APRS-IS clients, each with its own
/// filter.
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

    private readonly Option<string> _filter = new("--filter", "-f")
    {
        Description =
            "Upstream APRS-IS filter: everything local clients can ask for, for example \"r/35/-97/300\". Each "
            + "client then narrows it with its own filter.",
        Required = true,
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
            "Serve an APRS-IS feed to local APRS-IS clients, each with its own filter. Clients are read-only."
        );
        foreach (var option in new Option[] { _callsign, _server, _filter, _listen, _name, _history, _duration })
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

    private sealed record ServeOptions(AprsIsClientOptions Upstream, AprsIsServerOptions Local, TimeSpan? Duration);

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

        var filter = parseResult.GetValue(_filter)!;
        if (!AprsFilter.TryParse(filter, out var parsedFilter, out var filterError) || parsedFilter.IsEmpty)
        {
            error = $"--filter: {filterError ?? "an upstream filter is required."}";
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

        var upstream = new AprsIsClientOptions
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
            upstream.Validate();
            local.Validate();
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        options = new ServeOptions(upstream, local, duration);
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
        await using var connection = environment.CreateConnection(options.Upstream, logger);
        using var reporter = new ConnectionReporter(connection, environment.Error, options.Upstream.Endpoint, true);
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
                $"aprscli: login failed on {options.Upstream.Endpoint}: {ex.Message}"
            );
            return ExitCodes.LoginFailed;
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
        {
            await environment.Error.WriteLineAsync(
                $"aprscli: could not connect to {options.Upstream.Endpoint}: {ex.Message}"
            );
            return ExitCodes.ConnectionFailed;
        }

        await environment.Error.WriteLineAsync(
            $"aprscli: serving {options.Upstream.Endpoint} ({options.Upstream.Filter}) on {server.LocalEndPoint} as "
                + $"{options.Local.ServerName}"
        );

        var dedupe = new PacketDeduplicator(timeProvider: environment.TimeProvider);
        long served = 0,
            duplicates = 0;
        try
        {
            await foreach (var line in connection.ReadLinesAsync(run.Token))
            {
                if (line.StartsWith('#'))
                {
                    continue;
                }

                var packet = AprsPacket.Parse(line);
                if (dedupe.TryAccept(packet))
                {
                    server.Publish(packet);
                    served++;
                }
                else
                {
                    duplicates++;
                }
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            // Duration reached or Ctrl+C.
        }

        await environment.Error.WriteLineAsync(
            $"aprscli: served {served} packets ({duplicates} duplicates dropped); "
                + $"{server.ClientsDroppedForOverflow} clients dropped for falling behind"
        );
        return ExitCodes.Success;
    }
}
