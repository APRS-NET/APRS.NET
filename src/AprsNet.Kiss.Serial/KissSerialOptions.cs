namespace AprsNet.Kiss;

/// <summary>Settings for <see cref="KissSerialConnection"/>.</summary>
/// <remarks>
/// A connection reads the settings once, when it is created, so one instance can be reused for several.
/// <see cref="TransmitLimit"/> is the exception: when it is set, every connection given these options shares that
/// limit.
/// </remarks>
public sealed class KissSerialOptions
{
    /// <summary>The usual speed of a USB KISS TNC's serial port, such as the MFJ TNC-X: 9600 baud.</summary>
    public const int DefaultBaudRate = 9600;

    /// <summary>
    /// The serial port the TNC is on, such as <c>/dev/ttyUSB0</c>, <c>/dev/ttyACM0</c> or <c>COM3</c>.
    /// <see cref="KissSerialConnection.GetPortNames"/> lists the ports present.
    /// </summary>
    public string PortName { get; set; } = "";

    /// <summary>The port's speed. Defaults to <see cref="DefaultBaudRate"/>; the NinoTNC uses 57600.</summary>
    public int BaudRate { get; set; } = DefaultBaudRate;

    /// <summary>
    /// Whether to raise DTR (Data Terminal Ready) while the port is open. Defaults to <see langword="true"/>, as a
    /// terminal program leaves it. Turn it off for a TNC that wires DTR to something else, such as an ESP32 board whose
    /// DTR and RTS lines reset it or put it in its bootloader.
    /// </summary>
    public bool DtrEnable { get; set; } = true;

    /// <summary>
    /// Whether to raise RTS (Request To Send) while the port is open. Defaults to <see langword="true"/>: some radios'
    /// built-in TNCs, the Kenwood TH-D72A among them, send nothing while RTS is low. Turn it off for a board that uses
    /// RTS to reset, such as an ESP32 TNC.
    /// </summary>
    public bool RtsEnable { get; set; } = true;

    /// <summary>
    /// The radio port on a multi-port TNC, 0 to 15: frames are sent there, and only frames heard there are read.
    /// Defaults to 0.
    /// </summary>
    public int TncPort { get; set; }

    /// <summary>
    /// How long to wait before the first attempt to open the port again after it failed or went away; each failed
    /// attempt doubles it, up to <see cref="MaxReconnectDelay"/>. Defaults to 2 seconds, so a TNC unplugged and plugged
    /// back in is found again quickly.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The longest wait between attempts. A connection that stayed open at least this long starts again from
    /// <see cref="ReconnectDelay"/>. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The clock for reconnect waits and the default transmit limit. Tests can pass a fake one.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// How fast frames may be sent. <see langword="null"/>, the default, gives each connection a new limit with the
    /// default settings on <see cref="TimeProvider"/>; set one to share it, or <see cref="KissTransmitLimit.Off"/> for
    /// none.
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
        ArgumentException.ThrowIfNullOrWhiteSpace(PortName, nameof(PortName));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(BaudRate, 0, nameof(BaudRate));
        ArgumentOutOfRangeException.ThrowIfNegative(TncPort, nameof(TncPort));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(TncPort, 15, nameof(TncPort));
        if (QuietWarning is { } quiet)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quiet, TimeSpan.Zero, nameof(QuietWarning));
        }
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ReconnectDelay, TimeSpan.Zero, nameof(ReconnectDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxReconnectDelay, ReconnectDelay, nameof(MaxReconnectDelay));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
    }

    /// <summary>The limit a connection created now uses.</summary>
    internal KissTransmitLimit CreateTransmitLimit() =>
        TransmitLimit ?? new KissTransmitLimit(timeProvider: TimeProvider);
}
