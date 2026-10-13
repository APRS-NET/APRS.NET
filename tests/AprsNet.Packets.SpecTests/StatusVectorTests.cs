namespace AprsNet.Packets.SpecTests;

/// <summary>
/// Checks every decoded field of the APRS101 status vectors. An expected field this test does not know fails, so new
/// vector fields get checked.
/// </summary>
public class StatusVectorTests
{
    public static TheoryData<string> StatusVectorIds() =>
        [
            .. VectorFileTests
                .VectorIds()
                .Select(row => row.Data)
                .Where(id =>
                    VectorFileTests.Vector(id).GetProperty("expect").TryGetProperty("type", out var type)
                    && type.GetString() == "status"
                ),
        ];

    [Theory]
    [MemberData(nameof(StatusVectorIds))]
    public void DecodesTheVector(string id)
    {
        var vector = VectorFileTests.Vector(id);
        var packet = AprsPacket.Parse(vector.GetProperty("packet").GetString()!);

        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        var status = Assert.IsType<StatusReport>(packet.Payload);
        foreach (var field in vector.GetProperty("expect").EnumerateObject())
        {
            var e = field.Value;
            switch (field.Name)
            {
                case "type":
                // Destination interpretation (generic, software, locator, ALTNET) is a header feature not decoded yet.
                case "destination":
                    break;
                case "text":
                    Assert.Equal(e.GetString(), status.Text);
                    break;
                case "grid":
                    Assert.Equal(e.GetString(), status.MaidenheadLocator);
                    break;
                case "symbol":
                    Assert.NotNull(status.Symbol);
                    Assert.Equal(e.GetProperty("table").GetString()![0], status.Symbol.Value.Table);
                    Assert.Equal(e.GetProperty("code").GetString()![0], status.Symbol.Value.Code);
                    break;
                case "beamHeading":
                    Assert.Equal(e.GetInt32(), status.BeamHeading);
                    break;
                case "erpWatts":
                    Assert.Equal(e.GetInt32(), status.EffectiveRadiatedPowerWatts);
                    break;
                case "timestamp":
                    var timestamp = Assert.NotNull(status.Timestamp);
                    Assert.Equal(AprsTimestampFormat.DayHourMinute, timestamp.Format);
                    Assert.Equal(e.GetProperty("day").GetInt32(), timestamp.Day);
                    Assert.Equal(e.GetProperty("hour").GetInt32(), timestamp.Hour);
                    Assert.Equal(e.GetProperty("minute").GetInt32(), timestamp.Minute);
                    Assert.False(timestamp.IsLocal);
                    break;
                default:
                    Assert.Fail($"{id}: the test does not check expected field '{field.Name}' yet");
                    break;
            }
        }
    }
}
