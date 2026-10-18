using System.CommandLine;
using System.Text;
using AprsNet.Kiss;

namespace AprsNet.AprsIsCli;

/// <summary>
/// <c>tnc terminal</c>, a console on a serial TNC's own command line, and <c>tnc kiss on|off</c>, which runs a radio's
/// <see cref="TncSetup"/> once. Neither transmits: they only talk to the TNC.
/// </summary>
internal sealed class TncConsoleCommand(CliEnvironment environment)
{
    /// <summary>How long to keep printing what the TNC says after the last command.</summary>
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(1);

    private readonly Option<string> _tnc = new("--tnc", "-t")
    {
        Description =
            "The serial TNC as serial:PORT[@BAUD][,dtr=off][,rts=off][,setup=ID], such as serial:/dev/ttyUSB0 or "
            + $"serial:/dev/ttyUSB0,setup=th-d72. Built-in setups: {string.Join(", ", TncSetup.Known.Select(s => s.Id))}.",
        Required = true,
    };

    private readonly Option<string?> _script = new("--script")
    {
        Description =
            "Run the script in this file instead of reading commands: lines of send TEXT, bytes HEX, expect TIME "
            + "TEXT and pause TIME (see the CLI README). Prints what the TNC said.",
    };

    public IEnumerable<Command> Build()
    {
        var terminal = new Command(
            "terminal",
            "Type commands at a serial TNC's own command line and see what it says: for setting up a TNC by hand, "
                + "such as a radio's built-in TNC. A line starting with ~ is a script step, such as ~bytes C0 FF C0 "
                + "(the KISS exit frame). Ctrl+D ends it."
        )
        {
            _tnc,
            _script,
        };
        terminal.SetAction(TerminalAsync);

        var on = new Command("on", "Switch the TNC into KISS mode with its setup (,setup= in --tnc).") { _tnc };
        on.SetAction((result, token) => KissAsync(result, enter: true, token));
        var off = new Command("off", "Switch the TNC out of KISS mode, back to its command line.") { _tnc };
        off.SetAction((result, token) => KissAsync(result, enter: false, token));
        var kiss = new Command(
            "kiss",
            "Switch a radio's built-in TNC, or one with its own command line, into or out of KISS mode."
        )
        {
            on,
            off,
        };
        return [terminal, kiss];
    }

    private async Task<int> TerminalAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (await AddressAsync(parseResult, "terminal") is not { } address)
        {
            return ExitCodes.Usage;
        }

        TncScript? script = null;
        if (parseResult.GetValue(_script) is { } path)
        {
            try
            {
                script = TncScript.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                return await UsageAsync($"--script '{path}': {e.Message}", "terminal");
            }
        }

        if (await OpenAsync(address) is not { } terminal)
        {
            return ExitCodes.ConnectionFailed;
        }

        await using (terminal)
        {
            if (script is not null)
            {
                return await RunScriptAsync(terminal, script, address, cancellationToken);
            }

            await environment.Error.WriteLineAsync(
                $"aprscli: talking to the TNC at {address}. Type its commands; a line starting with ~ is a script "
                    + "step, such as ~bytes C0 FF C0. Ctrl+D ends."
            );
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var printing = PrintAsync(terminal, stop.Token);
            try
            {
                while (await environment.In.ReadLineAsync(cancellationToken) is { } line)
                {
                    if (line.StartsWith('~'))
                    {
                        try
                        {
                            await terminal.RunAsync(
                                TncScript.Parse(line[1..]),
                                environment.TimeProvider,
                                cancellationToken
                            );
                        }
                        catch (Exception e) when (e is FormatException or TncScriptException)
                        {
                            await environment.Error.WriteLineAsync($"aprscli: {e.Message}");
                        }
                    }
                    else
                    {
                        await terminal.SendLineAsync(line, cancellationToken);
                    }
                }

                await Task.Delay(Linger, environment.TimeProvider, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ctrl+C: end like Ctrl+D.
            }
            catch (IOException e)
            {
                await environment.Error.WriteLineAsync($"aprscli: lost the TNC at {address}: {e.Message}");
                return ExitCodes.ConnectionFailed;
            }
            finally
            {
                await stop.CancelAsync();
                await printing;
            }
        }

        return ExitCodes.Success;
    }

