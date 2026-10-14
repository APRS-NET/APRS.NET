using System.CommandLine;
using System.Text;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli;

/// <summary>The <c>parse</c> command: decode packets given as arguments, in a file or on standard input.</summary>
internal sealed class ParseCommand(CliEnvironment environment)
{
    private readonly Argument<string[]> _packets = new("packet")
    {
        Description =
            "Packets in TNC2 format (SOURCE>DEST,PATH:INFO). Quote each one. Without any, lines are read from --file "
            + "or standard input.",
        Arity = ArgumentArity.ZeroOrMore,
    };

    private readonly Option<string?> _file = new("--file", "-i")
    {
        Description = "Read packets from this file, one per line. Capture files from listen --capture work too.",
    };

    private readonly Option<string> _format = new("--format")
    {
        Description =
            "Output: pretty (default, a labelled block per packet), decoded (one line per packet) or json (one JSON "
            + "object per line).",
        DefaultValueFactory = _ => "pretty",
    };

    private readonly Option<string> _color = new("--color")
    {
        Description =
            "Colour for --format pretty: auto (only on a terminal, and not when NO_COLOR is set), always or never.",
        DefaultValueFactory = _ => "auto",
    };

    public Command Build()
    {
        _format.AcceptOnlyFromAmong("pretty", "decoded", "json");
        _color.AcceptOnlyFromAmong("auto", "always", "never");

        var command = new Command("parse", "Decode APRS packets and print their fields.");
        command.Arguments.Add(_packets);
        command.Options.Add(_file);
        command.Options.Add(_format);
        command.Options.Add(_color);
        command.SetAction(RunAsync);
        return command;
    }

    private async Task<int> RunAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var packets = parseResult.GetValue(_packets) ?? [];
        var file = parseResult.GetValue(_file);

        if (packets.Length > 0 && file is not null)
        {
            return await UsageAsync("give packets as arguments or with --file, not both.");
        }

        if (file is not null && !File.Exists(file))
        {
            return await UsageAsync($"no such file: {file}");
        }

        if (packets.Length == 0 && file is null && environment.InIsTerminal)
        {
            return await UsageAsync("give a packet, use --file, or pipe lines in.");
        }

        var format = parseResult.GetValue(_format);
        var color = parseResult.GetValue(_color) switch
        {
            "always" => true,
            "never" => false,
            _ => environment.OutIsTerminal
                && string.IsNullOrEmpty(environment.GetEnvironmentVariable("NO_COLOR"))
                && environment.GetEnvironmentVariable("TERM") != "dumb",
        };

        try
        {
            if (packets.Length > 0)
            {
                foreach (var argument in packets)
                {
                    // Shell arguments are text, which a station would send as UTF-8, so parse those bytes.
                    var input = CaptureInput.Split(argument);
                    await PrintAsync(
                        AprsPacket.Parse(Encoding.UTF8.GetBytes(input.Line)),
                        input.Received,
                        format!,
                        color
                    );
                }

                return ExitCodes.Success;
            }

            using var reader = file is null ? null : CaptureInput.OpenFile(file);
            await foreach (var input in CaptureInput.ReadAsync(reader ?? environment.In, cancellationToken))
            {
                if (input.Line.Length > 0 && input.Line[0] != '#') // skip blank lines and server comments
                {
                    await PrintAsync(AprsPacket.Parse(input.Line), input.Received, format!, color); // Latin-1: the bytes as read
                }
            }
        }
        catch (IOException)
        {
            // output closed, for example `aprscli parse -i capture.log | head`
        }

        return ExitCodes.Success;
    }

    private async Task PrintAsync(AprsPacket packet, DateTimeOffset? capturedAt, string format, bool color)
    {
        var received = capturedAt ?? environment.TimeProvider.GetUtcNow();
        var text = format switch
        {
            "decoded" => LineFormatter.EscapeForConsole(PacketFormatter.Decoded(packet)),
            "json" => PacketFormatter.Json(packet, received),
            _ => PrettyFormatter.Format(packet, received, color) + Environment.NewLine,
        };
        await environment.Out.WriteLineAsync(text);
    }

    private async Task<int> UsageAsync(string message)
    {
        await environment.Error.WriteLineAsync($"aprscli: {message}");
        await environment.Error.WriteLineAsync("Try 'aprscli parse --help'.");
        return ExitCodes.Usage;
    }
}
