namespace AprsNet.Packets;

/// <summary>Text for <see cref="ParseIssueCode"/> values.</summary>
public static class ParseIssueCodeExtensions
{
    /// <summary>A short sentence describing the issue, for logs and tools.</summary>
    public static string Describe(this ParseIssueCode code) =>
        code switch
        {
            ParseIssueCode.MissingHeader => "No SOURCE>DESTINATION header before the first ':'.",
            ParseIssueCode.EmptySource => "The header has no source before the '>'.",
            ParseIssueCode.EmptyDestination => "The header has no destination after the '>'.",
            ParseIssueCode.EmptyPathEntry => "The path has an empty entry.",
            ParseIssueCode.TooManyPathEntries => $"The path has more than {AprsPacket.MaxPathEntries} entries.",
            ParseIssueCode.InvalidCallsign => "A header entry holds a space, a control byte or a non-ASCII byte.",
            ParseIssueCode.EmptyInformation => "The packet has no information field after the ':'.",
            ParseIssueCode.InformationTooLong =>
                $"The information field is longer than {AprsPacket.MaxInformationLength} bytes.",
            ParseIssueCode.InvalidTimestamp => "The timestamp is not DDHHMMz, DDHHMM/ or HHMMSSh with valid values.",
            ParseIssueCode.PositionTooShort => "The information field ends before the position does.",
            ParseIssueCode.InvalidLatitude => "The latitude is malformed or out of range.",
            ParseIssueCode.InvalidLongitude => "The longitude is malformed or out of range.",
            ParseIssueCode.InvalidSymbolTable => "The symbol table identifier is not valid.",
            ParseIssueCode.InvalidSymbolCode => "The symbol code is not a printable ASCII character.",
            ParseIssueCode.LowercaseHemisphere => "A position uses a lower-case hemisphere letter.",
            ParseIssueCode.InvalidAddressee => "The message has no 9-character addressee followed by ':'.",
            ParseIssueCode.InvalidTelemetryDefinition => "The telemetry definition's values cannot be read.",
            _ => code.ToString(),
        };
}
