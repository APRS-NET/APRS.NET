using System.Globalization;
using System.Text;
using System.Text.Json;
using AprsNet.Packets;
using AprsNet.Packets.Json;
using AprsNet.Symbols;

namespace AprsNet.AprsIsCli;

/// <summary>How <c>aprscli</c> prints packets.</summary>
internal enum OutputFormat
{
    /// <summary>The packet line as received (control characters escaped).</summary>
    Raw,

    /// <summary>One readable line per packet: the header, then the decoded fields.</summary>
    Decoded,

    /// <summary>One JSON object per line.</summary>
    Json,

    /// <summary>A block per packet: a header line and labelled, aligned fields, coloured on a terminal.</summary>
    Pretty,
}

/// <summary>Turns parsed packets into the <c>decoded</c> and <c>json</c> output formats.</summary>
internal static class PacketFormatter
{
    /// <summary>One JSON object, in the library's documented format (<see cref="AprsPacketJson"/>).</summary>
    public static string Json(AprsPacket packet, DateTimeOffset received) => AprsPacketJson.Serialize(packet, received);

    /// <summary>
    /// The header as sent, then the packet type and its decoded fields, for example
    /// <c>N0CALL-9&gt;APRS,WIDE1-1  position 49.05833 -72.02917  /&gt; Car  088° 36 kn  "Test"</c>.
    /// </summary>
    public static string Decoded(AprsPacket packet)
    {
        var line = new StringBuilder();
        if (packet.HasHeader)
        {
            line.Append(packet.Source.Value).Append('>').Append(packet.Destination.Value);
            foreach (var entry in packet.Path)
            {
                line.Append(',').Append(entry);
            }
        }

        line.Append("  ");
        switch (packet.Payload)
        {
            case PositionedPayload report:
                Position(line, report);
                break;
            case StatusReport status:
                Status(line, status);
                break;
            case MessagePayload message:
                Message(line, message);
                break;
            case TelemetryDefinition definition:
                Telemetry(line, definition);
                break;
            case TelemetryReport telemetry:
                Telemetry(line, telemetry);
                break;
            case WeatherReport weather:
                line.Append("weather at ").Append(weather.Timestamp?.ToString() ?? "an unknown time");
                Weather(line, weather.Weather);
                if (weather.Comment.Trim().Length > 0)
                {
                    line.Append("  \"").Append(weather.Comment.Trim()).Append('"');
                }

                break;
            case CapabilitiesReport capabilities:
                line.Append("capabilities  ").Append(Capabilities(capabilities));
                break;
            case ThirdPartyPayload thirdParty:
                line.Append("third-party  ").Append(Decoded(thirdParty.Packet));
                break;
            case UserDefinedPayload user:
                line.Append("user-defined  ").Append(UserDefined(user));
                if (user.Data.Length > 0)
                {
                    line.Append("  \"").Append(user.Data).Append('"');
                }

                break;
            case RawWeatherReport raw:
                line.Append("raw weather  ").Append(RawWeather(raw.Format)).Append("  ").Append(raw.Data);
                break;
            default:
                line.Append(Name(packet.Type))
                    .Append(packet.Outcome == ParseOutcome.Malformed ? " (malformed)" : " (not decoded yet)");
                if (packet.HasHeader)
                {
                    line.Append("  ").Append(Encoding.Latin1.GetString(packet.Information.Span));
                }
                else
                {
                    line.Append("  ").Append(packet);
                }

                break;
        }

        foreach (var issue in packet.Issues)
        {
            line.Append(CultureInfo.InvariantCulture, $"  [{Name(issue.Severity)} {issue.Code} at {issue.Offset}]");
        }

        return line.ToString();
    }

    private static void Message(StringBuilder line, MessagePayload message)
    {
        line.Append(Describe(message));
        if (message.Kind is not (MessageKind.Acknowledgement or MessageKind.Rejection) && message.MessageId is { } id)
        {
            line.Append("  #").Append(id);
        }

        if (message.ReplyAck is { Length: > 0 } replyAck)
        {
            line.Append("  reply-ack ").Append(replyAck);
        }

        if (message is { Kind: MessageKind.Query, QueryArgument: { } argument })
        {
            line.Append("  ").Append(argument);
        }
        else if (message.Text.Length > 0 && message.Kind != MessageKind.Query)
        {
            line.Append("  \"").Append(message.Text).Append('"');
        }
    }

