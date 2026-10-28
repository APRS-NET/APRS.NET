namespace AprsNet.Kiss;

/// <summary>Settings for <see cref="AgwpeConnection"/>.</summary>
/// <remarks>
/// A connection reads the settings once, when it is created, so one instance can be reused for several.
/// <see cref="TransmitLimit"/> is the exception: when it is set, every connection given these options shares that limit.
/// </remarks>
public sealed class AgwpeOptions
{
    /// <summary>AGWPE's port: 8000, as Dire Wolf and SoundModem use.</summary>
    public const int DefaultPort = 8000;

    /// <summary>The AGWPE server's host name or address. Defaults to <c>localhost</c>.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>The AGWPE server's TCP port. Defaults to <see cref="DefaultPort"/>.</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>
    /// The radio port, 0 to 15, counted from 0 (the server's <c>Port1</c> is 0): frames are sent there, and only frames
    /// heard there are read. Defaults to 0.
    /// </summary>
    public int TncPort { get; set; }

    /// <summary>
    /// Whether to read frames heard on every radio port, not only <see cref="TncPort"/>: <c>ReadPortFramesAsync</c>
    /// says which port each came from, and <c>SendToPortAsync</c> sends on any. Defaults to <see langword="false"/>.
    /// </summary>
    public bool AllPorts { get; set; }

    /// <summary>How long to wait for the TCP connection. Defaults to 10 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a server that went away without closing the connection can go unnoticed, found by TCP keepalives as for
    /// <see cref="KissTcpOptions.DeadLinkTimeout"/>. Defaults to 60 seconds; <see cref="Timeout.InfiniteTimeSpan"/>
    /// turns keepalives off.
    /// </summary>
    public TimeSpan DeadLinkTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long to wait before the first reconnect; each failed attempt doubles it, up to <see cref="MaxReconnectDelay"/>.
    /// Defaults to 5 seconds.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The longest wait between reconnect attempts. A connection that stayed up at least this long starts again from
    /// <see cref="ReconnectDelay"/>. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The clock for reconnect waits and the default transmit limit. Tests can pass a fake one.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// How fast frames may be sent. <see langword="null"/>, the default, gives each connection a new limit with the
    /// default settings on <see cref="TimeProvider"/>; set one to share it, or <see cref="KissTransmitLimit.Off"/> for
    /// none.
    /// </summary>
    public KissTransmitLimit? TransmitLimit { get; set; }

    /// <summary>
    /// How long the server may send nothing at all before <see cref="KissConnection.Warning"/> reports
    /// <see cref="KissWarning.Quiet"/>; <see langword="null"/>, the default, never does. Pick longer than the channel is
    /// ever quiet, since a quiet channel looks the same.
    /// </summary>
    public TimeSpan? QuietWarning { get; set; }

    /// <summary>Throws <see cref="ArgumentException"/> if a setting is unusable.</summary>
    public void Validate() => ToTcp().Validate();

    /// <summary>The same settings for the TCP connection underneath.</summary>
    internal KissTcpOptions ToTcp() =>
        new()
        {
            Host = Host,
            Port = Port,
            TncPort = TncPort,
            AllPorts = AllPorts,
            ConnectTimeout = ConnectTimeout,
            DeadLinkTimeout = DeadLinkTimeout,
            ReconnectDelay = ReconnectDelay,
            MaxReconnectDelay = MaxReconnectDelay,
            TimeProvider = TimeProvider,
            TransmitLimit = TransmitLimit,
            QuietWarning = QuietWarning,
        };
}
