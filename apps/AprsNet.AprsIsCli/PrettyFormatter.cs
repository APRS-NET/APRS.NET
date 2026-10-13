using System.Globalization;
using System.Text;
using AprsNet.Packets;
using AprsNet.Symbols;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The <c>pretty</c> output format: one block per packet, a header line and then labelled, aligned fields. Colour is
/// optional (ANSI escape codes) and only used on a terminal.
/// </summary>
internal static class PrettyFormatter
{
    private const int _labelWidth = 10;
    private const string _indent = "          "; // lines up with the HH:mm:ss received time and two spaces

    /// <summary>A block of lines describing one packet, without a trailing newline.</summary>
    public static string Format(AprsPacket packet, DateTimeOffset received, bool color)
    {
        var style = new Style(color);
        var block = new StringBuilder();
        block.Append(style.Dim(received.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture))).Append("  ");
        if (packet.HasHeader)
        {
            block.Append(style.Bold(Safe(packet.Source.Value))).Append(" → ").Append(Safe(packet.Destination.Value));
            if (packet.Path.Count > 0)
            {
                block.Append(style.Dim("  via " + Safe(string.Join(",", packet.Path))));
            }
        }
        else
        {
            block.Append(style.Red("not a packet"));
        }

        var fields = new List<(string Label, string Value)>();
        switch (packet.Payload)
        {
            case PositionedPayload report:
                Position(fields, report, received, style);
                break;
            case StatusReport status:
                Status(fields, status, received, style);
                break;
            case MessagePayload message:
                Message(fields, message, style);
                break;
            case TelemetryDefinition definition:
                fields.Add(("telemetry", $"{PacketFormatter.Words(definition.Kind)} for {Safe(definition.Addressee)}"));
                fields.Add(
                    (
                        definition.Kind == TelemetryDefinitionKind.Equations ? "equations" : "values",
                        Safe(PacketFormatter.Values(definition))
                    )
                );
                break;
            case TelemetryReport telemetry:
                fields.Add(("telemetry", style.Bold($"#{Safe(telemetry.Sequence)}")));
                fields.Add(("analog", PacketFormatter.Analog(telemetry)));
                if (telemetry.Digital.Count > 0)
                {
                    fields.Add(("digital", PacketFormatter.Bits(telemetry)));
                }

                if (telemetry.Comment.Trim().Length > 0)
                {
                    fields.Add(("comment", style.Green($"\"{Safe(telemetry.Comment.Trim())}\"")));
                }

                break;
            default:
                fields.Add(
                    (
                        Name(packet.Type),
                        style.Dim(packet.Outcome == ParseOutcome.Malformed ? "could not be decoded" : "not decoded yet")
                    )
                );
                fields.Add(
                    (
                        "data",
                        style.Dim(
                            Safe(
                                packet.HasHeader
                                    ? Encoding.Latin1.GetString(packet.Information.Span)
                                    : packet.ToString()
                            )
                        )
                    )
                );
                break;
        }

        foreach (var issue in packet.Issues)
        {
            var text = $"{issue.Code.Describe()} (byte {issue.Offset})";
            fields.Add(
                issue.Severity == ParseIssueSeverity.Error
                    ? ("error", style.Red(text))
                    : ("warning", style.Yellow(text))
            );
        }

        foreach (var (label, value) in fields)
        {
            block.AppendLine().Append(_indent).Append(style.Cyan(label.PadRight(_labelWidth))).Append(value);
        }

