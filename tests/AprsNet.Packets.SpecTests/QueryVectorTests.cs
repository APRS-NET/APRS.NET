namespace AprsNet.Packets.SpecTests;

/// <summary>Checks every decoded field of the APRS101 query vectors (ch. 15): general and directed queries.</summary>
public class QueryVectorTests
{
    public static TheoryData<string> QueryVectorIds() =>
        [
            .. VectorFileTests
                .VectorIds()
                .Select(row => row.Data)
                .Where(id =>
                    VectorFileTests.Vector(id).GetProperty("expect").TryGetProperty("type", out var type)
                    && type.GetString() == "query"
                ),
        ];

    [Theory]
    [MemberData(nameof(QueryVectorIds))]
    public void DecodesTheVector(string id)
    {
        var vector = VectorFileTests.Vector(id);
        var packet = AprsPacket.Parse(vector.GetProperty("packet").GetString()!);
        var expect = vector.GetProperty("expect");
        var directed = expect.TryGetProperty("directed", out var d) && d.GetBoolean();

        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.Empty(packet.Issues);
        string? queryType,
            addressee = null,
            argument;
        QueryKind kind;
        QueryFootprint? footprint = null;
        if (directed)
        {
            var message = Assert.IsType<MessagePayload>(packet.Payload);
            (queryType, kind, addressee, argument) = (
                message.QueryType,
                message.QueryKind,
                message.Addressee,
                message.QueryArgument
            );
        }
        else
        {
            var query = Assert.IsType<QueryReport>(packet.Payload);
            (queryType, kind, footprint, argument) = (query.QueryType, query.Kind, query.Footprint, query.Argument);
        }

        Assert.NotEqual(QueryKind.Unknown, kind);
        foreach (var field in expect.EnumerateObject())
        {
            switch (field.Name)
            {
                case "type":
                    break;
                case "directed":
                    Assert.Equal(field.Value.GetBoolean(), packet.Type == PacketType.Message);
                    break;
                case "queryType":
                    Assert.Equal(field.Value.GetString(), queryType);
                    break;
                case "addressee":
                    Assert.Equal(field.Value.GetString(), addressee);
                    break;
                case "callsign":
                    Assert.Equal(field.Value.GetString(), argument);
                    break;
                case "footprint":
                    Assert.NotNull(footprint);
                    Assert.Equal(field.Value.GetProperty("latitude").GetDouble(), footprint.Value.Center.Latitude, 6);
                    Assert.Equal(field.Value.GetProperty("longitude").GetDouble(), footprint.Value.Center.Longitude, 6);
                    Assert.Equal(field.Value.GetProperty("radiusMiles").GetInt32(), footprint.Value.RadiusMiles);
                    break;
                default:
                    Assert.Fail($"{id}: the test does not check expected field '{field.Name}' yet");
                    break;
            }
        }
    }
}
