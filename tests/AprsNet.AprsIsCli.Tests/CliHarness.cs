using System.Net.Sockets;
using AprsNet.AprsIsTcpClient;
using AprsNet.AprsIsTcpClient.Tests;
using AprsNet.Kiss;
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

    /// <summary>A clock to use instead of the fixed one, for tests that move time on (message retries).</summary>
    public TimeProvider? Clock { get; init; }

    /// <summary>The options the CLI built for the client, so tests can check endpoint, callsign and passcode.</summary>
    public AprsIsClientOptions? ClientOptions { get; private set; }

    /// <summary>When set, the client's transport throws this on connect instead of using <see cref="Transport" />.</summary>
    public Exception? ConnectFailure { get; init; }

    /// <summary>Pretend stdout is a terminal, for <c>--color auto</c>.</summary>
    public bool OutIsTerminal { get; set; }

    /// <summary>Standard input for <c>parse</c> and <c>replay -</c>. Null leaves it empty.</summary>
    public string? Input { get; set; }

    /// <summary>Pretend stdin is a terminal, so <c>parse</c> without packets refuses to wait on it.</summary>
    public bool InIsTerminal { get; set; }

    /// <summary>
    /// Transports for the second and later connections, in order. Once used up, further connections are refused.
    /// </summary>
    public Queue<FakeAprsTransport> Reconnects { get; } = new();

    /// <summary>The transmit limit for <c>tnc send</c>: none by default, so tests do not wait.</summary>
    public Func<KissTransmitLimit> TransmitLimit { get; set; } = () => KissTransmitLimit.Off;

    /// <summary>The serial ports <c>tnc ports</c> finds.</summary>
    public IReadOnlyList<string> SerialPorts { get; set; } = [];

    /// <summary>How many transports the connection asked for, which is one per connection attempt.</summary>
    public int ConnectionAttempts { get; private set; }

    /// <summary>Queues a later connection whose login succeeds and which then sends <paramref name="lines" />.</summary>
    public void AddReconnect(params string[] lines)
    {
        var transport = new FakeAprsTransport();
        transport.Server($"# logresp NZ0P unverified, server T2TEST{Reconnects.Count + 2}");
        foreach (var line in lines)
            transport.Server(line);

        Reconnects.Enqueue(transport);
    }

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
            OutIsTerminal = OutIsTerminal,
            Error = error,
            In = Input is null ? TextReader.Null : new StringReader(Input),
            InIsTerminal = InIsTerminal,
            LoggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs)),
            TimeProvider = Clock ?? Time,
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
            CreateTransmitLimit = TransmitLimit,
            GetSerialPortNames = () => SerialPorts,
            GetEnvironmentVariable = name => Environment.GetValueOrDefault(name),
            CreateConnection = (options, logger) =>
            {
                ClientOptions = options;
                return new AprsIsConnection(options, logger, NextTransport);
            },
        };

        var code = await AprsCli.RunAsync(args, environment, cancellationToken);
        return new CliResult(code, output.ToString(), error.ToString());
    }

    private IAprsTransport NextTransport() =>
        ++ConnectionAttempts switch
        {
            1 when ConnectFailure is not null => new FailingTransport(ConnectFailure),
            1 => Transport,
            _ when Reconnects.TryDequeue(out var next) => next,
            _ => new FailingTransport(SocketFailures.Refused()),
        };

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

    public void Dispose() { }

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
