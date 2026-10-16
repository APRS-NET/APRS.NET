# AprsNet.Packets.Json

APRS packets as JSON: one documented object per packet, with when it was received, the raw line, the header, parse
issues and the decoded fields of its type. `aprscli listen --format json` prints the same objects.

```csharp
using AprsNet.Packets;
using AprsNet.Packets.Json;

var packet = AprsPacket.Parse("N0CALL-9>APRS,WIDE1-1:!4903.50N/07201.75W>088/036Mobile");
Console.WriteLine(AprsPacketJson.Serialize(packet, DateTimeOffset.UtcNow));
// {"received":"…","raw":"N0CALL-9>APRS,WIDE1-1:!4903.50N/07201.75W>088/036Mobile","source":"N0CALL-9",
//  "destination":"APRS","path":["WIDE1-1"],"type":"position","outcome":"parsed","issues":[],
//  "position":{"latitude":49.058333,"longitude":-72.029167,…,"course":88,"speedKnots":36,…,"comment":"Mobile"}}

using var writer = new Utf8JsonWriter(stream, AprsPacketJson.WriterOptions);
AprsPacketJson.Write(writer, packet, receivedAt);             // into a larger document
```

It writes with `Utf8JsonWriter`, with no reflection, so it trims and compiles ahead of time. It only writes: to read a
packet back, parse its `raw` line. Field names and units are public API; see
[the format](https://github.com/APRS-NET/APRS.NET/blob/main/docs/json.md).

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).
