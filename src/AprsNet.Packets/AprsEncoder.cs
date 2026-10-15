using System.Text;

namespace AprsNet.Packets;

/// <summary>
/// Writes payloads as APRS information fields, the reverse of the decoders. Text written by a caller is encoded as
/// UTF-8 (APRS 1.2); text that came from the parser keeps its original bytes. Values the format cannot carry throw
/// <see cref="ArgumentException"/>, and <see cref="AprsPacket.Create"/> then checks that the result decodes back to
/// the same payload.
/// </summary>
internal static class AprsEncoder
{
    /// <summary>APRS101 ch. 14: message text is at most 67 characters.</summary>
    private const int MaxMessageText = 67;

    public static byte[] Encode(AprsPayload payload) =>
        payload switch
        {
            StatusReport status => Status(status),
            MessagePayload message => Message(message),
            PositionReport position => PositionEncoder.Encode(position),
            MicEReport micE => MicEEncoder.Encode(micE),
            ObjectReport report => PositionEncoder.Encode(report),
            ItemReport item => PositionEncoder.Encode(item),
            WeatherReport weather => WeatherEncoder.Encode(weather),
            TelemetryReport telemetry => TelemetryEncoder.Encode(telemetry),
            TelemetryDefinition definition => TelemetryEncoder.Encode(definition),
            _ => throw new NotSupportedException(
                $"Encoding {payload.GetType().Name} is not supported yet; status reports, messages, position "
                    + "reports, Mic-E reports, objects, items, weather and telemetry are."
            ),
        };

    /// <summary>
    /// Checks the fields a caller set against the payload decoded from the encoded bytes; returns why they differ, or
    /// <see langword="null"/>.
    /// </summary>
    public static string? Mismatch(AprsPayload expected, AprsPayload? actual) =>
        (expected, actual) switch
        {
            (StatusReport e, StatusReport a) => StatusMismatch(e, a),
            (MessagePayload e, MessagePayload a) => MessageMismatch(e, a),
            (PositionReport e, PositionReport a) => e.IsMessagingCapable != a.IsMessagingCapable
                ? "the messaging flag does not survive encoding"
                : PositionMismatch(e, a),
            (MicEReport e, MicEReport a) => MicEEncoder.Mismatch(e, a),
            (ObjectReport e, ObjectReport a) => e.Name != a.Name ? "the name does not survive encoding"
            : e.IsLive != a.IsLive ? "the live or killed flag does not survive encoding"
            : PositionMismatch(e, a),
            (ItemReport e, ItemReport a) => e.Name != a.Name ? "the name does not survive encoding"
            : e.IsLive != a.IsLive ? "the live or killed flag does not survive encoding"
            : PositionMismatch(e, a),
            (WeatherReport e, WeatherReport a) => WeatherMismatch(e, a),
            (TelemetryReport e, TelemetryReport a) => TelemetryEncoder.Mismatch(e, a),
            (TelemetryDefinition e, TelemetryDefinition a) => TelemetryEncoder.Mismatch(e, a),
            _ => $"it decodes as {actual?.GetType().Name ?? "nothing"}, not {expected.GetType().Name}",
        };

    private static byte[] Status(StatusReport status)
    {
        var info = new List<byte> { (byte)'>' };
        if (status.Timestamp is { } timestamp)
        {
            if (status.MaidenheadLocator is not null)
            {
                throw new ArgumentException("A status report has a timestamp or a Maidenhead locator, not both.");
            }

            if (timestamp is not { Format: AprsTimestampFormat.DayHourMinute, IsLocal: false })
            {
                throw new ArgumentException("A status report timestamp must be zulu day-hour-minute (DDHHMMz).");
            }

            info.AddRange(Encoding.ASCII.GetBytes(timestamp.ToString()));
        }
        else if (status.MaidenheadLocator is { } locator)
        {
            if (status.Symbol is not { } symbol)
            {
                throw new ArgumentException("A status report with a Maidenhead locator needs its symbol.");
            }

            if (
                locator.Length is not (4 or 6)
                || StatusDecoder.TryLocator(Encoding.ASCII.GetBytes($"{locator}{symbol}"), out _) is null
            )
            {
                throw new ArgumentException(
                    $"'{locator}' is not a 4- or 6-character Maidenhead locator (two letters A-R, two digits, two letters A-X)."
                );
            }

            if (status.Text.Length > 0 && status.Text[0] != ' ')
            {
                throw new ArgumentException("Status text after a Maidenhead locator must start with a space.");
            }

            info.AddRange(Encoding.ASCII.GetBytes($"{locator.ToUpperInvariant()}{symbol}"));
        }
        else if (status.Symbol is not null)
        {
            throw new ArgumentException("A status report symbol goes with a Maidenhead locator.");
        }

        info.AddRange(TextBytes(status.TextBytes, status.Text, "status text"));
        return [.. info];
    }

