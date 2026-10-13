using System.Text.Json;

namespace AprsNet.Packets.SpecTests;

/// <summary>
/// Checks every decoded field of the APRS101 object and item vectors, using the position checks for the fields they
/// share with position reports. An expected field this test does not know fails, so new vector fields get checked.
/// </summary>
public class ObjectVectorTests
{
    public static TheoryData<string> ObjectVectorIds() =>
        [
            .. VectorFileTests
                .VectorIds()
                .Select(row => row.Data)
                .Where(id =>
                    VectorFileTests.Vector(id).GetProperty("packet").GetString()!.Split(':', 2)[1][0] is ';' or ')'
                ),
        ];

    [Theory]
    [MemberData(nameof(ObjectVectorIds))]
    public void DecodesTheVector(string id)
    {
        var vector = VectorFileTests.Vector(id);
        var packet = AprsPacket.Parse(vector.GetProperty("packet").GetString()!);

        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        var payload = Assert.IsAssignableFrom<PositionedPayload>(packet.Payload);
        var (name, live) = payload switch
        {
            ObjectReport o => (o.Name, o.IsLive),
            ItemReport i => (i.Name, i.IsLive),
            _ => throw new InvalidOperationException($"{id}: not an object or item"),
        };
        foreach (var field in vector.GetProperty("expect").EnumerateObject())
        {
            var e = field.Value;
            switch (field.Name)
            {
                case "type":
                    Assert.Equal(e.GetString() == "item", payload is ItemReport);
                    break;
                case "name":
                    Assert.Equal(e.GetString(), name);
                    break;
                case "alive":
                    Assert.Equal(e.GetBoolean(), live);
                    break;
                case "area":
                    Area(e, Assert.IsType<AreaShape>(payload.Area));
                    break;
                case "signpost":
                    Assert.Equal(e.GetString(), payload.Signpost);
                    break;
                // Weather in an object comment and storm data (\@) belong to the weather decoder, not decoded yet.
                case "weather" or "storm":
                    break;
                default:
                    Assert.True(
                        PositionVectorTests.CheckPositionField(id, field.Name, e, payload),
                        $"{id}: the test does not check expected field '{field.Name}' yet"
                    );
                    break;
            }
        }
    }

    private static void Area(JsonElement e, AreaShape area)
    {
        Assert.Equal(e.GetProperty("type").GetInt32(), area.Type);
        Assert.Equal(e.GetProperty("color").GetInt32(), area.Color);
        Assert.Equal(e.GetProperty("yy").GetInt32(), area.LatitudeCode);
        Assert.Equal(e.GetProperty("xx").GetInt32(), area.LongitudeCode);
        var corridor = e.TryGetProperty("corridorMiles", out var c) ? c.GetInt32() : (int?)null;
        Assert.Equal(corridor, area.CorridorMiles);
    }
}
