namespace AprsNet.Packets;

/// <summary>
/// What a <see cref="ParseIssue"/> is about. A stable vocabulary, also used as metric tag values: members may be added
/// but are never renamed or reused. <see cref="ParseIssueCodeExtensions.Describe"/> gives a sentence for people.
/// </summary>
public enum ParseIssueCode
{
    /// <summary>The line has no <c>SOURCE&gt;DESTINATION</c> header before the first <c>:</c>.</summary>
    MissingHeader,

    /// <summary>The header has nothing before the <c>&gt;</c>.</summary>
    EmptySource,

    /// <summary>The header has no destination after the <c>&gt;</c>.</summary>
    EmptyDestination,

    /// <summary>The path has an empty entry (two commas in a row, or a trailing comma).</summary>
    EmptyPathEntry,

    /// <summary>The path has more entries than any real packet needs.</summary>
    TooManyPathEntries,

    /// <summary>A header entry holds a space, a control byte or a byte above 0x7E.</summary>
    InvalidCallsign,

    /// <summary>There is nothing after the <c>:</c>.</summary>
    EmptyInformation,

    /// <summary>The information field is longer than any APRS packet should be; only its start is decoded.</summary>
    InformationTooLong,

    /// <summary>A timestamp is not <c>DDHHMMz</c>, <c>DDHHMM/</c> or <c>HHMMSSh</c> with valid values.</summary>
    InvalidTimestamp,

    /// <summary>The information field ends before the position does.</summary>
    PositionTooShort,

    /// <summary>The latitude is not <c>DDMM.hhN</c> or <c>S</c>, out of range, or not valid base-91.</summary>
    InvalidLatitude,

    /// <summary>The longitude is not <c>DDDMM.hhE</c> or <c>W</c>, out of range, or not valid base-91.</summary>
    InvalidLongitude,

    /// <summary>The symbol table is not <c>/</c>, <c>\</c>, an overlay character, or (compressed) <c>a</c>–<c>j</c>.</summary>
    InvalidSymbolTable,

    /// <summary>The symbol code is not a printable ASCII character.</summary>
    InvalidSymbolCode,

    /// <summary>A position uses a lower-case hemisphere letter (<c>n</c>, <c>s</c>, <c>e</c>, <c>w</c>); it is still decoded.</summary>
    LowercaseHemisphere,

    /// <summary>A message has no 9-character addressee followed by <c>:</c>.</summary>
    InvalidAddressee,

    /// <summary>A telemetry definition (<c>EQNS.</c> or <c>BITS.</c>) has values that cannot be read; it is kept without them.</summary>
    InvalidTelemetryDefinition,

    /// <summary>An object has no 9-character name followed by <c>*</c> (live) or <c>_</c> (killed) and a timestamp.</summary>
    InvalidObject,

    /// <summary>An item has no 3–9 character name followed by <c>!</c> (live) or <c>_</c> (killed).</summary>
    InvalidItem,

    /// <summary>A Mic-E destination is not six characters that each encode a latitude digit and a flag.</summary>
    InvalidMicEDestination,

    /// <summary>
    /// A telemetry report has no numeric or <c>MIC</c> sequence, an analog value that is not a number, or digital
    /// channels that are not eight <c>0</c>/<c>1</c> characters.
    /// </summary>
    InvalidTelemetry,

    /// <summary>
    /// A message addressee is not padded to 9 characters (or is padded past 9) before its colon; the message is still
    /// read.
    /// </summary>
    UnpaddedAddressee,

    /// <summary>A user-defined packet (<c>{</c>) is shorter than its user ID and packet type.</summary>
    InvalidUserDefined,

    /// <summary>A third-party packet (<c>}</c>) does not carry a packet with a <c>SOURCE&gt;DESTINATION:</c> header.</summary>
    InvalidThirdParty,

    /// <summary>Third-party packets are nested more deeply than the parser unwraps.</summary>
    ThirdPartyTooDeep,

    /// <summary>
    /// An object name shorter than 9 characters is not padded with spaces before its <c>*</c> or <c>_</c> (SvxLink's
    /// <c>EL-</c> nodes); the object is still read.
    /// </summary>
    UnpaddedObjectName,

    /// <summary>A <c>$</c> packet is not an NMEA sentence: <c>$</c>, an address of letters and digits, then a comma.</summary>
    InvalidNmeaSentence,

    /// <summary>An NMEA sentence's checksum doesn't match it; the sentence is still read.</summary>
    NmeaChecksumMismatch,

    /// <summary>An NMEA field can't be read (a time, number or coordinate); it is left out of the fix.</summary>
    InvalidNmeaField,

    /// <summary>A <c>?</c> packet is not a general query: <c>?</c>, a query type of letters or digits, then <c>?</c>.</summary>
    InvalidQuery,

    /// <summary>
    /// What follows a general query is not a target footprint (<c>lat,long,radius</c>); the query is still read, without
    /// its area.
    /// </summary>
    InvalidQueryFootprint,
}
