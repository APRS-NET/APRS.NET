using System.Diagnostics.CodeAnalysis;

namespace AprsNet.Packets.Is2;

/// <summary>
/// The modem a packet was received with, numbered as <c>ModemType</c> in aprsc's IS2 schema (<c>aprsis2.proto</c>).
/// The numbers go on the wire, so they never change; a value aprsc adds later can be cast to this type.
/// </summary>
/// <remarks>Experimental: IS2 is still a work in progress upstream, and this type may change or go.</remarks>
[Experimental(Is2Diagnostics.Id)]
public enum ReceptionModem
{
    /// <summary>Not known or not set: the same as giving no modem.</summary>
    Undefined = 0,

    /// <summary>AX.25 packet radio, such as 1200 or 9600 bit/s APRS through a TNC.</summary>
    Ax25 = 1,

    /// <summary>LoRa APRS, as LoRa trackers and igates send it.</summary>
    LoRaAprs = 2,

    /// <summary>FLARM aircraft collision avoidance beacons.</summary>
    Flarm = 3,

    /// <summary>D-Star DV mode data (D-PRS).</summary>
    DStar = 4,

    /// <summary>DMR GPS data.</summary>
    Dmr = 5,

    /// <summary>Yaesu System Fusion (C4FM).</summary>
    C4fm = 6,

    /// <summary>NXDN digital voice GPS data.</summary>
    Nxdn = 7,

    /// <summary>APCO P25 digital voice GPS data.</summary>
    P25 = 8,

    /// <summary>M17 digital voice GNSS data.</summary>
    M17 = 9,
}
