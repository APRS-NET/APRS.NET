using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AprsNet.Gateway;

/// <summary>Settings for an <see cref="AprsIsServer"/>.</summary>
public sealed partial class AprsIsServerOptions
{
    /// <summary>
    /// The server's name, sent in the login response and keepalives and used by clients in q-constructs: 1 to 9 letters,
    /// digits or <c>-</c>, such as <c>APRSNET</c>.
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// Where to listen. The default is port 14580 (APRS-IS's user-defined filter port) on the loopback address only, so
    /// nothing is exposed to the network unless asked for.
    /// </summary>
    public IPEndPoint EndPoint { get; init; } = new(IPAddress.Loopback, 14580);

    /// <summary>The most clients connected at once; further connections are told the server is full and closed.</summary>
    public int MaxClients { get; init; } = 100;

    /// <summary>How long a new connection has to send its <c>user</c> login line.</summary>
    public TimeSpan LoginTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often each client gets a <c>#</c> keepalive comment, as APRS-IS servers send every 20 seconds.</summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Lines queued for one client. A client that falls this far behind is disconnected, as on APRS-IS, rather than
    /// holding packets for everyone else.
    /// </summary>
    public int ClientQueueLength { get; init; } = 1000;

    /// <summary>
    /// How far back a client sees on login: published packets this recent that pass its filter are sent first, so a
    /// map fills in at once. <see cref="TimeSpan.Zero"/>, the default, sends only new packets. At most
    /// <see cref="MaxHistoryPackets"/> are kept.
    /// </summary>
    public TimeSpan History { get; init; } = TimeSpan.Zero;

    /// <summary>The most packets <see cref="History"/> keeps, oldest dropped first.</summary>
    public int MaxHistoryPackets { get; init; } = 100_000;

    /// <summary>
    /// Where stations were last heard, for the filter terms that measure from a station: <c>m/km</c> (from the client's
    /// own callsign), <c>f/call/km</c> and <c>t/types/call/km</c>. The server records every published packet in it.
    /// Without one, those terms pass nothing.
    /// </summary>
    public StationStore? Stations { get; init; }

    /// <summary>The software name in the banner and keepalives.</summary>
    public string SoftwareName { get; init; } = "APRS.NET";

    /// <summary>The software version in the banner and keepalives.</summary>
    public string SoftwareVersion { get; init; } = DefaultVersion();

    /// <summary>The clock for login timeouts and keepalives.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Throws <see cref="ArgumentException"/> if any setting is unusable.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(EndPoint);
        ArgumentNullException.ThrowIfNull(TimeProvider);
        if (ServerName is null || !ServerNamePattern().IsMatch(ServerName))
        {
            throw new ArgumentException("Server name must be 1 to 9 letters, digits or '-'.", nameof(ServerName));
        }

        if (MaxClients < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxClients), MaxClients, "At least one client is needed.");
        }

        if (ClientQueueLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ClientQueueLength), ClientQueueLength, "Must be positive.");
        }

        if (History < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(History), History, "Must not be negative.");
        }

        if (MaxHistoryPackets < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxHistoryPackets), MaxHistoryPackets, "Must be positive.");
        }

        if (LoginTimeout <= TimeSpan.Zero || KeepAliveInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                LoginTimeout <= TimeSpan.Zero ? nameof(LoginTimeout) : nameof(KeepAliveInterval),
                "Must be positive."
            );
        }

        if (string.IsNullOrWhiteSpace(SoftwareName) || string.IsNullOrWhiteSpace(SoftwareVersion))
        {
            throw new ArgumentException("Software name and version are required.", nameof(SoftwareName));
        }
    }

    private static string DefaultVersion() =>
        typeof(AprsIsServerOptions)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    [GeneratedRegex("^[A-Za-z0-9-]{1,9}$")]
    private static partial Regex ServerNamePattern();
}
