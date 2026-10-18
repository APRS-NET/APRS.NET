namespace AprsNet.Packets;

/// <summary>
/// What an <see cref="UnrecognizedPart"/> is. A stable vocabulary, also used as metric and capture tag values: members
/// may be added but are never renamed or reused. <see cref="UnrecognizedPartCodeExtensions.Describe"/> gives a sentence
/// for people.
/// </summary>
public enum UnrecognizedPartCode
{
    /// <summary>
    /// After an uncompressed position, seven bytes shaped like a data extension that don't decode: <c>PHG</c>,
    /// <c>RNG</c> or <c>DFS</c> with values that can't be read (<c>PHG1-220</c>), or a course and speed that can't
    /// (<c>36O/012</c>). They are left in the comment.
    /// </summary>
    UnknownDataExtension,

    /// <summary>
    /// An entry in the RF part of the path, before any q-construct, that no AX.25 frame could carry: more than six
    /// characters before the SSID, or an SSID that isn't 0 to 15. A digipeater could not have put it there.
    /// </summary>
    UnknownPathEntry,

    /// <summary>
    /// Weather readings that stop at something shaped like a reading: a letter and digits this library doesn't read,
    /// such as <c>V117</c>, <c>X241</c> or <c>P7.000000000000001</c>. That part and what follows are left in the comment.
    /// </summary>
    UnreadWeatherField,

    /// <summary>The data of a user-defined packet (<c>{</c>), whose format this library doesn't decode.</summary>
    UserDefinedData,
}
