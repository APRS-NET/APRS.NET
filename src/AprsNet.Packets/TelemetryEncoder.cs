using System.Globalization;
using System.Text;

namespace AprsNet.Packets;

/// <summary>
/// Writes telemetry (APRS101 ch. 13): <c>T#</c> reports, and the <c>PARM.</c>, <c>UNIT.</c>, <c>EQNS.</c> and
/// <c>BITS.</c> definitions, which are messages addressed to the telemetry station. Parsed payloads are rebuilt from
/// the bytes they arrived with; new ones are written in APRS101's form.
/// </summary>
internal static class TelemetryEncoder
{
    /// <summary>APRS101 ch. 14: message text is at most 67 characters.</summary>
    private const int MaxMessageText = 67;

    /// <summary>
    /// Writes <c>T#sss,aaa,aaa,aaa,aaa,aaa,bbbbbbbb</c> and the comment. Fewer analog values are allowed, but then
    /// nothing may follow them: a receiver would read digital channels or a comment as another analog value.
    /// </summary>
    public static byte[] Encode(TelemetryReport report)
    {
        var info = new List<byte>(64) { (byte)'T', (byte)'#' };
        if (report.ValuesBytes is { } values)
        {
            info.AddRange(values.Span);
            info.AddRange(report.CommentBytes.Span);
            return [.. info];
        }

        var text = new StringBuilder(Sequence(report));
        if (report.Analog.Count is < 1 or > 5)
        {
            throw new ArgumentException("A telemetry report has one to five analog values.");
        }

        foreach (var value in report.Analog)
        {
            text.Append(',').Append(value is { } v ? Analog(v) : "");
        }

        if (report.Digital.Count > 0)
        {
            if (report.Digital.Count != 8)
            {
                throw new ArgumentException("A telemetry report has all eight digital channels or none.");
            }

            if (report.Analog.Count != 5)
            {
                throw new ArgumentException("Digital channels follow all five analog values; give five.");
            }

            text.Append(',');
            foreach (var bit in report.Digital)
            {
                text.Append(bit ? '1' : '0');
            }
        }
        else if (report.Comment.Length > 0)
        {
            throw new ArgumentException("A telemetry comment follows the eight digital channels; give them too.");
        }

        info.AddRange(Encoding.ASCII.GetBytes(text.ToString()));
        info.AddRange(AprsEncoder.TextBytes(ReadOnlyMemory<byte>.Empty, report.Comment, "comment"));
        return [.. info];
    }

