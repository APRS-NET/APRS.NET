namespace AprsNet.Packets;

/// <summary>Text for <see cref="UnrecognizedPartCode"/> values.</summary>
public static class UnrecognizedPartCodeExtensions
{
    /// <summary>A short sentence describing the part, for logs and tools.</summary>
    public static string Describe(this UnrecognizedPartCode code) =>
        code switch
        {
            UnrecognizedPartCode.UnknownDataExtension => "A data extension after the position doesn't decode.",
            UnrecognizedPartCode.UnknownPathEntry => "A path entry before the q-construct isn't an AX.25 address.",
            UnrecognizedPartCode.UnreadWeatherField => "The weather readings stop at a reading that can't be read.",
            UnrecognizedPartCode.UserDefinedData => "The user-defined data is in a format this library doesn't decode.",
            _ => code.ToString(),
        };
}
