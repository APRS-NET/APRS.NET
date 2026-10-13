# AprsNet.Packets

APRS packet models, parser and encoder. No I/O.

> **Alpha pre-release.** APRS.NET packages are versioned `0.x.y-alpha.N` until at least 1.0.0, and the API can change
> between any two releases. Install with `--prerelease`. Not for production use.

## Parsing

`AprsPacket.Parse` never throws for malformed data. It always returns a packet holding the exact input, and reports
what it could not decode in `Outcome` and `Issues`.

```csharp
var packet = AprsPacket.Parse(line); // a TNC2 line: bytes, or a string read as Latin-1
if (!packet.HasHeader)
{
    Console.WriteLine($"Not a packet: {packet} ({string.Join(", ", packet.Issues)})");
    return;
}

Console.WriteLine($"{packet.Source} via {string.Join(",", packet.Path)}: {packet.Type}");
byte[] original = packet.GetBytes(); // exactly what was parsed, minus the line ending
```

Strings must hold one character per byte (Latin-1), as `AprsIsClient` returns them: APRS packets are bytes, and
Mic-E data is not valid UTF-8. `Parse(string)` throws `ArgumentException` for a character above U+00FF, because such
a string has already lost bytes.

Position reports are decoded: plain and compressed positions, ambiguity, timestamps, symbols, course and speed,
altitude, PHG, RNG, DFS, DF bearings and `!DAO!` precision. Status reports are decoded too: text, zulu timestamp,
Maidenhead locator and symbol, and meteor-scatter beam heading and power. Messages are decoded as `MessagePayload`
(text messages with IDs and APRS 1.1 reply-acks, acks, rejects, bulletins, announcements, NWS bulletins and directed
queries) or `TelemetryDefinition` (`PARM.`, `UNIT.`, `EQNS.`, `BITS.`). Other packet types (Mic-E, objects…) come
back with `Outcome == ParseOutcome.Unsupported` until their decoders land.

```csharp
if (packet.Payload is PositionReport report)
{
    Console.WriteLine($"{report.Position.Latitude:0.00000} {report.Position.Longitude:0.00000} {report.Symbol}");
    if (report.Course is { } course && report.SpeedKnots is { } speed) Console.WriteLine($"{course}° at {speed} kn");
}
```

See [the design notes](https://github.com/APRS-NET/APRS.NET/blob/main/docs/design/packet-api.md).

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).

<sub>Developed with AI assistance; see the [AI declaration](https://github.com/APRS-NET/APRS.NET/blob/main/AI-DECLARATION.md).</sub>
