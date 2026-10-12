using System.Net.Sockets;
using AprsNet.AprsIsTcpClient;
using AprsNet.AprsIsTcpClient.Tests;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli.Tests;

internal sealed record CliResult(int ExitCode, string Out, string Error);

/// <summary>Runs the CLI in-process against a fake APRS-IS transport.</summary>
internal sealed class CliHarness
{
    public FakeAprsTransport Transport { get; } = new();

    /// <summary>Every log entry the CLI wrote, as (category, level, message).</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    public Dictionary<string, string> Environment { get; } = [];

    private FixedTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 11, 12, 0, 0, 123, TimeSpan.Zero));

    /// <summary>The options the CLI built for the client, so tests can check endpoint, callsign and passcode.</summary>
    public AprsIsClientOptions? ClientOptions { get; private set; }

    /// <summary>When set, the client's transport throws this on connect instead of using <see cref="Transport" />.</summary>
    public Exception? ConnectFailure { get; init; }

    public CliHarness LoginAccepted(string callsign = "NZ0P")
    {
        Transport.Server("# aprsc 2.1.test");
        Transport.Server($"# logresp {callsign} unverified, server T2TEST");
        return this;
    }

    public async Task<CliResult> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        await using var output = new StringWriter();
        await using var error = new StringWriter();
        var environment = new CliEnvironment
        {
            Out = output,
            Error = error,
            LoggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs)),
            TimeProvider = Time,
            GetEnvironmentVariable = name => Environment.GetValueOrDefault(name),
            CreateClient = (options, logger) =>
            {
                ClientOptions = options;
                IAprsTransport transport = ConnectFailure is null ? Transport : new FailingTransport(ConnectFailure);
                return new AprsIsClient(options, logger, transport);
            }
        };

        var code = await AprsCli.RunAsync(args, environment, cancellationToken);
        return new CliResult(code, output.ToString(), error.ToString());
    }

    private sealed class FailingTransport(Exception failure) : IAprsTransport
    {
        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            throw failure;
        }

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException();
        }

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<(string Category, LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    // ReSharper disable once InconsistentlySynchronizedField
    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(categoryName, _entries);
    }

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, List<(string, LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (entries)
            {
                entries.Add((category, logLevel, formatter(state, exception)));
            }
        }
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset Now { get; } = now;

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }
}

internal static class SocketFailures
{
    public static SocketException Refused()
    {
        return new SocketException((int)SocketError.ConnectionRefused);
    }
}
