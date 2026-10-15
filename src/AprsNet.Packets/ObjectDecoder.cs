using System.Text;

namespace AprsNet.Packets;

/// <summary>Decodes objects and items (APRS101 ch. 11), which carry the same position body as position reports.</summary>
internal static class ObjectDecoder
{
    /// <summary>
    /// Decodes an object, <c>;NAME_____*DDHHMMz</c> then a position; <paramref name="offset"/> is where the information
    /// field starts in Raw.
    /// </summary>
    public static ObjectReport? DecodeObject(ReadOnlyMemory<byte> information, int offset, List<ParseIssue> issues)
    {
        var info = information.Span;
        var nameLength = 9;
        if (info.Length < 18 || info[10] is not ((byte)'*' or (byte)'_'))
        {
            // SvxLink and others send names shorter than 9 without padding (;EL-N0CALL*...). Accept a shorter name
            // only when a valid timestamp and position follow it, so a broken object is still reported as one.
            nameLength = UnpaddedNameLength(information, offset);
            if (nameLength == 0)
            {
                issues.Add(new ParseIssue(ParseIssueCode.InvalidObject, ParseIssueSeverity.Error, offset + 1));
                return null;
            }

            issues.Add(new ParseIssue(ParseIssueCode.UnpaddedObjectName, ParseIssueSeverity.Warning, offset + 1));
        }

        var marker = 1 + nameLength;
        var badTimestamp = !AprsTimestamp.TryParse(info[(marker + 1)..], out var timestamp);
        var fields = new PositionFields { Timestamp = badTimestamp ? null : timestamp };
        var decoded = PositionDecoder.TryDecodePosition(information, marker + 8, offset, issues, fields);
        if (badTimestamp)
        {
            // Objects must carry a timestamp, but a broken one in front of a good position only loses the time.
            var severity = decoded ? ParseIssueSeverity.Warning : ParseIssueSeverity.Error;
            issues.Insert(0, new ParseIssue(ParseIssueCode.InvalidTimestamp, severity, offset + marker + 1));
        }

        return decoded
            ? new ObjectReport(fields)
            {
                Name = Encoding.Latin1.GetString(info.Slice(1, nameLength)).TrimEnd(' '),
                IsLive = info[marker] == '*',
            }
            : null;
    }

    /// <summary>
    /// The length of an unpadded object name (1 to 8) that a live or killed marker, a valid timestamp and a valid
    /// position follow, longest first; 0 when there is none.
    /// </summary>
    private static int UnpaddedNameLength(ReadOnlyMemory<byte> information, int offset)
    {
        var info = information.Span;
        for (var length = Math.Min(8, info.Length - 18 + 8); length >= 1; length--)
        {
            var marker = 1 + length;
            if (
                info[marker] is (byte)'*' or (byte)'_'
                && AprsTimestamp.TryParse(info[(marker + 1)..], out _)
                && PositionDecoder.TryDecodePosition(information, marker + 8, offset, [], new PositionFields())
            )
            {
                return length;
            }
        }

        return 0;
    }

    /// <summary>
    /// Decodes an item, <c>)NAME!</c> (live) or <c>)NAME_</c> (killed) with a 3–9 character name, then a position.
    /// </summary>
    public static ItemReport? DecodeItem(ReadOnlyMemory<byte> information, int offset, List<ParseIssue> issues)
    {
        var info = information.Span;
        var nameLength = info[1..Math.Min(info.Length, 11)].IndexOfAny((byte)'!', (byte)'_');
        if (nameLength is < 3 or > 9)
        {
            issues.Add(new ParseIssue(ParseIssueCode.InvalidItem, ParseIssueSeverity.Error, offset + 1));
            return null;
        }

        var fields = new PositionFields();
        return PositionDecoder.TryDecodePosition(information, nameLength + 2, offset, issues, fields)
            ? new ItemReport(fields)
            {
                Name = Encoding.Latin1.GetString(info.Slice(1, nameLength)),
                IsLive = info[1 + nameLength] == '!',
            }
            : null;
    }
}
