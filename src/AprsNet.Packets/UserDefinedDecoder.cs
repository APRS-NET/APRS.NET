namespace AprsNet.Packets;

/// <summary>Decodes the header of user-defined data (APRS101 ch. 18); the data itself is the sending program's own.</summary>
internal static class UserDefinedDecoder
{
    /// <summary>Decodes a user-defined packet; <paramref name="offset"/> is where the information field starts in Raw.</summary>
    public static UserDefinedPayload? Decode(ReadOnlyMemory<byte> information, int offset, List<ParseIssue> issues)
    {
        if (information.Length < 3)
        {
            issues.Add(new ParseIssue(ParseIssueCode.InvalidUserDefined, ParseIssueSeverity.Error, offset));
            return null;
        }

        var span = information.Span;
        var data = information[3..];
        return new UserDefinedPayload
        {
            UserId = (char)span[1],
            UserPacketType = (char)span[2],
            DataBytes = data,
            Data = AprsText.Decode(data.Span),
        };
    }
}
