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
    /// How to switch the TNC into KISS mode, for a radio's built-in TNC or one with its own command line, such as
    /// <see cref="TncSetup.KenwoodThD72"/>; <see langword="null"/>, the default, for a TNC that only speaks KISS. The
    /// connection runs <see cref="TncSetup.EnterKiss"/> each time it opens the port, opens the port again when the TNC
    /// starts sending text instead of frames, and sends <see cref="TncSetup.LeaveKiss"/> when it closes.
    /// </summary>
    public TncSetup? Setup { get; set; }

    /// <summary>
    /// Whether the TNC prints each packet it hears as a TNC-2 text line instead of speaking KISS, as a radio does in its
    /// APRS mode (the Kenwood TH-D72's APRS12): the connection reads those lines as packets. Receive only: sending
    /// throws. The radio keeps working as an APRS station of its own, its display, beacons and message acks included,
    /// and needs no <see cref="Setup"/>. GPS sentences on the same port, from a radio with GPS PC output on, come
    /// through <see cref="KissConnection.NmeaSentenceReceived"/>; a packet line damaged where one ran into it is
    /// dropped and counted in <c>DroppedFrames</c>. Defaults to <see langword="false"/>, for KISS.
    /// </summary>
    public bool TextMode { get; set; }

    /// <summary>
    /// In <see cref="TextMode"/>, how long without a GPS sentence counts as the GPS gone, for
    /// <see cref="KissConnection.GpsPresenceChanged"/>. Defaults to 10 seconds; a TH-D72A sends a burst every 2.
    /// </summary>
    public TimeSpan GpsTimeout { get; set; } = TimeSpan.FromSeconds(10);

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
        if (TextMode && Setup is not null)
        {
            throw new ArgumentException(
                "A TNC read in text mode needs no setup: TextMode and Setup can't be used together.",
                nameof(TextMode)
            );
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(BaudRate, 0, nameof(BaudRate));
        ArgumentOutOfRangeException.ThrowIfNegative(TncPort, nameof(TncPort));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(TncPort, 15, nameof(TncPort));
        if (QuietWarning is { } quiet)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quiet, TimeSpan.Zero, nameof(QuietWarning));
        }
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(GpsTimeout, TimeSpan.Zero, nameof(GpsTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ReconnectDelay, TimeSpan.Zero, nameof(ReconnectDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxReconnectDelay, ReconnectDelay, nameof(MaxReconnectDelay));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
    }

    /// <summary>The limit a connection created now uses.</summary>
    internal KissTransmitLimit CreateTransmitLimit() =>
        TransmitLimit ?? new KissTransmitLimit(timeProvider: TimeProvider);
}
