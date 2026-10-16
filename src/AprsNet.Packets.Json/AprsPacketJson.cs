using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AprsNet.Symbols;

namespace AprsNet.Packets.Json;

/// <summary>
/// Writes an <see cref="AprsPacket"/> as one JSON object: when it was received, the raw line, the header, the parse
/// outcome and issues, and the decoded fields of its type. The format is documented in <c>docs/json.md</c>; field
/// names and units don't change without a breaking release.
/// </summary>
/// <remarks>
/// Writing only: the raw line is the source of truth, so to read a packet back, parse <c>raw</c> with
/// <see cref="AprsPacket.Parse(string)"/>. Uses <see cref="Utf8JsonWriter"/> directly, so it needs no reflection and
/// works when trimmed or compiled ahead of time.
/// </remarks>
public static class AprsPacketJson
{
    /// <summary>
    /// The writer options <see cref="Serialize"/> uses: text such as °, é and emoji is written as is rather than as
    /// <c>\uXXXX</c> escapes, because comments are mostly text people read. Use the same for <see cref="Write"/> to get
    /// the same output.
    /// </summary>
    public static JsonWriterOptions WriterOptions { get; } =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The packet as one line of JSON.</summary>
    /// <param name="packet">The packet.</param>
    /// <param name="received">When it was received, which also completes timestamps that leave out the date.</param>
    public static string Serialize(AprsPacket packet, DateTimeOffset received)
    {
        ArgumentNullException.ThrowIfNull(packet);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            Write(json, packet, received);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Writes the packet as a JSON object at the writer's current position.</summary>
    /// <param name="json">Where to write it.</param>
    /// <param name="packet">The packet.</param>
    /// <param name="received">When it was received, which also completes timestamps that leave out the date.</param>
    public static void Write(Utf8JsonWriter json, AprsPacket packet, DateTimeOffset received)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(packet);
        json.WriteStartObject();
        json.WriteString("received", Utc(received));
        json.WriteString("raw", packet.ToString());
        if (packet.HasHeader)
        {
            json.WriteString("source", packet.Source.Value);
            json.WriteString("destination", packet.Destination.Value);
            json.WriteStartArray("path");
            foreach (var entry in packet.Path)
            {
                json.WriteStringValue(entry.ToString());
            }

            json.WriteEndArray();
        }

        json.WriteString("type", Name(packet.Type));
        json.WriteString("outcome", Name(packet.Outcome));
        json.WriteStartArray("issues");
        foreach (var issue in packet.Issues)
        {
            json.WriteStartObject();
            json.WriteString("code", Name(issue.Code));
            json.WriteString("severity", Name(issue.Severity));
            json.WriteNumber("offset", issue.Offset);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        switch (packet.Payload)
        {
            case PositionedPayload report:
                json.WritePropertyName(
                    report switch
                    {
                        ObjectReport => "object",
                        ItemReport => "item",
                        MicEReport => "micE",
                        _ => "position",
                    }
                );
                Position(json, report, received);
                break;
            case StatusReport status:
                json.WritePropertyName("status");
                Status(json, status, received);
                break;
            case MessagePayload message:
                json.WritePropertyName("message");
                Message(json, message);
                break;
            case TelemetryDefinition definition:
                json.WritePropertyName("telemetryDefinition");
                Telemetry(json, definition);
                break;
            case TelemetryReport telemetry:
                json.WritePropertyName("telemetry");
                Telemetry(json, telemetry);
                break;
            case WeatherReport weather:
                json.WriteStartObject("weather");
                Timestamp(json, weather.Timestamp, received);
                WeatherReadings(json, weather.Weather);
                json.WriteString("comment", weather.Comment);
                json.WriteEndObject();
                break;
            case ThirdPartyPayload thirdParty:
                json.WritePropertyName("thirdParty");
                Write(json, thirdParty.Packet, received);
                break;
            case CapabilitiesReport capabilities:
                json.WriteStartArray("capabilities");
                foreach (var (token, value) in capabilities.Capabilities)
                {
                    json.WriteStartObject();
                    json.WriteString("token", token);
                    if (value is not null)
                    {
                        json.WriteString("value", value);
                    }

                    json.WriteEndObject();
                }

                json.WriteEndArray();
                break;
            case UserDefinedPayload user:
                json.WriteStartObject("userDefined");
                json.WriteString("userId", user.UserId.ToString());
                json.WriteString("packetType", user.UserPacketType.ToString());
                json.WriteBoolean("experimental", user.IsExperimental);
                json.WriteString("data", user.Data);
                json.WriteEndObject();
                break;
            case RawWeatherReport raw:
                json.WriteStartObject("rawWeather");
                json.WriteString("format", Name(raw.Format));
                json.WriteString("station", RawWeatherStation(raw.Format));
                json.WriteString("data", raw.Data);
                json.WriteEndObject();
                break;
        }

        json.WriteEndObject();
    }

    private static void Message(Utf8JsonWriter json, MessagePayload message)
    {
        json.WriteStartObject();
        json.WriteString("addressee", message.Addressee);
        json.WriteString("kind", Name(message.Kind));
        json.WriteString("text", message.Text);
        WriteString(json, "messageId", message.MessageId);
        WriteString(json, "replyAck", message.ReplyAck);
        json.WriteBoolean("ackRequired", message.IsAckRequired);
        WriteString(json, "bulletinId", message.BulletinId);
        WriteString(json, "bulletinGroup", message.BulletinGroup);
        WriteString(json, "queryType", message.QueryType);
        WriteString(json, "queryArgument", message.QueryArgument);
        json.WriteEndObject();
    }

    private static void Telemetry(Utf8JsonWriter json, TelemetryDefinition definition)
    {
        json.WriteStartObject();
        json.WriteString("addressee", definition.Addressee);
        json.WriteString("kind", Name(definition.Kind));
        json.WriteStartArray("labels");
        foreach (var label in definition.Labels)
        {
            json.WriteStringValue(label);
        }

        json.WriteEndArray();
        json.WriteStartArray("equations");
        foreach (var equation in definition.Equations)
        {
            json.WriteStartObject();
            json.WriteNumber("a", equation.A);
            json.WriteNumber("b", equation.B);
            json.WriteNumber("c", equation.C);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        WriteString(json, "bitSense", definition.BitSense);
        WriteString(json, "projectName", definition.ProjectName);
        json.WriteEndObject();
    }

    private static void Telemetry(Utf8JsonWriter json, TelemetryReport report)
    {
        json.WriteStartObject();
        json.WriteString("sequence", report.Sequence);
        if (report.SequenceNumber is { } number)
        {
            json.WriteNumber("sequenceNumber", number);
        }
        else
        {
            json.WriteNull("sequenceNumber");
        }

        json.WriteStartArray("analog");
        foreach (var value in report.Analog)
        {
            if (value is { } v)
            {
                json.WriteNumberValue(v);
            }
            else
            {
                json.WriteNullValue();
            }
        }

        json.WriteEndArray();
        WriteString(
            json,
            "digital",
            report.Digital.Count > 0 ? string.Concat(report.Digital.Select(b => b ? '1' : '0')) : null
        );
        json.WriteString("comment", report.Comment);
        json.WriteEndObject();
    }

    private static void WriteString(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null)
        {
            json.WriteNull(name);
        }
        else
        {
            json.WriteString(name, value);
        }
    }

    private static void Status(Utf8JsonWriter json, StatusReport status, DateTimeOffset received)
    {
        json.WriteStartObject();
        Timestamp(json, status.Timestamp, received);
        if (status.MaidenheadLocator is { } locator)
        {
            json.WriteString("locator", locator);
        }

        if (status.Symbol is { } symbol)
        {
            Symbol(json, symbol);
        }

        WriteNumber(json, "beamHeading", status.BeamHeading);
        WriteNumber(json, "erpWatts", status.EffectiveRadiatedPowerWatts);
        json.WriteString("text", status.Text);
        json.WriteEndObject();
    }

    private static void Timestamp(Utf8JsonWriter json, AprsTimestamp? timestamp, DateTimeOffset received)
    {
        if (timestamp is not { } sent)
        {
            return;
        }

        json.WriteStartObject("timestamp");
        json.WriteString("sent", sent.ToString());
        if (sent.ToDateTimeOffset(received) is { } time)
        {
            json.WriteString("utc", Utc(time));
        }

        json.WriteEndObject();
    }

    private static void Symbol(Utf8JsonWriter json, AprsSymbol symbol)
    {
        json.WriteStartObject("symbol");
        json.WriteString("table", symbol.WithoutOverlay.Table.ToString());
        json.WriteString("code", symbol.Code.ToString());
        if (symbol.Overlay is { } overlay)
        {
            json.WriteString("overlay", overlay.ToString());
        }

        json.WriteString("name", AprsSymbolCatalog.GetName(symbol));
        json.WriteEndObject();
    }

    private static void Position(Utf8JsonWriter json, PositionedPayload report, DateTimeOffset received)
    {
        json.WriteStartObject();
        switch (report)
        {
            case ObjectReport o:
                json.WriteString("name", o.Name);
                json.WriteBoolean("live", o.IsLive);
                break;
            case ItemReport i:
                json.WriteString("name", i.Name);
                json.WriteBoolean("live", i.IsLive);
                break;
        }

        json.WriteNumber("latitude", Math.Round(report.Position.Latitude, 6));
        json.WriteNumber("longitude", Math.Round(report.Position.Longitude, 6));
        json.WriteNumber("ambiguity", report.Position.Ambiguity);
        json.WriteBoolean("unknown", report.IsPositionUnknown);
        Symbol(json, report.Symbol);
        Timestamp(json, report.Timestamp, received);

        switch (report)
        {
            case PositionReport position:
                json.WriteBoolean("messaging", position.IsMessagingCapable);
                break;
            case MicEReport mice:
                json.WriteBoolean("messaging", mice.IsMessagingCapable);
                json.WriteString("message", Name(mice.Message));
                json.WriteBoolean("currentFix", mice.IsCurrentFix);
                WriteString(json, "device", mice.Device);
                WriteString(json, "locator", mice.MaidenheadLocator);
                break;
        }

        json.WriteBoolean("compressed", report.IsCompressed);
        WriteNumber(json, "course", report.Course);
        WriteNumber(json, "speedKnots", report.SpeedKnots);
        WriteNumber(json, "altitudeFeet", report.AltitudeFeet);
        WriteNumber(json, "rangeMiles", report.RangeMiles);
        if (report.PowerHeightGain is { } phg)
        {
            json.WriteStartObject("phg");
            json.WriteNumber("powerWatts", phg.PowerWatts);
            json.WriteNumber("heightFeet", phg.HeightFeet);
            json.WriteNumber("gainDbi", phg.GainDbi);
            WriteNumber(json, "directivity", phg.Directivity);
            json.WriteNumber("rangeMiles", Math.Round(phg.RangeMiles, 1));
            json.WriteEndObject();
        }

        if (report.DirectionFindingStrength is { } dfs)
        {
            json.WriteStartObject("dfs");
            json.WriteNumber("strength", dfs.Strength);
            json.WriteNumber("heightFeet", dfs.HeightFeet);
            json.WriteNumber("gainDbi", dfs.GainDbi);
            WriteNumber(json, "directivity", dfs.Directivity);
            json.WriteEndObject();
        }

        if (report.DirectionFinding is { } df)
        {
            json.WriteStartObject("df");
            json.WriteNumber("bearing", df.Bearing);
            json.WriteNumber("hits", df.Hits);
            json.WriteNumber("rangeMiles", df.RangeMiles);
            json.WriteNumber("quality", df.Quality);
            json.WriteBoolean("fixedStation", df.IsFixedStation);
            json.WriteEndObject();
        }

        if (report.CompressionType is { } type)
        {
            json.WriteStartObject("compressionType");
            json.WriteString("gpsFix", Name(type.GpsFix));
            json.WriteString("nmeaSource", Name(type.NmeaSource));
            json.WriteString("origin", Name(type.Origin));
            json.WriteEndObject();
        }

        if (report.Area is { } area)
        {
            json.WriteStartObject("area");
            json.WriteString("shape", area.Kind.ToString().ToLowerInvariant());
            json.WriteNumber("type", area.Type);
            json.WriteBoolean("filled", area.IsFilled);
            json.WriteNumber("color", area.Color);
            json.WriteNumber("latitudeOffsetDegrees", Math.Round(area.LatitudeOffsetDegrees, 6));
            json.WriteNumber("longitudeOffsetDegrees", Math.Round(area.LongitudeOffsetDegrees, 6));
            WriteNumber(json, "corridorMiles", area.CorridorMiles);
            json.WriteEndObject();
        }

        if (report.Signpost is { } signpost)
        {
            json.WriteString("signpost", signpost);
        }

        if (report.Datum is { } datum)
        {
            json.WriteString("datum", datum.ToString());
        }

        if (report.Weather is { } weather)
        {
            json.WriteStartObject("weather");
            WeatherReadings(json, weather);
            json.WriteEndObject();
        }

        if (report.Telemetry is { } telemetry)
        {
            json.WritePropertyName("telemetry");
            Telemetry(json, telemetry);
        }

        json.WriteString("comment", report.Comment);
        json.WriteEndObject();
    }

    private static void WeatherReadings(Utf8JsonWriter json, WeatherData w)
    {
        WriteNumber(json, "windDirection", w.WindDirection);
        WriteNumber(json, "windSpeedMph", w.WindSpeedMph);
        WriteNumber(json, "windGustMph", w.WindGustMph);
        WriteNumber(json, "temperatureF", w.TemperatureF);
        WriteNumber(json, "rainLastHourInches", w.RainLastHourInches);
        WriteNumber(json, "rainLast24HoursInches", w.RainLast24HoursInches);
        WriteNumber(json, "rainSinceMidnightInches", w.RainSinceMidnightInches);
        WriteNumber(json, "humidityPercent", w.HumidityPercent);
        WriteNumber(json, "pressureMillibars", w.PressureMillibars);
        WriteNumber(json, "luminosityWattsPerSquareMeter", w.LuminosityWattsPerSquareMeter);
        WriteNumber(json, "snowLast24HoursInches", w.SnowLast24HoursInches);
        WriteNumber(json, "rainCounter", w.RainCounter);
    }

    private static void WriteNumber(Utf8JsonWriter json, string name, double? value)
    {
        if (value is { } number)
        {
            json.WriteNumber(name, Math.Round(number, 2));
        }
        else
        {
            json.WriteNull(name);
        }
    }

    private static string RawWeatherStation(RawWeatherFormat format) =>
        format switch
        {
            RawWeatherFormat.UltimeterDataLogging => "Ultimeter 2000 (data logging)",
            RawWeatherFormat.UltimeterPacket => "Ultimeter 2000 (packet mode)",
            _ => "Peet Bros U-II",
        };

    private static string Utc(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Name<T>(T value)
        where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
