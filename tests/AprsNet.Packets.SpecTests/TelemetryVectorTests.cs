namespace AprsNet.Packets.SpecTests;

/// <summary>
/// Checks every decoded field of the APRS101 telemetry report vectors. An expected field this test does not know
/// fails, so new vector fields get checked.
/// </summary>
public class TelemetryVectorTests
{
    public static TheoryData<string> TelemetryVectorIds() =>
        [
            .. VectorFileTests
                .VectorIds()
                .Select(row => row.Data)
                .Where(id =>
                    VectorFileTests.Vector(id).GetProperty("expect").TryGetProperty("type", out var type)
                    && type.GetString() == "telemetry"
                ),
        ];

    [Theory]
    [MemberData(nameof(TelemetryVectorIds))]
    public void DecodesTheVector(string id)
    {
        var vector = VectorFileTests.Vector(id);
        var packet = AprsPacket.Parse(vector.GetProperty("packet").GetString()!);

        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        var report = Assert.IsType<TelemetryReport>(packet.Payload);
        foreach (var field in vector.GetProperty("expect").EnumerateObject())
        {
            var e = field.Value;
            switch (field.Name)
            {
                case "type":
                    break;
                case "sequence":
                    Assert.Equal(e.GetString(), report.Sequence);
                    break;
                case "analog":
                    Assert.Equal(e.EnumerateArray().Select(v => (double?)v.GetDouble()), report.Analog);
                    break;
                case "digital":
                    Assert.Equal(e.GetString(), string.Concat(report.Digital.Select(b => b ? '1' : '0')));
                    break;
                default:
                    Assert.Fail($"{id}: the test does not check expected field '{field.Name}' yet");
                    break;
            }
        }
    }
}
