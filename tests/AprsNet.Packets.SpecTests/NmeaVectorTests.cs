namespace AprsNet.Packets.SpecTests;

/// <summary>
/// Checks every decoded field of the APRS101 raw NMEA vectors (p. 34). Vectors about the symbol a tracker sending raw
/// NMEA takes from its destination or SSID (p. 92) are left out: that symbol isn't decoded yet.
/// </summary>
public class NmeaVectorTests
{
    public static TheoryData<string> NmeaVectorIds() =>
        [
            .. VectorFileTests
                .VectorIds()
                .Select(row => row.Data)
                .Where(id =>
                    VectorFileTests.Vector(id).GetProperty("expect") is var expect
                    && expect.TryGetProperty("type", out var type)
                    && type.GetString() == "nmea"
                    && !expect.TryGetProperty("symbol", out _)
                ),
        ];

    [Theory]
    [MemberData(nameof(NmeaVectorIds))]
    public void DecodesTheVector(string id)
    {
        var vector = VectorFileTests.Vector(id);
        var packet = AprsPacket.Parse(vector.GetProperty("packet").GetString()!);

        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.Empty(packet.Issues);
        var report = Assert.IsType<NmeaReport>(packet.Payload);
        foreach (var field in vector.GetProperty("expect").EnumerateObject())
        {
            switch (field.Name)
            {
                case "type":
                    break;
                case "sentence":
                    Assert.Equal(field.Value.GetString(), report.Sentence.Type);
                    break;
                case "latitude":
                    Assert.Equal(field.Value.GetDouble(), report.Fix.Position!.Value.Latitude, 0.000001);
                    break;
                case "longitude":
                    Assert.Equal(field.Value.GetDouble(), report.Fix.Position!.Value.Longitude, 0.000001);
                    break;
                case "altitudeMeters":
                    Assert.Equal(field.Value.GetDouble(), report.Fix.AltitudeMeters!.Value, 0.5);
                    break;
                case "speed":
                    Assert.Equal(field.Value.GetDouble(), report.Fix.SpeedKnots!.Value, 0.5);
                    break;
                case "course":
                    Assert.Equal(field.Value.GetDouble(), report.Fix.Course!.Value, 0.5);
                    break;
                case "checksumValid":
                    Assert.Equal(field.Value.GetBoolean(), report.Sentence.IsChecksumValid);
                    Assert.NotNull(report.Sentence.Checksum);
                    break;
                default:
                    Assert.Fail($"{id}: the test does not check expected field '{field.Name}' yet");
                    break;
            }
        }
    }
}