    /// <summary>Writes a definition as a message to the telemetry station: <c>:N0QBF-11 :PARM.Battery,Temp</c>.</summary>
    public static byte[] Encode(TelemetryDefinition definition)
    {
        // A parsed definition keeps whatever addressee it arrived with; a new one goes to a callsign.
        var addressee = definition.Addressee;
        var parsed = !definition.TextBytes.IsEmpty;
        if (
            addressee.Length > 9
            || addressee.Contains(':', StringComparison.Ordinal)
            || (!parsed && (addressee.Length == 0 || !addressee.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
        )
        {
            throw new ArgumentException(
                "A telemetry definition is addressed to the telemetry station's callsign: 1 to 9 letters, digits "
                    + "or '-'."
            );
        }

        var info = new List<byte>(80);
        info.AddRange(Encoding.ASCII.GetBytes($":{addressee, -9}:"));
        if (parsed)
        {
            info.AddRange(definition.TextBytes.Span);
            return [.. info];
        }

        var text = definition.Kind switch
        {
            TelemetryDefinitionKind.ParameterNames => "PARM." + Labels(definition),
            TelemetryDefinitionKind.Units => "UNIT." + Labels(definition),
            TelemetryDefinitionKind.Equations => "EQNS." + Equations(definition),
            TelemetryDefinitionKind.BitSense => "BITS." + BitSense(definition),
            _ => throw new ArgumentException("Unknown telemetry definition kind."),
        };
        if (text.Length > MaxMessageText)
        {
            throw new ArgumentException(
                $"A telemetry definition is at most {MaxMessageText} characters; this one is {text.Length}. "
                    + "Shorten the names."
            );
        }

        info.AddRange(AprsEncoder.TextBytes(ReadOnlyMemory<byte>.Empty, text, "telemetry definition"));
        return [.. info];
    }

    /// <summary>Checks a report against the decoded one: the same sequence, values, channels and comment.</summary>
    public static string? Mismatch(TelemetryReport e, TelemetryReport a) =>
        a.Sequence != (e.ValuesBytes is null ? Sequence(e) : e.Sequence) ? "the sequence does not survive encoding"
        : !e.Analog.SequenceEqual(a.Analog) ? "the analog values do not survive encoding"
        : !e.Digital.SequenceEqual(a.Digital) ? "the digital channels do not survive encoding"
        : e.Comment != a.Comment ? "the comment does not survive encoding"
        : null;

    /// <summary>Checks a definition against the decoded one.</summary>
    public static string? Mismatch(TelemetryDefinition e, TelemetryDefinition a) =>
        e.Addressee != a.Addressee ? "the addressee does not survive encoding"
        : e.Kind != a.Kind ? "the kind does not survive encoding"
        : !e.Labels.SequenceEqual(a.Labels) ? "the labels do not survive encoding"
        : !e.Equations.SequenceEqual(a.Equations) ? "the equations do not survive encoding"
        : e.BitSense != a.BitSense || e.ProjectName != a.ProjectName
            ? "the bit sense or project name does not survive encoding"
        : null;

    /// <summary>The sequence to write: <see cref="TelemetryReport.Sequence"/> if set, otherwise the number as three digits.</summary>
    private static string Sequence(TelemetryReport report)
    {
        if (report.Sequence.Length > 0)
        {
            var isNumber = report.Sequence.Length is >= 1 and <= 9 && report.Sequence.All(char.IsAsciiDigit);
            if (report.Sequence != "MIC" && !isNumber)
            {
                throw new ArgumentException("A telemetry sequence is digits, or MIC.");
            }

            if (
                report.SequenceNumber is { } number
                && (!isNumber || int.Parse(report.Sequence, CultureInfo.InvariantCulture) != number)
            )
            {
                throw new ArgumentException("Sequence and SequenceNumber differ; set one of them.");
            }

            return report.Sequence;
        }

        return report.SequenceNumber switch
        {
            null => throw new ArgumentException("A telemetry report needs a SequenceNumber, 0 to 999."),
            < 0 or > 999 => throw new ArgumentException(
                "A telemetry sequence number is 0 to 999; wrap it with % 1000."
            ),
            { } n => n.ToString("000", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>A raw analog value: a whole number 0 to 999, as three digits.</summary>
    private static string Analog(double value) =>
        value is >= 0 and <= 999 && value == Math.Floor(value)
            ? ((int)value).ToString("000", CultureInfo.InvariantCulture)
            : throw new ArgumentException(
                "Telemetry analog values are whole numbers 0 to 999. Send a raw value and scale it to a reading "
                    + "with an EQNS definition."
            );

    private static string Labels(TelemetryDefinition definition)
    {
        if (definition.Labels.Count is < 1 or > 13)
        {
            throw new ArgumentException(
                "A PARM or UNIT definition has 1 to 13 labels: five analog channels, then eight digital."
            );
        }

        if (definition.Equations.Count > 0 || definition.BitSense is not null || definition.ProjectName is not null)
        {
            throw new ArgumentException("A PARM or UNIT definition carries labels only.");
        }

        if (definition.Labels.Any(label => label.Contains(',', StringComparison.Ordinal)))
        {
            throw new ArgumentException("Telemetry labels must not contain commas, which separate them.");
        }

        return string.Join(',', definition.Labels);
    }

    private static string Equations(TelemetryDefinition definition)
    {
        if (definition.Equations.Count is < 1 or > 5)
        {
            throw new ArgumentException("An EQNS definition has one to five equations, one per analog channel.");
        }

        if (definition.Labels.Count > 0 || definition.BitSense is not null || definition.ProjectName is not null)
        {
            throw new ArgumentException("An EQNS definition carries equations only.");
        }

        return string.Join(',', definition.Equations.SelectMany(e => new[] { e.A, e.B, e.C }).Select(Coefficient));

        static string Coefficient(double value) =>
            double.IsFinite(value)
                ? value.ToString("R", CultureInfo.InvariantCulture)
                : throw new ArgumentException("Telemetry equation coefficients must be finite numbers.");
    }

    private static string BitSense(TelemetryDefinition definition)
    {
        if (definition.BitSense is not { Length: 8 } bits || bits.AsSpan().IndexOfAnyExcept('0', '1') >= 0)
        {
            throw new ArgumentException(
                "A BITS definition needs BitSense: eight 0 or 1 characters, the 'on' state of each digital channel."
            );
        }

        if (definition.Labels.Count > 0 || definition.Equations.Count > 0)
        {
            throw new ArgumentException("A BITS definition carries the bit sense and a project name only.");
        }

        return definition.ProjectName is { } project ? $"{bits},{project}" : bits;
    }
}
