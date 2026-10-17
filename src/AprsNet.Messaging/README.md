# AprsNet.Messaging

APRS message conversations for one station: message IDs, retries until an ack or rej, APRS 1.1 reply-acks, and acks
for the messages it receives, each delivered once. It does no I/O: hand it every packet you receive, from APRS-IS, a
KISS TNC or both, and send what it hands back.

```csharp
using AprsNet.Messaging;
using AprsNet.Packets;

using var station = new MessagingStation(Callsign.Parse("N0CALL-9"));
var address = PacketAddress.Parse("N0CALL-9", "APZNET", []);

// Everything received, from every source; replies go back the way a station was heard.
station.Receive(packet, source: "aprs-is");

// What to send, as it falls due: messages, retries, acks and rejs.
await foreach (var outgoing in station.ReadOutgoingAsync(cancellationToken))
{
    await connection.SendAsync(address.Create(outgoing.Payload), cancellationToken);
}

var message = station.Send(Callsign.Parse("WU2Z"), "Hello");
message.StateChanged += (_, _) => Console.WriteLine(message.State); // Sent, Retrying, Delivered, Rejected, Failed
message.Attempted += (_, _) => Console.WriteLine($"Try {message.Attempts}, next at {message.NextDue}"); // each retry
station.MessageReceived += (_, e) => Console.WriteLine($"{e.Message.From}: {e.Message.Text}");
```

Messages to one station go one at a time, retried on a `RetryProfile` (`Default`, `AprsIs`, `SlowHf` or `None`) that
never resends inside APRS-IS's 30-second duplicate window. Save `LastMessageId` and pass it back after a restart so
IDs don't repeat.

`BulletinBoard` keeps the bulletins, announcements and NWS bulletins heard, from anyone: hand it every packet, and its
`Changed` event says what was added, replaced (new text from the same sender) or removed (not heard for a day).

```csharp
using var board = new BulletinBoard();
board.Changed += (_, e) => Console.WriteLine($"{e.Change}: {e.Bulletin.From} {e.Bulletin.Addressee}: {e.Bulletin.Text}");
board.Receive(packet, source: "aprs-is");
```

See [the design](https://github.com/APRS-NET/APRS.NET/blob/main/docs/design/messaging.md).

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).
