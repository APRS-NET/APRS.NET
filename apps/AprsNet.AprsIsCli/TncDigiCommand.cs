using System.CommandLine;
using System.Globalization;
using System.Threading.Channels;
using AprsNet.Ax25;
using AprsNet.Kiss;

namespace AprsNet.AprsIsCli;

/// <summary>
/// <c>tnc digi</c>: runs an APRS digipeater on a KISS TNC. Each frame heard goes through <see cref="Digipeater"/>, and
/// each one it repeats is sent back to the TNC, within the transmit limit. A fill-in digipeater by default.
/// </summary>
internal sealed class TncDigiCommand(CliEnvironment environment)
{
    // Repeats waiting for the transmit limit; more are dropped, since a repeat sent late only adds to a busy channel.
    private const int MaxWaiting = 3;

    private readonly Option<string> _tnc = new("--tnc", "-t")
    {
        Description =
            "The TNC: host[:port] for a TNC over TCP such as Dire Wolf, agw:host[:port] for an AGWPE server, or "
            + "serial:PORT[@BAUD]. Add ,setup=th-d72 to "
            + "switch a radio's built-in TNC into KISS.",
        Required = true,
    };

    private readonly Option<string> _call = new("--call", "-c")
    {
        Description = "The digipeater's call with its SSID, such as N0CALL-1: inserted in each path it repeats.",
        Required = true,
    };

    private readonly Option<bool> _wide = new("--wide")
    {
        Description =
            "Repeat WIDEn-N as a wide-area digipeater, counting N down and trapping paths asking for more than "
            + "--max-hops. Only by agreement with the local APRS network. Default: fill-in, WIDE1-1 only.",
    };

    private readonly Option<string[]> _alias = new("--alias")
    {
        Description = "Another name the digipeater answers to, such as a location. Repeat for several.",
        AllowMultipleArgumentsPerToken = false,
    };

    private readonly Option<int> _maxHops = new("--max-hops")
    {
        Description = "With --wide, the most hops a WIDEn-N may ask for, 1 to 7; wider ones are trapped. Default: 2.",
        DefaultValueFactory = _ => 2,
    };

    private readonly Option<bool> _preemptive = new("--preemptive")
    {
        Description = "Also take the call or an alias later in the path, ahead of unused entries. Default: off.",
    };

    private readonly Option<string> _transmitLimit = new("--transmit-limit")
    {
        Description =
            "on: send at most 3 repeats at once, then one each 10s, dropping repeats that would wait longer than 3 "
            + "behind; off: no limit. Default: on.",
        DefaultValueFactory = _ => "on",
    };

    private readonly Option<int> _radioPort = new("--radio-port")
    {
        Description = "The radio port on a multi-port TNC, 0 to 15. Default: 0.",
        DefaultValueFactory = _ => 0,
    };

    private readonly Option<bool> _quiet = new("--quiet", "-q")
    {
        Description = "Print only the frames repeated, not those refused.",
    };

    private readonly Option<string?> _duration = new("--duration", "-d")
    {
        Description = "Stop after this long, for example 90s, 10m, 2h or 1d. Default: until Ctrl+C.",
    };

