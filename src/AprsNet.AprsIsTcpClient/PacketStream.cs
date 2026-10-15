using System.Runtime.CompilerServices;
using AprsNet.Packets;

namespace AprsNet.AprsIsTcpClient;

/// <summary>Turns a stream of packet lines into parsed packets, counting each by type and outcome.</summary>
internal static class PacketStream
{
    /// <param name="lines">The packet lines.</param>
    /// <param name="callsign">The login callsign, for counting messages addressed to it.</param>
    /// <param name="cancellationToken">Ends the stream.</param>
    public static async IAsyncEnumerable<AprsPacket> ParseAsync(
        IAsyncEnumerable<string> lines,
        string callsign,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await foreach (var line in lines.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            // Lines are read as Latin-1, one character per byte, which is what Parse(string) expects.
            var packet = AprsPacket.Parse(line);
            ClientMetrics.PacketParsed(packet.Type, packet.Outcome);
            if (packet.Outcome == ParseOutcome.Malformed)
            {
                ClientMetrics.ParseFailure(packet.Issues.First(i => i.Severity == ParseIssueSeverity.Error).Code);
            }
            if (
                packet.Payload is MessagePayload message
                && message.Addressee.Equals(callsign, StringComparison.OrdinalIgnoreCase)
                && message.Kind switch
                {
                    MessageKind.Message => "message",
                    MessageKind.Acknowledgement => "ack",
                    MessageKind.Rejection => "rej",
                    _ => null,
                }
                    is { } kind
            )
            {
                ClientMetrics.AddressedToUser(kind);
            }

            yield return packet;
        }
    }
}
