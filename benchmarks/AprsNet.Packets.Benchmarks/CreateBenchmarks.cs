using AprsNet.Packets;
using BenchmarkDotNet.Attributes;

namespace AprsNet.Packets.Benchmarks;

/// <summary>Building packets with <see cref="PacketAddress.Create(AprsPayload)"/>, which encodes and parses back.</summary>
[MemoryDiagnoser]
public class CreateBenchmarks
{
    private readonly PacketAddress _address = PacketAddress.Parse("N0CALL-9", "APZ123", ["WIDE1-1"]);

    private readonly PositionReport _position = new()
    {
        Position = new AprsPosition(49.05833, -72.02917),
        Symbol = new AprsSymbol('/', '>'),
        Course = 88,
        SpeedKnots = 36,
        Comment = "On the road",
    };

    private readonly MessagePayload _message = new()
    {
        Addressee = "WU2Z",
        Text = "Testing",
        MessageId = "003",
    };

    [Benchmark]
    public AprsPacket CreatePosition() => _address.Create(_position);

    [Benchmark]
    public AprsPacket CreateMessage() => _address.Create(_message);
}
