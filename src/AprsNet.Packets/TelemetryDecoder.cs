using System.Globalization;
using System.Text;

namespace AprsNet.Packets;

/// <summary>
/// Decodes telemetry reports (APRS101 ch. 13). As aprs.fi and most decoders do, it accepts analog values that are
/// decimals, negative or above 255, fewer than five analog values, and reports without digital channels.
/// </summary>
internal static class TelemetryDecoder
{
    private const NumberStyles _styles =
        NumberStyles.AllowLeadingSign
        | NumberStyles.AllowDecimalPoint
        | NumberStyles.AllowLeadingWhite
        | NumberStyles.AllowTrailingWhite;

    /// <summary>Decodes a telemetry report; <paramref name="offset"/> is where the information field starts in Raw.</summary>
    public static TelemetryReport? Decode(ReadOnlyMemory<byte> information, int offset, List<ParseIssue> issues)
    {
        var body = information[2..];
        var at = offset + 2;
        string sequence;
        int? number = null;
        if (body.Span.StartsWith("MIC"u8))
        {
            // APRS101 shows T#MIC with no comma before the first value; a comma is accepted too.
            sequence = "MIC";
            var skip = body.Length > 3 && body.Span[3] == ',' ? 4 : 3;
            body = body[skip..];
            at += skip;
        }
        else
        {
            var comma = body.Span.IndexOf((byte)',');
            if (comma is < 1 or > 9 || body.Span[..comma].IndexOfAnyExceptInRange((byte)'0', (byte)'9') >= 0)
            {
                issues.Add(Error(at));
                return null;
            }

            sequence = Encoding.Latin1.GetString(body.Span[..comma]);
            number = int.Parse(sequence, CultureInfo.InvariantCulture);
            body = body[(comma + 1)..];
            at += comma + 1;
        }

        var analog = new List<double?>();
        var more = true;
        while (more && analog.Count < 5)
        {
            var comma = body.Span.IndexOf((byte)',');
            var field = comma < 0 ? body.Span : body.Span[..comma];
            if (field.IsEmpty)
            {
                analog.Add(null);
            }
            else if (
                double.TryParse(field, _styles, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            )
            {
                analog.Add(value);
            }
            else
            {
                issues.Add(Error(at));
                return null;
            }

            more = comma >= 0;
            body = more ? body[(comma + 1)..] : body[body.Length..];
            at += more ? comma + 1 : field.Length;
        }

        bool[] digital = [];
        if (!body.IsEmpty)
        {
            if (body.Length < 8 || body.Span[..8].IndexOfAnyExcept((byte)'0', (byte)'1') >= 0)
            {
                issues.Add(Error(at));
                return null;
            }

            digital = [.. body.Span[..8].ToArray().Select(b => b == '1')];
            body = body[8..];
        }

        return new TelemetryReport
        {
            Sequence = sequence,
            SequenceNumber = number,
            Analog = analog,
            Digital = digital,
            CommentBytes = body,
            Comment = AprsText.Decode(body.Span),
        };
    }

    private static ParseIssue Error(int offset) =>
        new(ParseIssueCode.InvalidTelemetry, ParseIssueSeverity.Error, offset);
}
