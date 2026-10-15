using System.Runtime.CompilerServices;
using AprsNet.Packets;

namespace AprsNet.AprsIsTcpClient;

/// <summary>Turns a stream of packet lines into parsed packets, counting each by type and outcome.</summary>
internal static class PacketStream
{
    public static async IAsyncEnumerable<AprsPacket> ParseAsync(
        IAsyncEnumerable<string> lines,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await foreach (var line in lines.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            // Lines are read as Latin-1, one character per byte, which is what Parse(string) expects.
            var packet = AprsPacket.Parse(line);
            ClientMetrics.PacketParsed(packet.Type, packet.Outcome);
            yield return packet;
        }
    }
}
