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
        if (info.Length < 18 || info[10] is not ((byte)'*' or (byte)'_'))
        {
            issues.Add(new ParseIssue(ParseIssueCode.InvalidObject, ParseIssueSeverity.Error, offset + 1));
            return null;
        }

        var report = new ObjectReport
        {
            Name = Encoding.Latin1.GetString(info.Slice(1, 9)).TrimEnd(' '),
            IsLive = info[10] == '*',
        };
        var badTimestamp = !AprsTimestamp.TryParse(info[11..], out var timestamp);
        report.Timestamp = badTimestamp ? null : timestamp;

        var decoded = PositionDecoder.TryDecodePosition(information, 18, offset, issues, report);
        if (badTimestamp)
        {
            // Objects must carry a timestamp, but a broken one in front of a good position only loses the time.
            var severity = decoded ? ParseIssueSeverity.Warning : ParseIssueSeverity.Error;
            issues.Insert(0, new ParseIssue(ParseIssueCode.InvalidTimestamp, severity, offset + 11));
        }

        return decoded ? report : null;
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

        var report = new ItemReport
        {
            Name = Encoding.Latin1.GetString(info.Slice(1, nameLength)),
            IsLive = info[1 + nameLength] == '!',
        };
        return PositionDecoder.TryDecodePosition(information, nameLength + 2, offset, issues, report) ? report : null;
    }
}
