using System.Globalization;
using System.Text;

namespace AprsNet.AprsIsCli;

internal static class LineFormatter
{
    /// <summary>ISO 8601 UTC with milliseconds, for example <c>2026-10-11T12:00:00.123Z</c>.</summary>
    public static string Timestamp(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Makes a raw line safe to print: control characters (including ESC, which could drive the terminal) become <c>\xNN</c>.
    /// Capture files are not escaped; they keep every byte.
    /// </summary>
    public static string EscapeForConsole(string line)
    {
        StringBuilder? escaped = null;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c < 0x20 || (c >= 0x7F && c <= 0x9F))
            {
                escaped ??= new StringBuilder(line.Length + 8).Append(line, 0, i);
                escaped.Append(CultureInfo.InvariantCulture, $"\\x{(int)c:X2}");
            }
            else
            {
                escaped?.Append(c);
            }
        }

        return escaped?.ToString() ?? line;
    }
}