    public Command Build()
    {
        _transmitLimit.AcceptOnlyFromAmong("on", "off");
        var command = new Command(
            "digi",
            "Run an APRS digipeater on a KISS TNC: repeat WIDE1-1, the call and its aliases (and WIDEn-N with --wide), "
                + "printing each frame repeated or refused and why. It transmits on its own: you need a licence."
        )
        {
            _tnc,
            _call,
            _wide,
            _alias,
            _maxHops,
            _preemptive,
            _transmitLimit,
            _radioPort,
            _quiet,
            _duration,
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
            return await UsageAsync("--tnc with ,text is receive only and can't digipeat; leave ,text off.");
        }

        var callText = parseResult.GetValue(_call)!.Trim();
        if (!Ax25Address.TryParse(callText.ToUpperInvariant(), out var call))
        {
            return await UsageAsync(
                $"--call '{callText}' is not valid. Use one to six letters and digits and an SSID 0 to 15, such as "
                    + "N0CALL-1."
            );
        }

        var radioPort = parseResult.GetValue(_radioPort);
        if (radioPort is < 0 or > 15)
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

        var wide = parseResult.GetValue(_wide);
        var options = new DigipeaterOptions
        {
            FillInOnly = !wide,
            Aliases = parseResult.GetValue(_alias) ?? [],
            MaxHops = parseResult.GetValue(_maxHops),
            Preemptive = parseResult.GetValue(_preemptive),
            TimeProvider = environment.TimeProvider,
        };
        Digipeater digi;
        try
        {
            digi = new Digipeater(call, options);
        }
        catch (ArgumentException ex)
        {
            return await UsageAsync(
                ex.ParamName == nameof(DigipeaterOptions.MaxHops)
                    ? "--max-hops must be 1 to 7."
                    : "--alias must be one to six letters and digits, with an SSID 0 to 15, such as KCMO or WIDE3-3."
            );
        }

        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (duration is { } limit)
        {
            run.CancelAfter(limit);
        }

        var error = environment.Error;
        var events = environment.LoggerFactory.CreateLogger(Logging.EventCategory);
        var name = address.ToString();
        var limited = parseResult.GetValue(_transmitLimit) == "on";
        await using var tnc = environment.CreateTncConnection(
            address,
            radioPort,
            transmitLimit: limited
                ? new KissTransmitLimit(timeProvider: environment.TimeProvider)
                : KissTransmitLimit.Off
        );
        TncCommand.ReportConnection(tnc, name, error, events);
        await error.WriteLineAsync(
            $"aprscli: digipeating as {digi.Call} on the TNC at {name}: "
                + (wide ? $"WIDEn-N up to {options.MaxHops} hops" : "fill-in, WIDE1-1")
                + (options.Aliases.Count > 0 ? $", aliases {string.Join(", ", options.Aliases)}" : "")
                + (options.Preemptive ? ", preemptive" : "")
                + (limited ? "" : ", no transmit limit")
        );

        var quiet = parseResult.GetValue(_quiet);
        var refusals = new Dictionary<DigipeatRefusal, long>();
        long heard = 0;
        long repeated = 0;
        long dropped = 0;
        var waiting = Channel.CreateBounded<Ax25Frame>(
            new BoundedChannelOptions(MaxWaiting) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true },
            frame =>
            {
                Interlocked.Increment(ref dropped);
                Print($"not repeated (transmit limit): {LineFormatter.EscapeForConsole(frame.ToTnc2())}");
            }
        );
        var sending = SendAsync(waiting.Reader, run.Token);
        try
        {
            await foreach (var frame in tnc.ReadFramesAsync(run.Token))
            {
                heard++;
                var decision = digi.Digipeat(frame);
                if (decision.Frame is { } repeat)
                {
                    waiting.Writer.TryWrite(repeat);
                }
                else
                {
                    var reason = decision.Refusal!.Value;
                    refusals[reason] = refusals.GetValueOrDefault(reason) + 1;
                    if (!quiet)
                    {
                        Print($"not repeated ({Describe(reason)}): {LineFormatter.EscapeForConsole(frame.ToTnc2())}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            // Duration reached or Ctrl+C.
        }

        waiting.Writer.Complete();
        await sending;
        var refused = string.Join(
            ", ",
            refusals.OrderByDescending(r => r.Value).Select(r => $"{r.Value} {Describe(r.Key)}")
        );
        var droppedTotal = Interlocked.Read(ref dropped);
        await error.WriteLineAsync(
            $"aprscli: heard {heard} frames and repeated {Interlocked.Read(ref repeated)}"
                + (droppedTotal > 0 ? $" ({droppedTotal} more dropped by the transmit limit)" : "")
                + (refused.Length > 0 ? $"; not repeated: {refused}" : "")
        );
        return ExitCodes.Success;

        async Task SendAsync(ChannelReader<Ax25Frame> frames, CancellationToken token)
        {
            try
            {
                await foreach (var frame in frames.ReadAllAsync(token))
                {
                    try
                    {
                        await tnc.SendFrameAsync(frame, token);
                        Interlocked.Increment(ref repeated);
                        Print($"repeated {LineFormatter.EscapeForConsole(frame.ToTnc2())}");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Interlocked.Increment(ref dropped);
                        lock (error)
                        {
                            error.WriteLine($"aprscli: could not send a repeat: {ex.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Stopping: repeats still waiting are not sent.
            }
        }

        void Print(string line)
        {
            var at = environment.TimeProvider.GetUtcNow();
            lock (environment.Out)
            {
                environment.Out.WriteLine(
                    $"{at.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)} {line}"
                );
            }
        }
    }

    internal static string Describe(DigipeatRefusal refusal) =>
        refusal switch
        {
            DigipeatRefusal.NotAprs => "not APRS",
            DigipeatRefusal.NoHopsLeft => "no hops left",
            DigipeatRefusal.NotForUs => "not for this digipeater",
            DigipeatRefusal.OwnFrame => "its own frame",
            DigipeatRefusal.Duplicate => "repeated already",
            _ => refusal.ToString(),
        };

    private async Task<int> UsageAsync(string message)
    {
        await environment.Error.WriteLineAsync($"aprscli: {message}");
        await environment.Error.WriteLineAsync("Try 'aprscli tnc digi --help'.");
        return ExitCodes.Usage;
    }
}
