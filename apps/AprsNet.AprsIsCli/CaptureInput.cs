using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AprsNet.AprsIsCli;

/// <summary>One input line: the packet as received, and the capture timestamp in front of it if there was one.</summary>
internal readonly record struct InputLine(string Line, DateTimeOffset? Received, int Number);

/// <summary>
/// Reads packet lines from capture files (<c>&lt;UTC timestamp&gt; &lt;raw line&gt;</c>, as written by
/// <c>listen --capture</c>) or plain raw lines. Text is Latin-1, so every byte reaches the parser unchanged.
/// </summary>
internal static partial class CaptureInput
{
    public static StreamReader OpenFile(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.Latin1);

    public static async IAsyncEnumerable<InputLine> ReadAsync(
        TextReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var number = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } text)
        {
            number++;
            yield return Split(text, number);
        }
    }

    /// <summary>Removes a leading capture timestamp, if the line has one.</summary>
    public static InputLine Split(string text, int number = 1)
    {
        var match = CaptureTimestamp().Match(text);
        if (
            match.Success
            && DateTimeOffset.TryParse(
                match.Groups[1].Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var received
            )
        )
        {
            return new InputLine(text[match.Length..], received, number);
        }

        return new InputLine(text, null, number);
    }

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z) ")]
    private static partial Regex CaptureTimestamp();
}
