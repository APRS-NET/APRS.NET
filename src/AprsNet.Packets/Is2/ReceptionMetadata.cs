using System.Diagnostics.CodeAnalysis;

namespace AprsNet.Packets.Is2;

/// <summary>
/// How a packet was heard on the air: what the receiver measured, and the channel it listened on. IS2 servers (aprsc
/// 3.0) carry these values with the packet, for coverage maps and network analysis, without changing the packet. Each
/// value is <see langword="null"/> (or <see cref="ReceptionModem.Undefined"/>) when the receiver doesn't know it.
/// </summary>
/// <remarks>
/// <para>
/// A TNC that speaks KISS or AGWPE reports no signal strength, so for those only the modem, bit rate and frequency are
/// known, from the station's settings. RSSI and SNR come from receivers that measure them, such as LoRa radios.
/// </para>
/// <para>Experimental: IS2 is still a work in progress upstream, and this type may change or go.</para>
/// </remarks>
[Experimental(Is2Diagnostics.Id)]
public sealed record ReceptionMetadata
{
    /// <summary>
    /// The received signal strength, in dBm: negative, the closer to zero the stronger, from about -30 near by down to
    /// -130 or so for a packet barely decoded.
    /// </summary>
    public double? RssiDbm { get; init; }

    /// <summary>The signal-to-noise ratio, in dB. LoRa decodes below the noise, so it is often negative.</summary>
    public double? SnrDb { get; init; }

    /// <summary>The modem the packet was received with.</summary>
    public ReceptionModem Modem { get; init; }

    /// <summary>The bit rate of the transmission, in bit/s: 1200 or 9600 for packet radio, about 300 for LoRa APRS.</summary>
    public int? BitRate { get; init; }

    /// <summary>The radio frequency the packet was received on, in Hz: 144390000 for 144.390 MHz.</summary>
    public double? FrequencyHz { get; init; }
}
