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

Only the header is decoded so far; payload decoding (positions, Mic-E, messages…) is in progress. See
[the design notes](https://github.com/APRS-NET/APRS.NET/blob/main/docs/design/packet-api.md).

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).

<sub>Developed with AI assistance; see the [AI declaration](https://github.com/APRS-NET/APRS.NET/blob/main/AI-DECLARATION.md).</sub>
