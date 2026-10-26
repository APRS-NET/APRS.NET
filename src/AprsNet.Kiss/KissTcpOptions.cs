using System.Net.Sockets;

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

    /// <summary>
    /// Whether to read frames heard on every radio port of a multi-port TNC, such as a two-port serial TNC or Dire Wolf
    /// with two channels, not only <see cref="TncPort"/>: <c>ReadPortFramesAsync</c> says which port each came from,
    /// and <c>SendToPortAsync</c> sends on any. <see cref="TncPort"/> is still where <c>SendAsync</c> sends. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    public bool AllPorts { get; set; }

    /// <summary>How long to wait for the TCP connection. Defaults to 10 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a TNC that went away without closing the connection can go unnoticed: its host lost power, or this
    /// computer left the network. TCP keepalives find it, and the TNC's host answers them even while the radio channel
    /// is quiet, so a quiet channel is never mistaken for a dead link. Defaults to 60 seconds;
    /// <see cref="Timeout.InfiniteTimeSpan"/> turns keepalives off.
    /// </summary>
    public TimeSpan DeadLinkTimeout { get; set; } = TimeSpan.FromSeconds(60);

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

    /// <summary>
    /// How long the TNC may send nothing at all before <see cref="KissConnection.Warning"/> reports
    /// <see cref="KissWarning.Quiet"/>; <see langword="null"/>, the default, never does. For a TNC whose port stays
    /// open while it is switched off, such as a radio's built-in TNC behind a USB chip powered from the computer. Pick
    /// longer than the channel is ever quiet, since a quiet channel looks the same.
    /// </summary>
    public TimeSpan? QuietWarning { get; set; }

    /// <summary>Throws <see cref="ArgumentException"/> if a setting is unusable.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Host, nameof(Host));
        ArgumentOutOfRangeException.ThrowIfLessThan(Port, 1, nameof(Port));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, 65535, nameof(Port));
        ArgumentOutOfRangeException.ThrowIfNegative(TncPort, nameof(TncPort));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(TncPort, 15, nameof(TncPort));
        if (QuietWarning is { } quiet)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quiet, TimeSpan.Zero, nameof(QuietWarning));
        }
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ConnectTimeout, TimeSpan.Zero, nameof(ConnectTimeout));
        if (DeadLinkTimeout != Timeout.InfiniteTimeSpan)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(
                DeadLinkTimeout,
                TimeSpan.FromSeconds(2),
                nameof(DeadLinkTimeout)
            );
        }
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ReconnectDelay, TimeSpan.Zero, nameof(ReconnectDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxReconnectDelay, ReconnectDelay, nameof(MaxReconnectDelay));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
    }

    /// <summary>
    /// Turns on TCP keepalives so a dead link fails within <see cref="DeadLinkTimeout"/>: idle for half of it, then
    /// three probes spread over the other half.
    /// </summary>
    internal static void SetKeepAlive(Socket socket, TimeSpan deadLinkTimeout)
    {
        if (deadLinkTimeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        var idle = Math.Max(1, (int)(deadLinkTimeout.TotalSeconds / 2));
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, idle);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, Math.Max(1, idle / 3));
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
    }

    /// <summary>The limit a client created now uses.</summary>
    internal KissTransmitLimit CreateTransmitLimit() =>
        TransmitLimit ?? new KissTransmitLimit(timeProvider: TimeProvider);
}