    private async Task<int> KissAsync(ParseResult parseResult, bool enter, CancellationToken cancellationToken)
    {
        var command = enter ? "kiss on" : "kiss off";
        if (await AddressAsync(parseResult, command) is not { } address)
        {
            return ExitCodes.Usage;
        }

        if (address.Setup is not { } setup)
        {
            return await UsageAsync(
                $"{command} needs the TNC's setup in --tnc, such as serial:{address.SerialPort},setup=th-d72. "
                    + $"Built-in setups: {string.Join(", ", TncSetup.Known.Select(s => s.Id))}.",
                command
            );
        }

        if ((enter ? setup.EnterKiss : setup.LeaveKiss) is not { } script)
        {
            return await UsageAsync($"The {setup.Name} setup has no way out of KISS mode.", command);
        }

        if (await OpenAsync(address) is not { } terminal)
        {
            return ExitCodes.ConnectionFailed;
        }

        await using (terminal)
        {
            try
            {
                await terminal.RunAsync(script, environment.TimeProvider, cancellationToken);
            }
            catch (TncScriptException e)
            {
                await environment.Error.WriteLineAsync(
                    $"aprscli: the TNC at {address} didn't switch {(enter ? "to" : "out of")} KISS mode: {e.Message}"
                        + (enter && setup.Hint is { } hint ? " " + hint : "")
                );
                return ExitCodes.ConnectionFailed;
            }
        }

        await environment.Out.WriteLineAsync(
            enter
                ? $"The TNC at {address} is in KISS mode ({setup.Name})."
                : $"The TNC at {address} has left KISS mode ({setup.Name})."
        );
        return ExitCodes.Success;
    }

    private async Task<int> RunScriptAsync(
        TncTerminal terminal,
        TncScript script,
        TncAddress address,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await environment.Out.WriteAsync(
                Printable(await terminal.RunAsync(script, environment.TimeProvider, cancellationToken), out _)
            );
            await environment.Out.WriteLineAsync();
            return ExitCodes.Success;
        }
        catch (TncScriptException e)
        {
            await environment.Out.WriteAsync(Printable(e.Transcript, out _));
            await environment.Out.WriteLineAsync();
            await environment.Error.WriteLineAsync($"aprscli: the script stopped at the TNC at {address}: {e.Message}");
            return ExitCodes.ConnectionFailed;
        }
    }

    /// <summary>Prints what the TNC says until stopped or the port closes.</summary>
    private async Task PrintAsync(TncTerminal terminal, CancellationToken cancellationToken)
    {
        var afterCr = false;
        try
        {
            await foreach (var piece in terminal.ReadAsync(cancellationToken))
            {
                var text = Printable(piece, out var endsWithCr, afterCr);
                afterCr = endsWithCr;
                await environment.Out.WriteAsync(text);
                await environment.Out.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException e)
        {
            await environment.Error.WriteLineAsync($"aprscli: the TNC's port closed: {e.Message}");
        }
    }

    /// <summary>
    /// The TNC's text for a terminal: CR, LF and CR LF become one line break, and other control bytes and bytes above
    /// 0x7E (KISS frames, for example) become <c>\xNN</c>, so they can't drive the terminal.
    /// </summary>
    internal static string Printable(string text, out bool endsWithCr, bool afterCr = false)
    {
        var result = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == '\n' && afterCr)
            {
                afterCr = false;
                continue;
            }

            afterCr = c == '\r';
            result.Append(
                c switch
                {
                    '\r' or '\n' => "\n",
                    '\t' => "\t",
                    < ' ' or > '~' => $"\\x{(int)c:X2}",
                    _ => c.ToString(),
                }
            );
        }

        endsWithCr = afterCr;
        return result.ToString();
    }

    private async Task<TncAddress?> AddressAsync(ParseResult parseResult, string command)
    {
        var text = parseResult.GetValue(_tnc)!.Trim();
        if (!TncAddress.TryParse(text, out var address))
        {
            await UsageAsync($"--tnc '{text}' is not valid. Use {TncAddress.Forms}.", command);
            return null;
        }

        if (!address.IsSerial)
        {
            await UsageAsync(
                $"tnc {command} talks to a TNC's command line on a serial port; give --tnc as serial:PORT.",
                command
            );
            return null;
        }

        return address;
    }

    private async Task<TncTerminal?> OpenAsync(TncAddress address)
    {
        try
        {
            return environment.OpenTncTerminal(address);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await environment.Error.WriteLineAsync($"aprscli: cannot open the TNC at {address}: {e.Message}");
            return null;
        }
    }

    private async Task<int> UsageAsync(string message, string command)
    {
        await environment.Error.WriteLineAsync($"aprscli: {message}");
        await environment.Error.WriteLineAsync($"Try 'aprscli tnc {command} --help'.");
        return ExitCodes.Usage;
    }
}