    private static byte[] Message(MessagePayload message)
    {
        // APRS101 only says 9 characters, padded with spaces; services use '_' (NWS_WARN) and spaces (BLN3 SKYR).
        var addressee = message.Addressee;
        if (
            addressee.Length is < 1 or > 9
            || addressee[^1] == ' '
            || !addressee.All(c => c is >= ' ' and <= '~' and not ':')
        )
        {
            throw new ArgumentException(
                "A message addressee must be 1 to 9 printable ASCII characters, without ':' or trailing spaces."
            );
        }

        var info = new List<byte>(80);
        info.AddRange(Encoding.ASCII.GetBytes($":{addressee, -9}:"));
        if (message.Kind is MessageKind.Acknowledgement or MessageKind.Rejection)
        {
            if (message.Text.Length > 0)
            {
                throw new ArgumentException("An acknowledgement or rejection carries no text.");
            }

            if (message.MessageId is null)
            {
                throw new ArgumentException("An acknowledgement or rejection needs the message ID it answers.");
            }

            info.AddRange(Encoding.ASCII.GetBytes(message.Kind == MessageKind.Acknowledgement ? "ack" : "rej"));
            info.AddRange(MessageId(message));
            return [.. info];
        }

        var text = TextBytes(message.TextBytes, message.Text, "message text");
        if (message.TextBytes.IsEmpty)
        {
            // Text from a caller follows APRS101: no '|', '~' or '{', and at most 67 characters.
            if (message.Text.AsSpan().IndexOfAny("|~{") >= 0)
            {
                throw new ArgumentException("Message text must not contain '|', '~' or '{'.");
            }

            if (message.Text.Length > MaxMessageText)
            {
                throw new ArgumentException($"Message text is limited to {MaxMessageText} characters.");
            }
        }

        info.AddRange(text);
        if (message.MessageId is not null)
        {
            info.Add((byte)'{');
            info.AddRange(MessageId(message));
        }
        else if (message.ReplyAck is not null)
        {
            throw new ArgumentException("A reply-ack needs a message ID.");
        }

        return [.. info];
    }

    /// <summary>The message ID, then <c>}</c> and the reply-ack when there is one (APRS 1.1).</summary>
    private static byte[] MessageId(MessagePayload message)
    {
        if (!IsId(message.MessageId, 1))
        {
            throw new ArgumentException("A message ID must be 1 to 5 letters or digits.");
        }

        if (message.ReplyAck is null)
        {
            return Encoding.ASCII.GetBytes(message.MessageId!);
        }

        if (!IsId(message.ReplyAck, 0))
        {
            throw new ArgumentException("A reply-ack must be 0 to 5 letters or digits.");
        }

        return Encoding.ASCII.GetBytes($"{message.MessageId}}}{message.ReplyAck}");
    }

    private static bool IsId(string? id, int minimum) =>
        id is not null && id.Length >= minimum && id.Length <= 5 && id.All(char.IsAsciiLetterOrDigit);

