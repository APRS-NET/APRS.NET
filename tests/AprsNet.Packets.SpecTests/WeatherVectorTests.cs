using System.Text.Json;

namespace AprsNet.Packets.SpecTests;

/// <summary>
/// Checks every decoded field of the APRS101 weather vectors: positionless reports and positions with the weather
/// station symbol. An expected field this test does not know fails, so new vector fields get checked.
/// </summary>
public class WeatherVectorTests
{
    public static TheoryData<string> WeatherVectorIds() =>
        [
            .. VectorFileTests
                .VectorIds()
                .Select(row => row.Data)
                .Where(id =>
                    VectorFileTests.Vector(id).GetProperty("expect").TryGetProperty("type", out var type)
                    && type.GetString() == "weather"
                ),
        ];

    [Theory]
    [MemberData(nameof(WeatherVectorIds))]
    public void DecodesTheVector(string id)
    {
        var vector = VectorFileTests.Vector(id);
        var packet = AprsPacket.Parse(vector.GetProperty("packet").GetString()!);

        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        var (weather, comment) = packet.Payload switch
        {
            WeatherReport r => (r.Weather, r.Comment),
            PositionedPayload p => (p.Weather ?? throw new InvalidOperationException($"{id}: no weather"), p.Comment),
            _ => throw new InvalidOperationException($"{id}: not a weather report"),
        };
        foreach (var field in vector.GetProperty("expect").EnumerateObject())
        {
            var e = field.Value;
            switch (field.Name)
            {
                case "type":
                    break;
                case "weather":
                    CheckWeather(id, e, weather, comment);
                    break;
                case "timestamp" when packet.Payload is WeatherReport report:
                    PositionVectorTests.Timestamp(e, report.Timestamp!.Value);
                    break;
                default:
                    Assert.True(
                        PositionVectorTests.CheckPositionField(
                            id,
                            field.Name,
                            e,
                            Assert.IsAssignableFrom<PositionedPayload>(packet.Payload)
                        ),
                        $"{id}: the test does not check expected field '{field.Name}' yet"
                    );
                    break;
            }
        }
    }

    /// <summary>
    /// Checks the vector's weather fields. The vectors give rain in hundredths of an inch and pressure in millibars,
    /// as sent. APRS101's software and unit type codes are not split out, so they are checked as the comment's start.
    /// </summary>
    internal static void CheckWeather(string id, JsonElement expected, WeatherData weather, string comment)
    {
        var software = "";
        foreach (var field in expected.EnumerateObject())
        {
            var e = field.Value;
            switch (field.Name)
            {
                case "windDirection":
                    Assert.Equal(IntOrNull(e), weather.WindDirection);
                    break;
                case "windSpeed":
                    Near(e, weather.WindSpeedMph, 0.05, id, field.Name);
                    break;
                case "windGust":
                    Assert.Equal(IntOrNull(e), weather.WindGustMph);
                    break;
                case "temperatureF":
                    Assert.Equal(IntOrNull(e), weather.TemperatureF);
                    break;
                case "rain1h":
                    Near(e, weather.RainLastHourInches * 100, 0.001, id, field.Name);
                    break;
                case "rain24h":
                    Near(e, weather.RainLast24HoursInches * 100, 0.001, id, field.Name);
                    break;
                case "rainMidnight":
                    Near(e, weather.RainSinceMidnightInches * 100, 0.001, id, field.Name);
                    break;
                case "humidity":
                    Assert.Equal(IntOrNull(e), weather.HumidityPercent);
                    break;
                case "pressureMb":
                    Near(e, weather.PressureMillibars, 0.001, id, field.Name);
                    break;
                case "luminosity":
                    Assert.Equal(IntOrNull(e), weather.LuminosityWattsPerSquareMeter);
                    break;
                case "software":
                    software = e.GetString() + software;
                    break;
                case "unit":
                    software += e.GetString();
                    break;
                default:
                    Assert.Fail($"{id}: the test does not check expected weather field '{field.Name}' yet");
                    break;
            }
        }

        Assert.StartsWith(software, comment, StringComparison.Ordinal);
    }

    private static int? IntOrNull(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();

    private static void Near(JsonElement e, double? actual, double tolerance, string id, string name)
    {
        if (e.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.True(
            actual is { } a && Math.Abs(a - e.GetDouble()) <= tolerance,
            $"{id}: {name} expected {e.GetDouble()}, got {actual}"
        );
    }
}
