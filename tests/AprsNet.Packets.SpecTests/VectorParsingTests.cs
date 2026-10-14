namespace AprsNet.Packets.SpecTests;

/// <summary>
/// Runs the APRS101 test vectors through <see cref="AprsPacket.Parse(string)"/>. Until the payload decoders exist this
/// checks the header and, where the data type identifier alone decides it, the packet type.
/// </summary>
public class VectorParsingTests
{
    // Vector types that only the data type identifier decides. The others (position, weather, query, NMEA…) can start
    // with several identifiers or need the payload decoded, so they are checked when their decoders land.
    private static readonly Dictionary<string, PacketType> _types = new(StringComparer.Ordinal)
    {
        ["mice"] = PacketType.MicE,
        ["object"] = PacketType.Object,
        ["item"] = PacketType.Item,
        ["message"] = PacketType.Message,
        ["ack"] = PacketType.Message,
        ["rej"] = PacketType.Message,
        ["bulletin"] = PacketType.Message,
        ["announcement"] = PacketType.Message,
        ["groupBulletin"] = PacketType.Message,
        ["nwsBulletin"] = PacketType.Message,
        ["telemetryParameters"] = PacketType.Message,
        ["telemetryUnits"] = PacketType.Message,
        ["telemetryEquations"] = PacketType.Message,
        ["telemetryBits"] = PacketType.Message,
        ["status"] = PacketType.Status,
        ["telemetry"] = PacketType.Telemetry,
        ["thirdParty"] = PacketType.ThirdParty,
        ["capabilities"] = PacketType.Capabilities,
        ["userDefined"] = PacketType.UserDefined,
        ["rawWeather"] = PacketType.Weather,
        ["unknown"] = PacketType.Unknown,
        ["invalid"] = PacketType.Unknown,
    };

    public static TheoryData<string> VectorIds() => VectorFileTests.VectorIds();

    public static TheoryData<string> TypedVectorIds() =>
        [.. VectorFileTests.VectorIds().Select(row => row.Data).Where(id => ExpectedType(id) is not null)];

    [Theory]
    [MemberData(nameof(VectorIds))]
    public void ParsesTheHeaderAndKeepsTheLine(string id)
    {
        var line = VectorFileTests.Vector(id).GetProperty("packet").GetString()!;

        var packet = AprsPacket.Parse(line);

        Assert.True(packet.HasHeader, $"{id}: {string.Join(", ", packet.Issues)}");
        if (
            VectorFileTests.Vector(id).GetProperty("expect").TryGetProperty("reject", out var reject)
            && reject.GetBoolean()
        )
        {
            // Vectors a decoder must reject (a truncated Mic-E report) parse their header but not their payload.
            Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        }
        else
        {
            Assert.DoesNotContain(packet.Issues, i => i.Severity == ParseIssueSeverity.Error);
        }
        Assert.Equal(line, packet.ToString());
        Assert.Equal(line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..], Latin1(packet.Information));
    }

    [Theory]
    [MemberData(nameof(TypedVectorIds))]
    public void TheDataTypeIdentifierGivesTheType(string id)
    {
        var line = VectorFileTests.Vector(id).GetProperty("packet").GetString()!;

        Assert.Equal(ExpectedType(id), AprsPacket.Parse(line).Type);
    }

    private static PacketType? ExpectedType(string id) =>
        VectorFileTests.Vector(id).GetProperty("expect").TryGetProperty("type", out var type)
        && _types.TryGetValue(type.GetString()!, out var expected)
            ? expected
            : null;

    private static string Latin1(ReadOnlyMemory<byte> bytes) => System.Text.Encoding.Latin1.GetString(bytes.Span);
}
