namespace AprsNet.Packets;

/// <summary>What a <see cref="PathEntry"/> is.</summary>
public enum PathEntryKind
{
    /// <summary>A station: a digipeater, or the IGate or server login that passed the packet to APRS-IS.</summary>
    Station,

    /// <summary>
    /// A generic hop that names no station: <c>WIDEn-N</c>, <c>TRACEn-N</c>, <c>RELAY</c>, a regional alias such as
    /// <c>NCA2-1</c>, or an APRS-IS marker (<c>TCPIP</c>, <c>TCPXX</c>, <c>NOGATE</c>, <c>RFONLY</c>). Matched without
    /// regard to case.
    /// </summary>
    Alias,

    /// <summary>An APRS-IS q-construct such as <c>qAR</c>, which says how the packet entered APRS-IS.</summary>
    QConstruct,

    /// <summary>
    /// The APRS-IS server named after a <c>qAC</c>, <c>qAX</c>, <c>qAU</c> or <c>qAI</c> q-construct.
    /// </summary>
    Server,
}
