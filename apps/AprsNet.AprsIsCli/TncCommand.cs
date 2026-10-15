using System.CommandLine;
using System.Globalization;
using System.Net.Sockets;
using AprsNet.Kiss;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The <c>tnc</c> commands: talk to a KISS TNC over TCP (Dire Wolf, a soundmodem, a network TNC). <c>tnc listen</c>
/// prints the APRS packets it hears, in the same formats as <c>listen</c>.
/// </summary>
internal sealed class TncCommand(CliEnvironment environment)
{
    private readonly Option<string> _tnc = new("--tnc", "-t")
    {
        Description = $"The TNC as host[:port]. Default port: {KissTcpClient.DefaultPort} (Dire Wolf's KISS port).",
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

    private readonly Option<string?> _duration = new("--duration", "-d")
    {
        Description = "Stop after this long, for example 90s, 10m, 2h or 1d.",
    };

    public Command Build()
    {
        _format.AcceptOnlyFromAmong("raw", "decoded", "json", "pretty");
        var listen = new Command("listen", "Connect to a KISS TNC over TCP and print the APRS packets it hears.")
        {
            _tnc,
            _radioPort,
            _format,
            _count,
            _duration,
        };
        listen.SetAction(ListenAsync);
        var tnc = new Command("tnc", "Talk to a KISS TNC over TCP, such as Dire Wolf.") { listen };
        return tnc;
    }

    private async Task<int> ListenAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var text = parseResult.GetValue(_tnc)!.Trim();
        var colon = text.LastIndexOf(':');
        var host = colon < 0 ? text : text[..colon];
        var port = KissTcpClient.DefaultPort;
        if (
            host.Length == 0
            || (
                colon >= 0
                && !int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port)
            )
            || port is < 1 or > 65535
        )
        {
            return await UsageAsync($"--tnc '{text}' is not valid. Use host or host:port, such as localhost:8001.");
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

        var format = Enum.Parse<OutputFormat>(parseResult.GetValue(_format)!, ignoreCase: true);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (duration is { } limit)
        {
            run.CancelAfter(limit);
        }

        await using var client = new KissTcpClient(host, port, parseResult.GetValue(_radioPort));
        try
        {
            await client.ConnectAsync(run.Token);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException)
        {
            await environment.Error.WriteLineAsync(
                $"aprscli: could not connect to the TNC at {host}:{port}: {ex.Message}"
            );
            return ExitCodes.ConnectionFailed;
        }

        var received = 0;
        try
        {
            await foreach (var packet in client.ReadPacketsAsync(run.Token))
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
                if (parseResult.GetValue(_count) is { } count && ++received >= count)
                {
                    return ExitCodes.Success;
                }
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }
        catch (IOException ex)
        {
            await environment.Error.WriteLineAsync($"aprscli: lost the TNC at {host}:{port}: {ex.Message}");
            return ExitCodes.ConnectionFailed;
        }

        await environment.Error.WriteLineAsync($"aprscli: the TNC at {host}:{port} closed the connection.");
        return ExitCodes.ConnectionFailed;
    }

    private async Task<int> UsageAsync(string message)
    {
        await environment.Error.WriteLineAsync($"aprscli: {message}");
        await environment.Error.WriteLineAsync("Try 'aprscli tnc listen --help'.");
        return ExitCodes.Usage;
    }
}
