using System.Runtime.InteropServices;

namespace AprsNet.Packets;

/// <summary>Finds the <see cref="UnrecognizedPart"/>s of a packet, after parsing.</summary>
internal static class UnrecognizedParts
{
    private static readonly UnrecognizedPart[] _none = [];

    public static IReadOnlyList<UnrecognizedPart> Find(
        List<PathEntry> path,
        List<int> pathStarts,
        int informationOffset,
        int informationLength,
        AprsPayload? payload
    )
    {
        List<UnrecognizedPart>? parts = null;
        void Add(UnrecognizedPartCode code, int offset, int length) =>
            (parts ??= []).Add(new UnrecognizedPart(code, offset, length));

        // The RF part of the path ends at the q-construct; after it, APRS-IS logins needn't be AX.25 addresses.
        for (var i = 0; i < path.Count && path[i].Kind != PathEntryKind.QConstruct; i++)
        {
            if (path[i].Kind == PathEntryKind.Station && !path[i].Station.IsAx25)
            {
                Add(UnrecognizedPartCode.UnknownPathEntry, pathStarts[i], path[i].Station.Value.Length);
            }
        }

        switch (payload)
        {
            case UserDefinedPayload:
                Add(UnrecognizedPartCode.UserDefinedData, informationOffset, informationLength);
                break;
            case WeatherReport { CommentBytes: var rest } when UnreadWeatherField(rest.Span) is > 0 and var length:
                Add(UnrecognizedPartCode.UnreadWeatherField, OffsetOf(rest), length);
                break;
            case PositionedPayload positioned and not MicEReport:
                if (
                    !positioned.IsCompressed
                    && positioned.ExtensionBytes.IsEmpty
                    && IsUnknownExtension(positioned.CommentBytes.Span)
                )
                {
                    Add(UnrecognizedPartCode.UnknownDataExtension, OffsetOf(positioned.CommentBytes), 7);
                }

                if (
                    positioned.Weather is not null
                    && UnreadWeatherField(positioned.WeatherRestBytes.Span) is > 0 and var unread
                )
                {
                    Add(UnrecognizedPartCode.UnreadWeatherField, OffsetOf(positioned.WeatherRestBytes), unread);
                }

                break;
        }

        return parts ?? (IReadOnlyList<UnrecognizedPart>)_none;
    }

    /// <summary>
    /// Whether the comment starts with seven bytes shaped like a data extension, which didn't decode or they wouldn't be
    /// in the comment: <c>PHG</c>, <c>RNG</c> or <c>DFS</c> and four bytes, or a course and speed (<c>ddd/ddd</c>)
    /// with at least four digits.
    /// </summary>
    private static bool IsUnknownExtension(ReadOnlySpan<byte> comment)
    {
        if (comment.Length < 7)
        {
            return false;
        }

        if (comment.StartsWith("PHG"u8) || comment.StartsWith("RNG"u8) || comment.StartsWith("DFS"u8))
        {
            return true;
        }

        var digits = 0;
        foreach (var b in comment[..7])
        {
            digits += b is >= (byte)'0' and <= (byte)'9' ? 1 : 0;
        }

        return comment[3] == (byte)'/' && digits >= 4;
    }

    /// <summary>
    /// How long the reading-shaped field at the start of what follows the weather readings is: a letter, then at least
    /// two digits, dots, dashes or spaces with a digit among them. Zero when there is none.
    /// </summary>
    private static int UnreadWeatherField(ReadOnlySpan<byte> rest)
    {
        if (rest.IsEmpty || !char.IsAsciiLetter((char)rest[0]))
        {
            return 0;
        }

        var end = 1;
        var digits = 0;
        while (end < rest.Length && rest[end] is (>= (byte)'0' and <= (byte)'9') or (byte)'.' or (byte)'-' or (byte)' ')
        {
            digits += char.IsAsciiDigit((char)rest[end]) ? 1 : 0;
            end++;
        }

        while (end > 1 && rest[end - 1] == (byte)' ')
        {
            end--;
        }

        return end >= 3 && digits > 0 ? end : 0;
    }

    /// <summary>Where a slice of the packet's bytes starts in Raw: every payload slice comes from that one array.</summary>
    private static int OffsetOf(ReadOnlyMemory<byte> slice) =>
        MemoryMarshal.TryGetArray(slice, out var segment) ? segment.Offset : 0;
}