    /// <summary>What a message is, in a few words: <c>message to WU2Z</c>, <c>ack to N0CALL for 003</c>.</summary>
    private static string Describe(MessagePayload message) =>
        message.Kind switch
        {
            MessageKind.Acknowledgement => $"ack to {message.Addressee} for {message.MessageId}",
            MessageKind.Rejection => $"reject to {message.Addressee} for {message.MessageId}",
            MessageKind.Bulletin when message.BulletinGroup is { } group =>
                $"bulletin {message.BulletinId} to group {group}",
            MessageKind.Bulletin => $"bulletin {message.BulletinId}",
            MessageKind.Announcement when message.BulletinGroup is { } group =>
                $"announcement {message.BulletinId} to group {group}",
            MessageKind.Announcement => $"announcement {message.BulletinId}",
            MessageKind.NwsBulletin => $"NWS bulletin {message.Addressee}",
            MessageKind.Query => $"query {message.QueryType} to {message.Addressee}",
            _ => $"message to {message.Addressee}",
        };

    private static void Telemetry(StringBuilder line, TelemetryDefinition definition)
    {
        line.Append("telemetry ")
            .Append(Words(definition.Kind))
            .Append(" for ")
            .Append(definition.Addressee)
            .Append("  ");
        line.Append(Values(definition));
    }

    private static void Telemetry(StringBuilder line, TelemetryReport report)
    {
        line.Append("telemetry #").Append(report.Sequence).Append("  ").Append(Analog(report));
        if (report.Digital.Count > 0)
        {
            line.Append("  bits ").Append(Bits(report));
        }

        if (report.Comment.Trim().Length > 0)
        {
            line.Append("  \"").Append(report.Comment.Trim()).Append('"');
        }
    }

    /// <summary>The raw analog values as sent, <c>-</c> for an empty field: <c>199, 0, 255, 73, 123</c>.</summary>
    internal static string Analog(TelemetryReport report) =>
        string.Join(
            ", ",
            report.Analog.Select(v => v is { } value ? value.ToString(CultureInfo.InvariantCulture) : "-")
        );

    /// <summary>The digital channels as <c>0</c>/<c>1</c> characters: <c>01101001</c>.</summary>
    internal static string Bits(TelemetryReport report) => string.Concat(report.Digital.Select(b => b ? '1' : '0'));

    /// <summary>A definition kind in words: <c>names</c>, <c>units</c>, <c>equations</c>, <c>bit sense</c>.</summary>
    internal static string Words(TelemetryDefinitionKind kind) =>
        kind switch
        {
            TelemetryDefinitionKind.ParameterNames => "names",
            TelemetryDefinitionKind.Units => "units",
            TelemetryDefinitionKind.Equations => "equations",
            _ => "bit sense",
        };

    /// <summary>A definition's values on one line: labels, equations, or bit sense and project.</summary>
    internal static string Values(TelemetryDefinition definition) =>
        definition.Kind switch
        {
            TelemetryDefinitionKind.Equations => string.Join(
                " | ",
                definition.Equations.Select(e =>
                    string.Create(CultureInfo.InvariantCulture, $"{e.A}·v² + {e.B}·v + {e.C}")
                )
            ),
            TelemetryDefinitionKind.BitSense => definition.ProjectName is { } project
                ? $"{definition.BitSense} \"{project}\""
                : definition.BitSense ?? "",
            _ => string.Join(", ", definition.Labels),
        };

    private static void Status(StringBuilder line, StatusReport status)
    {
        line.Append("status");
        if (status.Timestamp is { } timestamp)
        {
            line.Append("  at ").Append(timestamp);
        }

        if (status.MaidenheadLocator is { } locator)
        {
            line.Append("  locator ").Append(locator);
            if (status.Symbol is { } symbol)
            {
                line.Append("  ").Append(symbol).Append(' ').Append(SymbolName(symbol));
            }
        }

        if (status.BeamHeading is { } heading)
        {
            line.Append(
                CultureInfo.InvariantCulture,
                $"  beam {heading:000}° {status.EffectiveRadiatedPowerWatts} W ERP"
            );
        }

        if (status.Text.Trim().Length > 0)
        {
            line.Append("  \"").Append(status.Text.Trim()).Append('"');
        }
    }

