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

    /// <summary>The transmit limit for <c>tnc send</c>; tests replace it to avoid real waits.</summary>
    public Func<KissTransmitLimit> CreateTransmitLimit { get; init; } = () => new KissTransmitLimit();

    /// <summary>A console on the command line of the serial TNC at an address, for <c>tnc terminal</c> and <c>tnc kiss</c>; tests replace it.</summary>
    public Func<TncAddress, TncTerminal> OpenTncTerminal { get; init; } =
        address =>
            KissSerialConnection.OpenTerminal(
                new KissSerialOptions
                {
                    PortName = address.SerialPort!,
                    BaudRate = address.BaudRate,
                    DtrEnable = address.Dtr,
                    RtsEnable = address.Rts,
                }
            );

    /// <summary>A reconnecting KISS connection to the TNC at <paramref name="address"/>, over TCP or a serial port.</summary>
    public KissConnection CreateTncConnection(TncAddress address, int radioPort = 0, TimeSpan? quietWarning = null) =>
        address.SerialPort is { } name
            ? new KissSerialConnection(
                new KissSerialOptions
                {
                    PortName = name,
                    BaudRate = address.BaudRate,
                    DtrEnable = address.Dtr,
                    RtsEnable = address.Rts,
                    Setup = address.Setup,
                    TextMode = address.TextMode,
                    TncPort = radioPort,
                    TimeProvider = TimeProvider,
                    QuietWarning = quietWarning,
                }
            )
            : new KissTcpConnection(
                new KissTcpOptions
                {
                    Host = address.Host,
                    Port = address.Port,
                    TncPort = radioPort,
                    ReconnectDelay = ReconnectDelay,
                    TimeProvider = TimeProvider,
                    QuietWarning = quietWarning,
                }
            );

    /// <summary>The serial ports present, for <c>tnc ports</c>; tests replace it.</summary>
    public Func<IReadOnlyList<string>> GetSerialPortNames { get; init; } = KissSerialConnection.GetPortNames;

    /// <summary>Makes the listener ID used when no <c>--callsign</c> is given; tests replace it.</summary>
    public Func<string> NewListenerId { get; init; } = () => AprsIsLogin.NewListenerId();
}
