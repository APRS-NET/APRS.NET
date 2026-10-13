using System.Text.Json;

namespace AprsNet.Packets.SpecTests;

/// <summary>
/// Checks every decoded field of the APRS101 position vectors. A vector marked superseded (the X1J exception, removed
/// by APRS 1.1) is left out. An expected field this test does not know fails, so new vector fields get checked.
/// </summary>
public class PositionVectorTests
{
    public static TheoryData<string> PositionVectorIds() =>
        [
            .. VectorFileTests
                .VectorIds()
                .Select(row => row.Data)
                .Where(id =>
                {
                    var vector = VectorFileTests.Vector(id);
                    return vector.GetProperty("status").GetString() != "superseded"
                        && vector.GetProperty("expect").TryGetProperty("type", out var type)
                        && type.GetString() == "position";
                }),
        ];

    [Theory]
    [MemberData(nameof(PositionVectorIds))]
    public void DecodesTheVector(string id)
    {
        var vector = VectorFileTests.Vector(id);
        var packet = AprsPacket.Parse(vector.GetProperty("packet").GetString()!);

        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        var report = Assert.IsType<PositionReport>(packet.Payload);
        foreach (var field in vector.GetProperty("expect").EnumerateObject())
        {
            var e = field.Value;
            switch (field.Name)
            {
                case "type":
                    break;
                case "latitude":
                    Near(e.GetDouble(), report.Position.Latitude, 0.000001, id, field.Name);
                    break;
                case "longitude":
                    Near(e.GetDouble(), report.Position.Longitude, 0.000001, id, field.Name);
                    break;
                case "ambiguity":
                    Assert.Equal(e.GetInt32(), report.Position.Ambiguity);
                    break;
                case "messaging":
                    Assert.Equal(e.GetBoolean(), report.IsMessagingCapable);
                    break;
                case "positionUnknown":
                    Assert.Equal(e.GetBoolean(), report.IsPositionUnknown);
                    break;
                case "comment":
                    Assert.Equal(e.GetString(), report.Comment);
                    break;
                case "course":
                    Assert.Equal(IntOrNull(e), report.Course);
                    break;
                case "speed":
                    NearOrNull(e, report.SpeedKnots, 0.05, id, field.Name);
                    break;
                case "rangeMiles":
                    NearOrNull(e, report.RangeMiles, 0.01, id, field.Name);
                    break;
                case "altitudeFeet":
                    NearOrNull(e, report.AltitudeFeet, 0.5, id, field.Name);
                    break;
                case "symbol":
                    Symbol(e, report.Symbol);
                    break;
                case "timestamp":
                    Timestamp(e, Assert.NotNull(report.Timestamp));
                    break;
                case "phg":
                    Assert.NotNull(report.PowerHeightGain);
                    var phg = report.PowerHeightGain;
                    Assert.Equal(e.GetProperty("powerWatts").GetInt32(), phg.PowerWatts);
                    Assert.Equal(e.GetProperty("heightFeet").GetInt32(), phg.HeightFeet);
                    Assert.Equal(e.GetProperty("gainDb").GetInt32(), phg.GainDbi);
                    Assert.Equal(IntOrNull(e.GetProperty("directivity")), phg.Directivity);
                    Near(e.GetProperty("rangeMiles").GetDouble(), phg.RangeMiles, 0.06, id, "phg.rangeMiles");
                    break;
                case "dfs":
                    Assert.NotNull(report.DirectionFindingStrength);
                    var dfs = report.DirectionFindingStrength;
                    Assert.Equal(e.GetProperty("strength").GetInt32(), dfs.Strength);
                    Assert.Equal(e.GetProperty("heightFeet").GetInt32(), dfs.HeightFeet);
                    Assert.Equal(e.GetProperty("gainDb").GetInt32(), dfs.GainDbi);
                    Assert.Equal(IntOrNull(e.GetProperty("directivity")), dfs.Directivity);
                    break;
                case "df":
                    Assert.NotNull(report.DirectionFinding);
                    DirectionFinding(e, report.DirectionFinding);
                    break;
                case "compressionType":
                    var type = Assert.NotNull(report.CompressionType);
                    Assert.Equal(Name(e, "gpsFix"), type.GpsFix.ToString().ToUpperInvariant());
                    Assert.Equal(Name(e, "nmeaSource"), type.NmeaSource.ToString().ToUpperInvariant());
                    Assert.Equal(Name(e, "origin"), type.Origin.ToString().ToUpperInvariant());
                    break;
                default:
                    Assert.Fail($"{id}: the test does not check expected field '{field.Name}' yet");
                    break;
            }
        }
    }

    private static void Symbol(JsonElement e, AprsSymbol symbol)
    {
        Assert.Equal(e.GetProperty("table").GetString()![0], symbol.WithoutOverlay.Table);
        Assert.Equal(e.GetProperty("code").GetString()![0], symbol.Code);
        var overlay = e.TryGetProperty("overlay", out var o) ? o.GetString()![0] : (char?)null;
        Assert.Equal(overlay, symbol.Overlay);
    }

    private static void Timestamp(JsonElement e, AprsTimestamp timestamp)
    {
        var format =
            e.GetProperty("format").GetString() == "HMS"
                ? AprsTimestampFormat.HourMinuteSecond
                : AprsTimestampFormat.DayHourMinute;
        Assert.Equal(format, timestamp.Format);
        Assert.Equal(e.GetProperty("hour").GetInt32(), timestamp.Hour);
        Assert.Equal(e.GetProperty("minute").GetInt32(), timestamp.Minute);
        if (e.TryGetProperty("day", out var day))
        {
            Assert.Equal(day.GetInt32(), timestamp.Day);
        }

        if (e.TryGetProperty("second", out var second))
        {
            Assert.Equal(second.GetInt32(), timestamp.Second);
        }

        if (e.TryGetProperty("zone", out var zone))
        {
            Assert.Equal(zone.GetString() == "local", timestamp.IsLocal);
        }
    }

    private static void DirectionFinding(JsonElement e, DirectionFindingReport df)
    {
        Assert.Equal(e.GetProperty("bearing").GetInt32(), df.Bearing);
        Assert.Equal(e.GetProperty("quality").GetInt32(), df.Quality);
        if (e.TryGetProperty("hits", out var hits))
        {
            Assert.Equal(hits.GetInt32(), df.Hits);
        }

        if (e.TryGetProperty("rangeMiles", out var range))
        {
            Assert.Equal(range.GetInt32(), df.RangeMiles);
        }

        if (e.TryGetProperty("fixedStation", out var fixedStation))
        {
            Assert.Equal(fixedStation.GetBoolean(), df.IsFixedStation);
        }
    }

    private static string Name(JsonElement e, string property) =>
        e.GetProperty(property).GetString()!.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    private static int? IntOrNull(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();

    private static void NearOrNull(JsonElement e, double? actual, double tolerance, string id, string field)
    {
        if (e.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(actual);
            return;
        }

        Near(e.GetDouble(), Assert.NotNull(actual), tolerance, id, field);
    }

    private static void Near(double expected, double actual, double tolerance, string id, string field) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"{id}: {field} expected {expected}, got {actual}");
}
