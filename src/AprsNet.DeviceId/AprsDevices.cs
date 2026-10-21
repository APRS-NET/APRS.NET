using AprsNet.Packets;

namespace AprsNet.DeviceId;

/// <summary>
/// Identifies the device or program that sent a packet, from the aprs-deviceid list
/// (<see href="https://github.com/aprsorg/aprs-deviceid"/>): by its destination (TOCALL), or for Mic-E by the codes
/// around its comment.
/// </summary>
/// <remarks>
/// The list is built into this package (see <see cref="ListCommit"/> and <see cref="ListDate"/>), so lookups need no
/// file or network access. To use a newer list without a new package, load it as an <see cref="AprsDeviceList"/>. Its data is licensed under CC BY-SA 2.0 by its maintainers, Heikki Hannikainen, OH7LZB, and
/// contributors; see the package's LICENSE.md. In TOCALL patterns <c>?</c> stands for any character, <c>n</c> for a
/// digit and a final <c>*</c> for any characters; when several match, the one with the most literal characters wins.
/// </remarks>
public static partial class AprsDevices
{
    /// <summary>The aprs-deviceid commit the built-in list was taken from.</summary>
    public static string ListCommit => _listCommit;

    /// <summary>The date of the aprs-deviceid commit the built-in list was taken from.</summary>
    public static DateOnly ListDate => _listDate;

    /// <summary>
    /// When the built-in list was generated: <see cref="AprsDeviceList.GeneratedAt"/> of <see cref="AprsDeviceList.BuiltIn"/>.
    /// </summary>
    public static DateTimeOffset ListGeneratedAt => _listGeneratedAt;

    internal static AprsDeviceList BuiltInList => BuiltIn.List;

    /// <inheritdoc cref="AprsDeviceList.Identify(AprsPacket)"/>
    public static AprsDevice? Identify(AprsPacket packet) => BuiltInList.Identify(packet);

    /// <inheritdoc cref="AprsDeviceList.FromTocall(string)"/>
    public static AprsDevice? FromTocall(string destination) => BuiltInList.FromTocall(destination);

    // Made on first use, once the generated tables in the other part of this class are set: the order of field
    // initializers across the parts of a partial class is not defined.
    private static class BuiltIn
    {
        internal static readonly AprsDeviceList List = new(
            _tocalls,
            _tocallPatterns,
            _micE,
            _micELegacy,
            _listGeneratedAt,
            _listCommit
        );
    }
}
