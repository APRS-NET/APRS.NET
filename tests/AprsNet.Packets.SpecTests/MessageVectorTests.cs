using System.Text.Json;

namespace AprsNet.Packets.SpecTests;

/// <summary>
/// Checks every decoded field of the APRS101 vectors that use the message format (<c>:ADDRESSEE:</c>): messages,
/// acknowledgements, bulletins, NWS bulletins, directed queries and telemetry definitions. An expected field this test
/// does not know fails, so new vector fields get checked.
/// </summary>
public class MessageVectorTests
{
    public static TheoryData<string> MessageVectorIds() =>
        [
            .. VectorFileTests
                .VectorIds()
                .Select(row => row.Data)
                .Where(id =>
                    VectorFileTests.Vector(id).GetProperty("packet").GetString()!.Split(':', 2)[1].StartsWith(':')
                ),
        ];

    [Theory]
    [MemberData(nameof(MessageVectorIds))]
    public void DecodesTheVector(string id)
    {
        var vector = VectorFileTests.Vector(id);
        var packet = AprsPacket.Parse(vector.GetProperty("packet").GetString()!);
        var expect = vector.GetProperty("expect");

        Assert.Equal(PacketType.Message, packet.Type);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        if (expect.GetProperty("type").GetString()!.StartsWith("telemetry", StringComparison.Ordinal))
        {
            Telemetry(id, expect, Assert.IsType<TelemetryDefinition>(packet.Payload));
        }
        else
        {
            Message(id, expect, Assert.IsType<MessagePayload>(packet.Payload));
        }
    }

    private static void Message(string id, JsonElement expect, MessagePayload message)
    {
        foreach (var field in expect.EnumerateObject())
        {
            var e = field.Value;
            switch (field.Name)
            {
                case "type":
                    Assert.Equal(
                        e.GetString() switch
                        {
                            "ack" => MessageKind.Acknowledgement,
                            "rej" => MessageKind.Rejection,
                            "bulletin" or "groupBulletin" => MessageKind.Bulletin,
                            "announcement" => MessageKind.Announcement,
                            "nwsBulletin" => MessageKind.NwsBulletin,
                            "query" => MessageKind.Query,
                            _ => MessageKind.Message,
                        },
                        message.Kind
                    );
                    break;
                case "addressee":
                    Assert.Equal(e.GetString(), message.Addressee);
                    break;
                case "text":
                    Assert.Equal(e.GetString(), message.Text);
                    break;
                case "messageId":
                    Assert.Equal(e.ValueKind == JsonValueKind.Null ? null : e.GetString(), message.MessageId);
                    break;
                case "ackRequired":
                    Assert.Equal(e.GetBoolean(), message.IsAckRequired);
                    break;
                case "bulletinId":
                    Assert.Equal(e.GetString(), message.BulletinId);
                    break;
                case "group":
                    Assert.Equal(e.GetString(), message.BulletinGroup);
                    break;
                case "queryType":
                    Assert.Equal(e.GetString(), message.QueryType);
                    break;
                case "directed":
                    Assert.Equal(e.GetBoolean(), message.Kind == MessageKind.Query);
                    break;
                case "callsign":
                    Assert.Equal(e.GetString(), message.QueryArgument);
                    break;
                // NTS radiogram routing inside the message text (N1\...) is not decoded yet.
                case "nts":
                    break;
                default:
                    Assert.Fail($"{id}: the test does not check expected field '{field.Name}' yet");
                    break;
            }
        }
    }

    private static void Telemetry(string id, JsonElement expect, TelemetryDefinition definition)
    {
        foreach (var field in expect.EnumerateObject())
        {
            var e = field.Value;
            switch (field.Name)
            {
                case "type":
                    Assert.Equal(
                        e.GetString() switch
                        {
                            "telemetryParameters" => TelemetryDefinitionKind.ParameterNames,
                            "telemetryUnits" => TelemetryDefinitionKind.Units,
                            "telemetryEquations" => TelemetryDefinitionKind.Equations,
                            _ => TelemetryDefinitionKind.BitSense,
                        },
                        definition.Kind
                    );
                    break;
                case "addressee":
                    Assert.Equal(e.GetString(), definition.Addressee);
                    break;
                case "names" or "units":
                    Assert.Equal(e.EnumerateArray().Select(x => x.GetString()), definition.Labels);
                    break;
                case "coefficients":
                    Assert.Equal(
                        e.EnumerateArray()
                            .Select(t => new TelemetryEquation(t[0].GetDouble(), t[1].GetDouble(), t[2].GetDouble())),
                        definition.Equations
                    );
                    break;
                case "bits":
                    Assert.Equal(e.GetString(), definition.BitSense);
                    break;
                case "project":
                    Assert.Equal(e.GetString(), definition.ProjectName);
                    break;
                default:
                    Assert.Fail($"{id}: the test does not check expected field '{field.Name}' yet");
                    break;
            }
        }
    }
}
