using AprsNet.AprsIsTcpClient;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli;

/// <summary>Everything the commands touch outside the process: streams, environment, clock and the client factory. Tests replace these.</summary>
internal sealed class CliEnvironment
{
    public required TextWriter Out { get; init; }

    public required TextWriter Error { get; init; }

    public required ILoggerFactory LoggerFactory { get; init; }

    public Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Whether <see cref="Out"/> is an interactive terminal (not a pipe or file), for <c>--color auto</c>.</summary>
    public bool OutIsTerminal { get; init; }

    /// <summary>First wait before <c>--reconnect</c> tries again; it doubles up to a minute.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    public Func<AprsIsClientOptions, ILogger, AprsIsClient> CreateClient { get; init; } =
        (options, logger) => new AprsIsClient(options, logger);
}
