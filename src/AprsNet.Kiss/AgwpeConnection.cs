namespace AprsNet.Kiss;

/// <summary>
/// A lasting connection to a TNC through an AGWPE server over TCP: Dire Wolf's AGW port, UZ7HO SoundModem, or AGWPE
/// itself. It reads and sends raw AX.25 frames (AGWPE's <c>K</c> frames) and otherwise works as
/// <see cref="KissTcpConnection"/> does: it reconnects with the same waits and states, reads one radio port or every
/// one (<see cref="KissConnection.AllPorts"/>), applies the transmit limit per port, and warns when the server goes
/// quiet.
/// </summary>
/// <remarks>
/// <para>
/// On each connection it asks for the server's version (<see cref="ServerVersion"/>), its radio ports
/// (<see cref="Ports"/>) and raw frames. Ports are numbered from 0, as in KISS: the server's <c>Port1</c> is port 0.
/// It registers no callsign, as raw frames need none; connected mode isn't supported.
/// </para>
/// <para>
/// <see cref="KissConnection.SendRawAsync"/>, <see cref="KissConnection.SendLineAsync"/> and
/// <see cref="KissConnection.RunScriptAsync"/> are for a TNC's command prompt and have no use here.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// await using var tnc = new AgwpeConnection("localhost"); // Dire Wolf's AGW port, 8000
/// await foreach (var packet in tnc.ReadPacketsAsync(cancellationToken))
/// {
///     Console.WriteLine(packet);
/// }
/// </code>
/// </example>
public sealed class AgwpeConnection : KissConnection
{
    private IReadOnlyList<AgwpePort> _ports = [];
    private string? _version;

    /// <summary>
    /// Creates a connection to the AGWPE server at <paramref name="host"/>:<paramref name="port"/>, on its first radio
    /// port, with the other settings at their defaults; nothing connects until a read starts.
    /// </summary>
    /// <exception cref="ArgumentException">The host is empty or the port is not 1 to 65535.</exception>
    public AgwpeConnection(string host, int port = AgwpeOptions.DefaultPort)
        : this(new AgwpeOptions { Host = host, Port = port }) { }

    /// <summary>Creates a connection with <paramref name="options"/>; nothing connects until a read starts.</summary>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public AgwpeConnection(AgwpeOptions options)
        : this(KissTcpConnection.ConnectTcp(Validated(options).ToTcp()), options) { }

    internal AgwpeConnection(Func<CancellationToken, Task<Stream>> connect, AgwpeOptions options)
        : base(
            connect,
            $"{Validated(options).Host}:{options.Port}",
            options.TncPort,
            options.ReconnectDelay,
            options.MaxReconnectDelay,
            options.TimeProvider,
            options.TransmitLimit ?? new KissTransmitLimit(timeProvider: options.TimeProvider),
            options.QuietWarning,
            allPorts: options.AllPorts,
            agwpe: true
        ) { }

    /// <summary>
    /// The server's radio ports, from its latest list; empty until it has sent one. Kept while reconnecting, and
    /// replaced, never changed, so a new list means the server listed them again.
    /// </summary>
    public IReadOnlyList<AgwpePort> Ports => Volatile.Read(ref _ports);

    /// <summary>
    /// The version the server reports, such as <c>2005.127</c>; <see langword="null"/> until it has said. Dire Wolf
    /// reports a fixed version for compatibility, not its own.
    /// </summary>
    public string? ServerVersion => Volatile.Read(ref _version);

    /// <summary>Raised when the server lists its radio ports, on each connection. Raised on the reading thread.</summary>
    public event EventHandler<AgwpePortsEventArgs>? PortsReceived;

    private protected override void AgwpePortsRead(IReadOnlyList<AgwpePort> ports)
    {
        Volatile.Write(ref _ports, ports);
        try
        {
            PortsReceived?.Invoke(this, new AgwpePortsEventArgs(ports));
        }
        catch (Exception)
        {
            // A handler's failure mustn't end the read.
        }
    }

    private protected override void AgwpeVersionRead(string version) => Volatile.Write(ref _version, version);

    private static AgwpeOptions Validated(AgwpeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return options;
    }
}

/// <summary>The radio ports an AGWPE server listed: see <see cref="AgwpeConnection.PortsReceived"/>.</summary>
public sealed class AgwpePortsEventArgs : EventArgs
{
    internal AgwpePortsEventArgs(IReadOnlyList<AgwpePort> ports) => Ports = ports;

    /// <summary>The ports, in the server's order.</summary>
    public IReadOnlyList<AgwpePort> Ports { get; }
}