    private static void Position(StringBuilder line, PositionedPayload report)
    {
        line.Append(
            report switch
            {
                ObjectReport o => $"object {o.Name}{(o.IsLive ? "" : " (killed)")}  position ",
                ItemReport i => $"item {i.Name}{(i.IsLive ? "" : " (killed)")}  position ",
                MicEReport => "Mic-E position ",
                _ => "position ",
            }
        );
        if (report.IsPositionUnknown)
        {
            line.Append("unknown");
        }
        else
        {
            line.Append(
                CultureInfo.InvariantCulture,
                $"{report.Position.Latitude:0.00000} {report.Position.Longitude:0.00000}"
            );
            if (report.Position.Ambiguity > 0)
            {
                line.Append(CultureInfo.InvariantCulture, $" (ambiguity {report.Position.Ambiguity})");
            }
        }

        line.Append("  ").Append(report.Symbol).Append(' ').Append(SymbolName(report.Symbol));
        if (report.Timestamp is { } timestamp)
        {
            line.Append("  at ").Append(timestamp);
        }

        if (report.Weather is { } weather)
        {
            Weather(line, weather);
        }
        else if (report.Course is not null || report.SpeedKnots is not null)
        {
            line.Append("  ");
            line.Append(
                report.Course is { } course ? course.ToString("000", CultureInfo.InvariantCulture) + "°" : "---"
            );
            if (report.SpeedKnots is { } speed)
            {
                line.Append(CultureInfo.InvariantCulture, $" {speed:0.#} kn");
            }
        }

        if (report.AltitudeFeet is { } altitude)
        {
            line.Append(CultureInfo.InvariantCulture, $"  {altitude:0} ft");
        }

        if (report.PowerHeightGain is { } phg)
        {
            line.Append(
                CultureInfo.InvariantCulture,
                $"  PHG {phg.PowerWatts} W {phg.HeightFeet} ft {phg.GainDbi} dBi {Direction(phg.Directivity)} ({phg.RangeMiles:0.#} mi)"
            );
        }

        if (report.RangeMiles is { } range)
        {
            line.Append(CultureInfo.InvariantCulture, $"  range {range:0.#} mi");
        }

        if (report.DirectionFindingStrength is { } dfs)
        {
            line.Append(
                CultureInfo.InvariantCulture,
                $"  DFS S{dfs.Strength} {dfs.HeightFeet} ft {dfs.GainDbi} dBi {Direction(dfs.Directivity)}"
            );
        }

        if (report.DirectionFinding is { } df)
        {
            line.Append(CultureInfo.InvariantCulture, $"  DF bearing {df.Bearing:000}° quality {df.Quality}");
        }

        if (report.Area is { } area)
        {
            line.Append(CultureInfo.InvariantCulture, $"  area {AreaText(area)}");
        }

        if (report.Signpost is { } signpost)
        {
            line.Append("  signpost ").Append(signpost);
        }

        if (report is MicEReport mice)
        {
            line.Append("  ").Append(MicEMessageName(mice.Message));
            if (mice.Device is { } device)
            {
                line.Append("  ").Append(device);
            }
        }

        if (report is PositionReport { IsMessagingCapable: true } or MicEReport { IsMessagingCapable: true })
        {
            line.Append("  messaging");
        }

        if (report.Telemetry is { } telemetry)
        {
            line.Append("  ");
            Telemetry(line, telemetry);
        }

        if (report.Comment.Length > 0)
        {
            line.Append("  \"").Append(report.Comment.Trim()).Append('"');
        }
    }

    /// <summary>A Mic-E message as the radios show it: <c>In Service</c>, <c>Custom-2</c>, <c>Emergency</c>.</summary>
    internal static string MicEMessageName(MicEMessage message) =>
        message switch
        {
            MicEMessage.OffDuty => "Off Duty",
            MicEMessage.EnRoute => "En Route",
            MicEMessage.InService => "In Service",
            MicEMessage.Returning => "Returning",
            MicEMessage.Committed => "Committed",
            MicEMessage.Special => "Special",
            MicEMessage.Priority => "Priority",
            >= MicEMessage.Custom0 and <= MicEMessage.Custom6 => $"Custom-{message - MicEMessage.Custom0}",
            _ => message.ToString(),
        };

