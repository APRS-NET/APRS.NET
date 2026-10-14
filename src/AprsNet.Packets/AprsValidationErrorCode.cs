namespace AprsNet.Packets;

/// <summary>
/// Why input a person typed was refused, carried by <see cref="AprsValidationException.Code"/> so an app can show the
/// error in the reader's language without matching English text. A stable vocabulary: members may be added but are
/// never renamed, removed or reused.
/// </summary>
public enum AprsValidationErrorCode
{
    /// <summary>
    /// A callsign is empty or too long: 1 to 9 characters for a packet header, an APRS-IS login or a passcode's base
    /// callsign, and 1 to 6 before the SSID for a sending station (<c>PacketAddress</c>).
    /// </summary>
    CallsignInvalidLength,

    /// <summary>
    /// A callsign holds a character it cannot: a space, a header separator (<c>&gt;</c>, <c>:</c>, <c>,</c>,
    /// <c>*</c>) or non-ASCII in a packet header; anything but letters, digits and <c>-</c> in an APRS-IS login; anything
    /// but letters and digits in a passcode's base callsign. <see cref="AprsValidationException.Index"/> and
    /// <see cref="AprsValidationException.Character"/> say which.
    /// </summary>
    CallsignInvalidCharacter,

    /// <summary>A digipeater path entry is empty or longer than 9 characters (not counting a trailing <c>*</c>).</summary>
    PathEntryInvalidLength,

    /// <summary>
    /// A digipeater path entry holds a space, a header separator or a non-ASCII character.
    /// <see cref="AprsValidationException.Index"/> and <see cref="AprsValidationException.Character"/> say which.
    /// </summary>
    PathEntryInvalidCharacter,

    /// <summary>
    /// The software name sent with the APRS-IS login is empty or holds whitespace or a control character; for the
    /// latter <see cref="AprsValidationException.Index"/> and <see cref="AprsValidationException.Character"/> say which.
    /// </summary>
    SoftwareNameInvalid,

    /// <summary>
    /// The software version sent with the APRS-IS login is empty or holds whitespace or a control character; for the
    /// latter <see cref="AprsValidationException.Index"/> and <see cref="AprsValidationException.Character"/> say which.
    /// </summary>
    SoftwareVersionInvalid,

    /// <summary>
    /// An APRS-IS filter holds a control character such as a line break, which would end the command early.
    /// <see cref="AprsValidationException.Index"/> and <see cref="AprsValidationException.Character"/> say which.
    /// </summary>
    FilterControlCharacter,

    /// <summary>A server address has no host name or IP address.</summary>
    ServerHostEmpty,

    /// <summary>
    /// A server address is not <c>host</c>, <c>host:port</c> or <c>[ipv6]:port</c>: an unclosed bracket, text after
    /// the bracket that is not <c>:port</c>, an IPv6 address without brackets, or whitespace inside the host.
    /// </summary>
    ServerHostInvalid,

    /// <summary>A server port is not a whole number.</summary>
    ServerPortInvalid,

    /// <summary>A server port is outside 1 to 65535.</summary>
    ServerPortOutOfRange,

    /// <summary>
    /// A sending station's SSID, after the <c>-</c>, is empty, longer than 2 characters, or not letters and digits.
    /// <see cref="AprsValidationException.Index"/> points at the first character after the <c>-</c>.
    /// </summary>
    CallsignInvalidSsid,

    /// <summary>
    /// A destination is not a TOCALL: 1 to 6 upper-case letters or digits, optionally followed by an SSID from 0 to 15.
    /// </summary>
    DestinationInvalid,

    /// <summary>A digipeater path has more than 10 entries.</summary>
    PathTooLong,
}
