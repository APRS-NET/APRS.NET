namespace AprsNet.Packets.SpecTests;

/// <summary>
/// Checks every expected field of the APRS101 station capabilities (ch. 15), user-defined (ch. 18) and raw weather
/// (ch. 12) vectors. An expected field this test does not know fails, so new vector fields get checked.
/// </summary>
public class CapabilitiesAndUserDefinedVectorTests
{
    private static readonly string[] _types = ["capabilities", "userDefined", "rawWeather"];

    public static TheoryData<string> VectorIds() =>
        [
            .. VectorFileTests
                .VectorIds()
                .Select(row => row.Data)
                .Where(id =>
                    VectorFileTests.Vector(id).GetProperty("expect").TryGetProperty("type", out var type)
                    && _types.Contains(type.GetString())
                ),
        ];

    [Fact]
    public void FindsTheVectors()
    {
        // One capabilities, two user-defined and four raw weather vectors: a typo in a type would skip them silently.
        Assert.Equal(7, VectorIds().Count);
    }

    [Theory]
    [MemberData(nameof(VectorIds))]
    public void DecodesTheVector(string id)
    {
        var vector = VectorFileTests.Vector(id);
        var packet = AprsPacket.Parse(vector.GetProperty("packet").GetString()!);

        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.Empty(packet.Issues);
        foreach (var field in vector.GetProperty("expect").EnumerateObject())
        {
            var e = field.Value;
            switch (field.Name, packet.Payload)
            {
                case ("type", _):
                    break;
                case ("capabilities", CapabilitiesReport report):
                    Assert.Equal(
                        e.EnumerateObject().Select(p => new KeyValuePair<string, string?>(p.Name, p.Value.GetString())),
                        report.Capabilities
                    );
                    break;
                case ("userId", UserDefinedPayload user):
                    Assert.Equal(e.GetString(), user.UserId.ToString());
                    break;
                case ("packetType", UserDefinedPayload user):
                    Assert.Equal(e.GetString(), user.UserPacketType.ToString());
                    break;
                case ("data", UserDefinedPayload user):
                    Assert.Equal(e.GetString(), user.Data);
                    break;
                case ("experimental", UserDefinedPayload user):
                    Assert.Equal(e.GetBoolean(), user.IsExperimental);
                    break;
                case ("station", RawWeatherReport raw):
                    Assert.Equal(
                        e.GetString(),
                        raw.Format == RawWeatherFormat.PeetBrosUII ? "Peet Bros U-II" : "Ultimeter 2000"
                    );
                    break;
                default:
                    Assert.Fail($"{id}: the test does not check expected field '{field.Name}' yet");
                    break;
            }
        }
    }
}
