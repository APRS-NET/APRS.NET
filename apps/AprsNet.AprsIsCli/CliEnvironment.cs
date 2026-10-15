using AprsNet.AprsIsTcpClient;
using AprsNet.Kiss;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli;

/// <summary>Everything the commands touch outside the process: streams, environment, clock and the client factory. Tests replace these.</summary>
internal sealed class CliEnvironment
{
    public required TextWriter Out { get; init; }

    public required TextWriter Error { get; init; }

    /// <summary>Standard input, read as Latin-1 so packet bytes arrive unchanged (<c>parse</c> and <c>replay -</c>).</summary>
    public TextReader In { get; init; } = TextReader.Null;

    /// <summary>Whether <see cref="In"/> is an interactive terminal, so <c>parse</c> does not wait on it for nothing.</summary>
    public bool InIsTerminal { get; init; }

    public required ILoggerFactory LoggerFactory { get; init; }

    public Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Whether <see cref="Out"/> is an interactive terminal (not a pipe or file), for <c>--color auto</c>.</summary>
    public bool OutIsTerminal { get; init; }

    /// <summary>First wait before reconnecting; it doubles up to a minute.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    public Func<AprsIsClientOptions, ILogger, AprsIsConnection> CreateConnection { get; init; } =
        (options, logger) => new AprsIsConnection(options, logger);

    /// <summary>A reconnecting KISS connection to the TNC at <paramref name="host"/>:<paramref name="port"/>.</summary>
    public KissTcpConnection CreateTncConnection(string host, int port) =>
        new(host, port, reconnectDelay: ReconnectDelay, timeProvider: TimeProvider);
}