    /// <summary>The parser's original bytes when the payload came from it, otherwise the text as UTF-8.</summary>
    internal static ReadOnlySpan<byte> TextBytes(ReadOnlyMemory<byte> parsed, string text, string what)
    {
        if (!parsed.IsEmpty)
        {
            return parsed.Span;
        }

        if (text.Any(char.IsControl))
        {
            throw new ArgumentException($"The {what} must not contain control characters such as line breaks.");
        }

        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>
    /// Compares what a receiver decodes with what was given. Numbers are rounded to the format's precision, so they
    /// only have to land within it; text, symbol, timestamp and which fields are present must match exactly.
    /// </summary>
    internal static string? PositionMismatch(PositionedPayload e, PositionedPayload a)
    {
        // An uncompressed position is sent in hundredths of a minute, coarser with ambiguity; compressed is finer.
        var latitudeStep = e.IsCompressed ? 1 / 380926.0 : AmbiguityStep(e.Position.Ambiguity);
        var longitudeStep = e.IsCompressed ? 1 / 190463.0 : AmbiguityStep(e.Position.Ambiguity);
        var datumSlack = e.Datum is null ? 0 : 0.01 / 60; // a !DAO! position is truncated, not rounded
        return e.Symbol != a.Symbol ? "the symbol does not survive encoding"
            : e.Timestamp != a.Timestamp ? "the timestamp does not survive encoding"
            : e.IsCompressed != a.IsCompressed ? "the position format does not survive encoding"
            : e.Position.Ambiguity != a.Position.Ambiguity ? "the ambiguity does not survive encoding"
            : Math.Abs(e.Position.Latitude - a.Position.Latitude) > latitudeStep + datumSlack + 1e-9
            || Math.Abs(e.Position.Longitude - a.Position.Longitude) > longitudeStep + datumSlack + 1e-9
                ? "the position does not survive encoding"
            : (PositionEncoder.CourseOf(e) is null) != (a.Course is null)
            || (PositionEncoder.SpeedOf(e) is null) != (a.SpeedKnots is null)
                ? "the course and speed do not survive encoding"
            : (e.PowerHeightGain is null) != (a.PowerHeightGain is null)
            || (e.RangeMiles is null) != (a.RangeMiles is null)
            || (e.DirectionFindingStrength is null) != (a.DirectionFindingStrength is null)
                ? "the data extension does not survive encoding"
            : (e.AltitudeFeet is null) != (a.AltitudeFeet is null) ? "the altitude does not survive encoding"
            : (e.Weather is null) != (a.Weather is null) ? "the comment would be read as weather readings"
            : e.Weather is { } sent && a.Weather is { } read && WeatherEncoder.Mismatch(sent, read) is { } weather
                ? weather
            : PositionEncoder.ExpectedComment(e) != a.Comment ? "the comment does not survive encoding"
            : null;

        static double AmbiguityStep(int ambiguity) =>
            ambiguity switch
            {
                0 => 0.5 / 6000, // half a hundredth of a minute
                1 => 0.1 / 60,
                2 => 1.0 / 60,
                3 => 10.0 / 60,
                _ => 1.0,
            };
    }

    private static string? WeatherMismatch(WeatherReport e, WeatherReport a) =>
        e.Timestamp != a.Timestamp ? "the timestamp does not survive encoding"
        : (e.Weather.WindDirection is null) != (a.Weather.WindDirection is null)
        || (e.Weather.WindSpeedMph is null) != (a.Weather.WindSpeedMph is null)
            ? "the wind does not survive encoding"
        : WeatherEncoder.Mismatch(e.Weather, a.Weather) is { } weather ? weather
        : e.Comment != a.Comment ? "the comment would be read as weather readings, or does not survive encoding"
        : null;

    private static string? StatusMismatch(StatusReport e, StatusReport a) =>
        e.Timestamp != a.Timestamp ? "the text would be read as a timestamp"
        : !string.Equals(e.MaidenheadLocator?.ToUpperInvariant(), a.MaidenheadLocator, StringComparison.Ordinal)
        || e.Symbol != a.Symbol
            ? "the text would be read as a Maidenhead locator"
        : e.Text != a.Text ? "the text does not survive encoding"
        : null;

    private static string? MessageMismatch(MessagePayload e, MessagePayload a) =>
        e.Kind != a.Kind ? $"it decodes as {a.Kind}, not {e.Kind}; the kind follows from the addressee and text"
        : e.Addressee != a.Addressee ? "the addressee does not survive encoding"
        : e.MessageId != a.MessageId || e.ReplyAck != a.ReplyAck ? "the message ID does not survive encoding"
        : e.Text != a.Text ? "the text does not survive encoding"
        : null;
}
