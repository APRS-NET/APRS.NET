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

    public Func<AprsIsClientOptions, ILogger, AprsIsClient> CreateClient { get; init; } =
        (options, logger) => new AprsIsClient(options, logger);
}
