namespace AprsNet.Kiss;

/// <summary>Settings for <see cref="KissTcpClient"/> and <see cref="KissTcpConnection"/>.</summary>
/// <remarks>
/// A client or connection reads the settings once, when it is created, so one instance can be reused for several.
/// <see cref="TransmitLimit"/> is the exception: when it is set, every client given these options shares that limit.
/// </remarks>
public sealed class KissTcpOptions
{
    /// <summary>The TNC's host name or address. Defaults to <c>localhost</c>, where Dire Wolf usually runs.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>The TNC's KISS TCP port. Defaults to <see cref="KissTcpClient.DefaultPort"/> (8001, Dire Wolf's).</summary>
    public int Port { get; set; } = KissTcpClient.DefaultPort;

    /// <summary>
    /// The radio port on a multi-port TNC, 0 to 15: frames are sent there, and only frames heard there are read.
    /// Defaults to 0.
    /// </summary>
    public int TncPort { get; set; }

    /// <summary>How long to wait for the TCP connection. Defaults to 10 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// <see cref="KissTcpConnection"/>: how long to wait before the first reconnect; each failed attempt doubles it, up
    /// to <see cref="MaxReconnectDelay"/>. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <see cref="KissTcpConnection"/>: the longest wait between reconnect attempts. A connection that stayed up at
    /// least this long starts again from <see cref="ReconnectDelay"/>. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The clock for reconnect waits and the default transmit limit. Tests can pass a fake one.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// How fast frames may be sent. <see langword="null"/>, the default, gives each client a new limit with the default
    /// settings on <see cref="TimeProvider"/>; set one to share it, or <see cref="KissTransmitLimit.Off"/> for none.
    /// </summary>
    public KissTransmitLimit? TransmitLimit { get; set; }

    /// <summary>Throws <see cref="ArgumentException"/> if a setting is unusable.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Host, nameof(Host));
        ArgumentOutOfRangeException.ThrowIfLessThan(Port, 1, nameof(Port));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, 65535, nameof(Port));
        ArgumentOutOfRangeException.ThrowIfNegative(TncPort, nameof(TncPort));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(TncPort, 15, nameof(TncPort));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ConnectTimeout, TimeSpan.Zero, nameof(ConnectTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ReconnectDelay, TimeSpan.Zero, nameof(ReconnectDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxReconnectDelay, ReconnectDelay, nameof(MaxReconnectDelay));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
    }

    /// <summary>The limit a client created now uses.</summary>
    internal KissTransmitLimit CreateTransmitLimit() =>
        TransmitLimit ?? new KissTransmitLimit(timeProvider: TimeProvider);
}