    /// <summary>An area shape in words: <c>filled box, colour 3, 0.067° × 0.067°</c>.</summary>
    internal static string AreaText(AreaShape area) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(area.IsFilled ? "filled " : "")}{area.Kind.ToString().ToLowerInvariant()}, colour {area.Color}, {area.LatitudeOffsetDegrees:0.###}° × {area.LongitudeOffsetDegrees:0.###}°{(area.CorridorMiles is { } corridor ? $", corridor {corridor} mi" : "")}"
        );

    private static void Weather(StringBuilder line, WeatherData weather)
    {
        foreach (var (label, value) in WeatherFields(weather))
        {
            line.Append("  ").Append(label).Append(' ').Append(value);
        }
    }

    /// <summary>The readings that were sent, labelled, in plain units: <c>("wind", "220° 4 mph, gusts 5 mph")</c>.</summary>
    internal static List<(string Label, string Value)> WeatherFields(WeatherData w)
    {
        var fields = new List<(string Label, string Value)>();
        var wind = new List<string>();
        if (w.WindDirection is not null || w.WindSpeedMph is not null)
        {
            wind.Add(
                string.Join(
                    ' ',
                    new[]
                    {
                        w.WindDirection is { } d ? d.ToString("000", CultureInfo.InvariantCulture) + "°" : null,
                        w.WindSpeedMph is { } s ? string.Create(CultureInfo.InvariantCulture, $"{s:0.#} mph") : null,
                    }.OfType<string>()
                )
            );
        }

        if (w.WindGustMph is { } gust)
        {
            wind.Add(string.Create(CultureInfo.InvariantCulture, $"gusts {gust} mph"));
        }

        if (wind.Count > 0)
        {
            fields.Add(("wind", string.Join(", ", wind)));
        }

        if (w.TemperatureF is { } f)
        {
            fields.Add(("temp", string.Create(CultureInfo.InvariantCulture, $"{f}°F ({(f - 32) / 1.8:0.0}°C)")));
        }

        var rain = new List<string>();
        Add(rain, w.RainLastHourInches, "last hour");
        Add(rain, w.RainLast24HoursInches, "24 h");
        Add(rain, w.RainSinceMidnightInches, "since midnight");
        if (rain.Count > 0)
        {
            fields.Add(("rain", string.Join(", ", rain)));
        }

        if (w.HumidityPercent is { } humidity)
        {
            fields.Add(("humidity", string.Create(CultureInfo.InvariantCulture, $"{humidity}%")));
        }

        if (w.PressureMillibars is { } pressure)
        {
            fields.Add(("pressure", string.Create(CultureInfo.InvariantCulture, $"{pressure:0.0} mb")));
        }

        if (w.LuminosityWattsPerSquareMeter is { } light)
        {
            fields.Add(("sunlight", string.Create(CultureInfo.InvariantCulture, $"{light} W/m²")));
        }

        if (w.SnowLast24HoursInches is { } snow)
        {
            fields.Add(("snow", string.Create(CultureInfo.InvariantCulture, $"{snow:0.#} in in 24 h")));
        }

        if (w.RainCounter is { } counter)
        {
            fields.Add(("rain raw", counter.ToString(CultureInfo.InvariantCulture)));
        }

        return fields;

        static void Add(List<string> parts, double? inches, string period)
        {
            if (inches is { } value)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{value:0.00} in {period}"));
            }
        }
    }

    private static string SymbolName(AprsSymbol symbol) => AprsSymbolCatalog.GetName(symbol);

    private static string Direction(int? degrees) =>
        degrees is { } d ? d.ToString("000", CultureInfo.InvariantCulture) + "°" : "omni";

    /// <summary>The capabilities as sent: <c>IGATE, MSG_CNT=0, LOC_CNT=1</c>.</summary>
    internal static string Capabilities(CapabilitiesReport report) =>
        string.Join(", ", report.Capabilities.Select(c => c.Value is null ? c.Key : $"{c.Key}={c.Value}"));

    /// <summary><c>user Q, type 1</c>, or <c>experimental, type z</c> for <c>{{</c>.</summary>
    internal static string UserDefined(UserDefinedPayload user) =>
        user.IsExperimental
            ? $"experimental, type {user.UserPacketType}"
            : $"user {user.UserId}, type {user.UserPacketType}";

    internal static string RawWeather(RawWeatherFormat format) =>
        format switch
        {
            RawWeatherFormat.UltimeterDataLogging => "Ultimeter 2000 (data logging)",
            RawWeatherFormat.UltimeterPacket => "Ultimeter 2000 (packet mode)",
            _ => "Peet Bros U-II",
        };

    /// <summary>The packet type as the JSON format names it: <c>position</c>, <c>micE</c>, <c>thirdParty</c>.</summary>
    internal static string TypeName(PacketType type) => Name(type);

    private static string Name<T>(T value)
        where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
