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
}
