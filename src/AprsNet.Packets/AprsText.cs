using System.Text;
using System.Text.Unicode;

namespace AprsNet.Packets;

/// <summary>Free text in a packet (comments, status text). APRS has no text encoding, so this is a best guess.</summary>
internal static class AprsText
{
    /// <summary>UTF-8 when the bytes are valid UTF-8, otherwise one character per byte (Latin-1).</summary>
    public static string Decode(ReadOnlySpan<byte> bytes) =>
        Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : Encoding.Latin1.GetString(bytes);
}
