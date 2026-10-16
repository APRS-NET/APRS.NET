using System.CommandLine;

namespace AprsNet.AprsIsCli;

/// <summary>Builds the <c>aprscli</c> command tree and runs it.</summary>
internal static class AprsCli
{
    private static RootCommand Build(CliEnvironment environment)
    {
        var root = new RootCommand("APRS.NET command-line tool.");
        root.Subcommands.Add(new ListenCommand(environment).Build());
        root.Subcommands.Add(new PasscodeCommand(environment).Build());
        root.Subcommands.Add(new ParseCommand(environment).Build());
        root.Subcommands.Add(new ReplayCommand(environment).Build());
        root.Subcommands.Add(new SendCommand(environment).Build());
        root.Subcommands.Add(new TncCommand(environment).Build());
        root.Subcommands.Add(new ServeCommand(environment).Build());
        root.Subcommands.Add(new HealthCommand(environment).Build());
        return root;
    }

    public static async Task<int> RunAsync(
        string[] args,
        CliEnvironment environment,
        CancellationToken cancellationToken
    )
    {
        var parseResult = Build(environment).Parse(args);

        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
            {
                await environment.Error.WriteLineAsync($"aprscli: {error.Message}");
            }

            await environment.Error.WriteLineAsync("Try 'aprscli --help'.");
            return ExitCodes.Usage;
        }

        try
        {
            return await parseResult.InvokeAsync(
                new InvocationConfiguration { Output = environment.Out, Error = environment.Error },
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            await environment.Error.WriteLineAsync($"aprscli: unexpected error: {ex.Message}");
            return ExitCodes.Error;
        }
    }
}
