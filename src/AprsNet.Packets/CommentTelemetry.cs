using System.Globalization;

namespace AprsNet.Packets;

/// <summary>
/// Base-91 comment telemetry (APRS 1.2, OH7LZB): <c>|SSAABBCCDDEEFF|</c> at the end of a position comment, before
/// any <c>!DAO!</c> and Mic-E device code. Two base-91 characters each give the sequence, one to five analog values
/// (0 to 8280), and, after all five, the eight digital channels as one value whose lowest bit is channel 1.
/// </summary>
internal static class CommentTelemetry
{
    /// <summary>The largest value two base-91 characters may carry, as the format defines it.</summary>
    public const int MaxValue = 8280;

    /// <summary>
    /// Finds the last valid <c>|…|</c> block in a comment. Returns its start and length (pipes included), or
    /// <c>(-1, 0)</c> when there is none.
    /// </summary>
    public static (int Start, int Length) Find(ReadOnlySpan<byte> comment, out TelemetryReport? report)
    {
        report = null;
        for (var close = comment.LastIndexOf((byte)'|'); close > 0; close = comment[..close].LastIndexOf((byte)'|'))
        {
            var open = comment[..close].LastIndexOf((byte)'|');
            if (open < 0)
            {
                break;
            }

            if (TryDecode(comment[(open + 1)..close], out report))
            {
                return (open, close - open + 1);
            }
        }

        return (-1, 0);
    }

    /// <summary>The comment without its telemetry block, or the comment itself when there is none.</summary>
    public static ReadOnlySpan<byte> Strip(ReadOnlySpan<byte> comment, out TelemetryReport? report)
    {
        var (start, length) = Find(comment, out report);
        return start < 0 ? comment : (byte[])[.. comment[..start], .. comment[(start + length)..]];
    }

    /// <summary>Writes a report as a <c>|…|</c> block, checking it fits the format.</summary>
    public static string Encode(TelemetryReport report)
    {
        var sequence = report.SequenceNumber switch
        {
            null when int.TryParse(report.Sequence, NumberStyles.None, CultureInfo.InvariantCulture, out var n) => n,
            null => throw new ArgumentException("Comment telemetry needs a SequenceNumber, 0 to 8280."),
            { } n => n,
        };
        if (report.Analog.Count is < 1 or > 5)
        {
            throw new ArgumentException("Comment telemetry has one to five analog values.");
        }

        if (report.Digital.Count is not (0 or 8))
        {
            throw new ArgumentException("Comment telemetry has all eight digital channels or none.");
        }

        if (report.Digital.Count == 8 && report.Analog.Count != 5)
        {
            throw new ArgumentException("Comment telemetry's digital channels follow all five analog values.");
        }

        if (report.Comment.Length > 0)
        {
            throw new ArgumentException("Comment telemetry has no comment of its own; use the position's comment.");
        }

        var text = new System.Text.StringBuilder("|");
        Append(text, sequence, "sequence");
        foreach (var value in report.Analog)
        {
            if (value is not { } v || v != Math.Floor(v))
            {
                throw new ArgumentException(
                    "Comment telemetry analog values are whole numbers 0 to 8280, none missing."
                );
            }

            Append(text, (int)Math.Min(v, int.MaxValue), "analog value");
        }

        if (report.Digital.Count == 8)
        {
            var bits = 0;
            for (var i = 0; i < 8; i++)
            {
                bits |= report.Digital[i] ? 1 << i : 0;
            }

            Append(text, bits, "digital value");
        }

        return text.Append('|').ToString();
    }

    /// <summary>Checks the readings a caller set against the decoded ones.</summary>
    public static string? Mismatch(TelemetryReport? e, TelemetryReport? a) =>
        (e is null) != (a is null)
            ? "the comment would be read as telemetry, or the telemetry does not survive encoding"
        : e is null || a is null ? null
        : (e.SequenceNumber ?? int.Parse(e.Sequence, CultureInfo.InvariantCulture)) != a.SequenceNumber
        || !e.Analog.SequenceEqual(a.Analog)
        || !e.Digital.SequenceEqual(a.Digital)
            ? "the telemetry does not survive encoding"
        : null;

    private static void Append(System.Text.StringBuilder text, int value, string what)
    {
        if (value is < 0 or > MaxValue)
        {
            throw new ArgumentException($"A comment telemetry {what} must be 0 to {MaxValue}.");
        }

        text.Append((char)(33 + (value / 91))).Append((char)(33 + (value % 91)));
    }

    private static bool TryDecode(ReadOnlySpan<byte> block, out TelemetryReport? report)
    {
        report = null;
        if (
            block.Length is < 4 or > 14
            || block.Length % 2 != 0
            || block.IndexOfAnyExceptInRange((byte)'!', (byte)'{') >= 0
        )
        {
            return false;
        }

        // Two characters from '!' to '{' reach at most 90 × 91 + 90 = 8280, the format's maximum, so no value can be
        // out of range once the characters are.
        var values = new int[block.Length / 2];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ((block[2 * i] - 33) * 91) + (block[(2 * i) + 1] - 33);
        }

        // Seven values are the sequence, five analog channels and the digital bits; fewer have no bits.
        var analogCount = Math.Min(values.Length - 1, 5);
        bool[] digital =
            values.Length == 7 ? [.. Enumerable.Range(0, 8).Select(bit => (values[6] & (1 << bit)) != 0)] : [];
        report = new TelemetryReport
        {
            Sequence = values[0].ToString(CultureInfo.InvariantCulture),
            SequenceNumber = values[0],
            Analog = [.. values.Skip(1).Take(analogCount).Select(v => (double?)v)],
            Digital = digital,
        };
        return true;
    }
}
