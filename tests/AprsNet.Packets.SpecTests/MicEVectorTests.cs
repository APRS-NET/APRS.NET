namespace AprsNet.Packets.SpecTests;

/// <summary>
/// Checks every decoded field of the APRS101 Mic-E vectors, using the position checks for the shared fields.
/// Superseded vectors (the original Mic-E telemetry, replaced by APRS 1.2 type codes) and the rejected truncated
/// vector are left out. An expected field this test does not know fails, so new vector fields get checked.
/// </summary>
public class MicEVectorTests
{
    public static TheoryData<string> MicEVectorIds() =>
        [
            .. VectorFileTests
                .VectorIds()
                .Select(row => row.Data)
                .Where(id =>
                {
                    var vector = VectorFileTests.Vector(id);
                    return vector.GetProperty("status").GetString() != "superseded"
                        && vector.GetProperty("expect").TryGetProperty("type", out var type)
                        && type.GetString() == "mice";
                }),
        ];

    [Theory]
    [MemberData(nameof(MicEVectorIds))]
    public void DecodesTheVector(string id)
    {
        var vector = VectorFileTests.Vector(id);
        var packet = AprsPacket.Parse(vector.GetProperty("packet").GetString()!);

        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        var report = Assert.IsType<MicEReport>(packet.Payload);
        foreach (var field in vector.GetProperty("expect").EnumerateObject())
        {
            var e = field.Value;
            switch (field.Name)
            {
                case "type":
                // Destination SSID path routing (WIDE2-2 from SSID 2) is a header feature not decoded yet.
                case "destination":
                    break;
                case "current":
                    Assert.Equal(e.GetBoolean(), report.IsCurrentFix);
                    break;
                case "messageCode":
                    Assert.Equal(Code(report.Message), e.GetString());
                    break;
                case "messageName":
                    Assert.Equal(e.GetString(), Name(report.Message));
                    break;
                case "device":
                    // APRS101 calls the TH-D7 what the aprs-deviceid list calls the TH-D7A.
                    Assert.StartsWith(e.GetString()!, report.Device?.Model ?? "", StringComparison.Ordinal);
                    break;
                case "statusText":
                    Assert.Equal(e.GetString(), report.Comment);
                    break;
                case "altitudeMeters":
                    Assert.Equal(e.GetDouble(), report.AltitudeFeet!.Value * 0.3048, 6);
                    break;
                case "grid":
                    Assert.Equal(e.GetString(), report.MaidenheadLocator);
                    break;
                default:
                    Assert.True(
                        PositionVectorTests.CheckPositionField(id, field.Name, e, report),
                        $"{id}: the test does not check expected field '{field.Name}' yet"
                    );
                    break;
            }
        }
    }

    [Fact]
    public void TheTruncatedVectorIsRejected()
    {
        var packet = AprsPacket.Parse(VectorFileTests.Vector("ch10.mice-truncated").GetProperty("packet").GetString()!);

        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Contains(packet.Issues, i => i.Code == ParseIssueCode.PositionTooShort);
    }

    private static string Code(MicEMessage message) =>
        message switch
        {
            MicEMessage.Emergency => "Emergency",
            MicEMessage.Unknown => "Unknown",
            >= MicEMessage.Custom0 => $"C{message - MicEMessage.Custom0}",
            _ => $"M{(int)message}",
        };

    private static string Name(MicEMessage message) =>
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
}
