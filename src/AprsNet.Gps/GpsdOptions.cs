namespace AprsNet.Gps;

/// <summary>Settings for <see cref="GpsdConnection"/>.</summary>
public sealed class GpsdOptions
{
    /// <summary>gpsd's default port: 2947.</summary>
    public const int DefaultPort = 2947;

    /// <summary>The machine gpsd runs on. Defaults to <c>localhost</c>.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>gpsd's port. Defaults to <see cref="DefaultPort"/>.</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>
    /// The one GPS to read, as gpsd names it (<c>/dev/ttyACM0</c>), when gpsd has several; <see langword="null"/>, the
    /// default, reads them all.
    /// </summary>
    public string? Device { get; set; }

    /// <summary>How long to wait for gpsd to accept a connection. Defaults to 10 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long to wait before the first reconnect; each failed attempt doubles it, up to
    /// <see cref="MaxReconnectDelay"/>. Defaults to 2 seconds.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The longest wait between reconnect attempts. A connection that stayed up at least this long starts again from
    /// <see cref="ReconnectDelay"/>. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The clock for the reconnect waits. Defaults to the system clock.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Throws <see cref="ArgumentException"/> if a setting is unusable.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Host, nameof(Host));
        ArgumentOutOfRangeException.ThrowIfLessThan(Port, 1, nameof(Port));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, 65535, nameof(Port));
        if (Device is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(Device, nameof(Device));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ConnectTimeout, TimeSpan.Zero, nameof(ConnectTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ReconnectDelay, TimeSpan.Zero, nameof(ReconnectDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxReconnectDelay, ReconnectDelay, nameof(MaxReconnectDelay));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
    }
}
