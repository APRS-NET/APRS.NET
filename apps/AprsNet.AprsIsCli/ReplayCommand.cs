using System.CommandLine;

namespace AprsNet.AprsIsCli;

/// <summary>The <c>replay</c> command: run captured lines through the parser and report what it made of them.</summary>
internal sealed class ReplayCommand(CliEnvironment environment)
{
    private readonly Argument<string[]> _files = new("files")
    {
        Description = "Capture files (from listen --capture) or files of raw lines. Use - for standard input.",
        Arity = ArgumentArity.OneOrMore,
    };

    private readonly Option<int> _examples = new("--examples", "-e")
    {
        Description = "Example lines to show for each issue and undecoded data type. 0 hides them. Default: 3.",
        DefaultValueFactory = _ => 3,
    };

    private readonly Option<string> _format = new("--format")
    {
        Description = "Report format: text (default) or json.",
        DefaultValueFactory = _ => "text",
    };

    public Command Build()
    {
        _format.AcceptOnlyFromAmong("text", "json");

        var command = new Command(
            "replay",
            "Run captured packets through the parser and report outcomes, packet types, issues and undecoded types."
        );
        command.Arguments.Add(_files);
        command.Options.Add(_examples);
        command.Options.Add(_format);
        command.SetAction(RunAsync);
        return command;
    }

    private async Task<int> RunAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var files = parseResult.GetValue(_files)!;
        var examples = parseResult.GetValue(_examples);
        if (examples < 0)
        {
            return await UsageAsync("--examples cannot be negative.");
        }

        if (files.Count(f => f == "-") > 1)
        {
            return await UsageAsync("standard input (-) can only be read once.");
        }

        // Check every file before reading any, so a typo in the last one does not waste a long run.
        var missing = files.FirstOrDefault(f => f != "-" && !File.Exists(f));
        if (missing is not null)
        {
            return await UsageAsync($"no such file: {missing}");
        }

        var report = new ReplayReport(examples);
        foreach (var file in files)
        {
            report.Files++;
            var name = file == "-" ? "stdin" : file;
            using var reader = file == "-" ? null : CaptureInput.OpenFile(file);
            await foreach (var input in CaptureInput.ReadAsync(reader ?? environment.In, cancellationToken))
            {
                report.Add(input.Line, $"{name}:{input.Number}");
            }
        }

        try
        {
            await environment.Out.WriteAsync(
                parseResult.GetValue(_format) == "json" ? report.ToJson() + "\n" : report.ToText()
            );
        }
        catch (IOException)
        {
            // output closed, for example `aprscli replay capture.log | head`
        }

        return ExitCodes.Success;
    }

    private async Task<int> UsageAsync(string message)
    {
        await environment.Error.WriteLineAsync($"aprscli: {message}");
        await environment.Error.WriteLineAsync("Try 'aprscli replay --help'.");
        return ExitCodes.Usage;
    }
}