        return block.ToString();
    }

    private static void Position(
        List<(string Label, string Value)> fields,
        PositionedPayload report,
        DateTimeOffset received,
        Style style
    )
    {
        switch (report)
        {
            case ObjectReport o:
                fields.Add(("object", style.Bold(Safe(o.Name)) + (o.IsLive ? "" : style.Red("  killed"))));
                break;
            case ItemReport i:
                fields.Add(("item", style.Bold(Safe(i.Name)) + (i.IsLive ? "" : style.Red("  killed"))));
                break;
        }

        var where = report.IsPositionUnknown
            ? "unknown"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{report.Position.Latitude:0.00000}  {report.Position.Longitude:0.00000}"
            );
        if (report.Position.Ambiguity > 0)
        {
            where += style.Dim($"  (ambiguous to {Ambiguity(report.Position.Ambiguity)})");
        }

        fields.Add(("position", style.Bold(where)));
        fields.Add(("symbol", $"{Safe(report.Symbol.ToString())}  {AprsSymbolCatalog.GetName(report.Symbol)}"));
        if (report.Timestamp is { } timestamp)
        {
            fields.Add(("time", Time(timestamp, received)));
        }

        if (report.Course is not null || report.SpeedKnots is not null)
        {
            var course = report.Course is { } c
                ? c.ToString("000", CultureInfo.InvariantCulture) + "°"
                : "course unknown";
            var speed = report.SpeedKnots is { } s
                ? string.Create(CultureInfo.InvariantCulture, $" at {s:0.#} kn ({s * 1.852:0.#} km/h)")
                : "";
            fields.Add((report.Symbol.Code == '_' ? "wind" : "course", course + speed));
        }

        if (report.AltitudeFeet is { } altitude)
        {
            fields.Add(
                (
                    "altitude",
                    string.Create(CultureInfo.InvariantCulture, $"{altitude:N0} ft ({altitude * 0.3048:N0} m)")
                )
            );
        }

        if (report.PowerHeightGain is { } phg)
        {
            fields.Add(
                (
                    "station",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{phg.PowerWatts} W, antenna {phg.HeightFeet} ft, {phg.GainDbi} dBi {Direction(phg.Directivity)}, range {phg.RangeMiles:0.#} mi"
                    )
                )
            );
        }

        if (report.RangeMiles is { } range)
        {
            fields.Add(("range", string.Create(CultureInfo.InvariantCulture, $"{range:0.#} mi")));
        }

        if (report.DirectionFindingStrength is { } dfs)
        {
            fields.Add(
                (
                    "DF",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"signal S{dfs.Strength}, antenna {dfs.HeightFeet} ft, {dfs.GainDbi} dBi {Direction(dfs.Directivity)}"
                    )
                )
            );
        }

        if (report.DirectionFinding is { } df)
        {
            fields.Add(
                (
                    "bearing",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{df.Bearing:000}°, quality {df.Quality}, {df.Hits} hits, {df.RangeMiles} mi{(df.IsFixedStation ? ", fixed station" : "")}"
                    )
                )
            );
        }

        if (report.CompressionType is { } type)
        {
            fields.Add(
                (
                    "GPS",
                    $"{(type.GpsFix == CompressedGpsFix.Current ? "current fix" : "old fix")}, {type.NmeaSource.ToString().ToUpperInvariant()}, {Name(type.Origin)}"
                )
            );
        }

        if (report.Datum is { } datum)
        {
            fields.Add(("datum", datum is 'W' or 'w' ? "WGS84 (extra precision)" : $"{datum} (extra precision)"));
        }

        if (report.Area is { } area)
        {
            fields.Add(("area", PacketFormatter.AreaText(area)));
        }

        if (report.Signpost is { } signpost)
        {
            fields.Add(("signpost", style.Bold(Safe(signpost))));
        }

        switch (report)
        {
            case PositionReport position:
                fields.Add(("messaging", position.IsMessagingCapable ? "yes" : "no"));
                break;
            case MicEReport mice:
                var message = PacketFormatter.MicEMessageName(mice.Message);
                fields.Add(("Mic-E", mice.Message == MicEMessage.Emergency ? style.Red(message) : message));
                fields.Add(("device", mice.Device ?? "not identified"));
                fields.Add(("fix", mice.IsCurrentFix ? "current" : "old"));
                fields.Add(("messaging", mice.IsMessagingCapable ? "yes" : "no"));
                if (mice.MaidenheadLocator is { } locator)
                {
                    fields.Add(("locator", locator));
                }

                break;
        }
        if (report.Comment.Trim().Length > 0)
        {
            fields.Add(("comment", style.Green($"\"{Safe(report.Comment.Trim())}\"")));
        }
    }

    private static void Status(
        List<(string Label, string Value)> fields,
        StatusReport status,
        DateTimeOffset received,
        Style style
    )
    {
        fields.Add(("status", style.Green($"\"{Safe(status.Text.Trim())}\"")));
        if (status.Timestamp is { } timestamp)
        {
            fields.Add(("time", Time(timestamp, received)));
        }

        if (status.MaidenheadLocator is { } locator)
        {
            fields.Add(("locator", style.Bold(locator)));
        }

        if (status.Symbol is { } symbol)
        {
            fields.Add(("symbol", $"{Safe(symbol.ToString())}  {AprsSymbolCatalog.GetName(symbol)}"));
        }

        if (status.BeamHeading is { } heading)
        {
            fields.Add(
                (
                    "beam",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{heading:000}°, {status.EffectiveRadiatedPowerWatts} W ERP"
                    )
                )
            );
        }
    }

    private static void Message(List<(string Label, string Value)> fields, MessagePayload message, Style style)
    {
        var group = message.BulletinGroup is { } g ? $" to group {g}" : "";
        fields.Add(
            message.Kind switch
            {
                MessageKind.Acknowledgement => ("ack", $"{message.MessageId} to {message.Addressee}"),
                MessageKind.Rejection => ("reject", $"{message.MessageId} to {message.Addressee}"),
                MessageKind.Bulletin => ("bulletin", $"{message.BulletinId}{group}"),
                MessageKind.Announcement => ("announce", $"{message.BulletinId}{group}"),
                MessageKind.NwsBulletin => ("NWS", message.Addressee),
                MessageKind.Query => ("query", $"{message.QueryType} to {message.Addressee}"),
                _ => ("to", message.Addressee),
            }
        );
        fields[^1] = (fields[^1].Label, style.Bold(Safe(fields[^1].Value)));
        if (message.Kind == MessageKind.Query)
        {
            if (message.QueryArgument is { } argument)
            {
                fields.Add(("about", Safe(argument)));
            }
        }
        else if (message.Kind is not (MessageKind.Acknowledgement or MessageKind.Rejection))
        {
            fields.Add(("text", style.Green($"\"{Safe(message.Text)}\"")));
        }

        if (message.Kind is not (MessageKind.Acknowledgement or MessageKind.Rejection) && message.MessageId is { } id)
        {
            fields.Add(("id", $"{Safe(id)}{(message.IsAckRequired ? " (ack expected)" : "")}"));
        }

        if (message.ReplyAck is { } replyAck)
        {
            fields.Add(("reply-ack", replyAck.Length > 0 ? $"also acknowledges {Safe(replyAck)}" : "supported"));
        }
    }

    private static string Time(AprsTimestamp timestamp, DateTimeOffset received) =>
        timestamp.ToDateTimeOffset(received) is { } time
            ? string.Create(CultureInfo.InvariantCulture, $"{time:d MMM HH:mm} UTC  ({timestamp})")
            : $"{timestamp} (sender's local time)";

    private static string Ambiguity(int digits) =>
        digits switch
        {
            1 => "0.1 minute",
            2 => "1 minute",
            3 => "10 minutes",
            _ => "1 degree",
        };

    private static string Direction(int? degrees) =>
        degrees is { } d ? "toward " + d.ToString("000", CultureInfo.InvariantCulture) + "°" : "omni";

    private static string Name<T>(T value)
        where T : struct, Enum
    {
        // CompressedOrigin.TncBText -> "tnc b text", PacketType.MicE -> "mic e": readable without a lookup table.
        var name = value.ToString();
        var words = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                words.Append(' ');
            }

            words.Append(char.ToLowerInvariant(name[i]));
        }

        return words.ToString();
    }

    // Packet text can hold control characters (ESC could drive the terminal); show them as \xNN like raw output does.
    private static string Safe(string text) => LineFormatter.EscapeForConsole(text);

    private readonly struct Style(bool color)
    {
        public string Bold(string text) => Wrap("1", text);

        public string Dim(string text) => Wrap("2", text);

        public string Red(string text) => Wrap("31", text);

        public string Green(string text) => Wrap("32", text);

        public string Yellow(string text) => Wrap("33", text);

        public string Cyan(string text) => Wrap("36", text);

        private string Wrap(string code, string text) => color ? $"\u001b[{code}m{text}\u001b[0m" : text;
    }
}
