# Getting started with the gateway

`AprsNet.Gateway` runs a local APRS-IS server. Clients (APRSIS32, Xastir, YAAC, `aprscli listen`) log in to it as they
would to a public server, each with its own filter, and it serves them what you feed it: an APRS-IS connection, your
TNCs, or both, with each packet sent once however many sources heard it.

```sh
dotnet add package AprsNet.Gateway --prerelease
dotnet add package AprsNet.Kiss --prerelease   # for TNC sources
```

`aprscli serve` does all of this from the command line; this guide does it in code.

## Serve an APRS-IS feed

Make one read-only connection upstream with a broad filter, everything your clients may ask for, and feed it to the
server:

<!-- snippet: gateway-aprs-is -->
```csharp
// A local APRS-IS server on this computer's usual APRS-IS port.
await using var server = new AprsIsServer(
    new AprsIsServerOptions { ServerName = "MYGATE", EndPoint = new IPEndPoint(IPAddress.Loopback, 14580) }
);
server.Start();

// One read-only upstream connection with a broad filter: everything local clients may ask for.
await using var aprsIs = new AprsIsConnection(
    new AprsIsClientOptions { Callsign = "N0CALL", Filter = "r/38.98/-94.67/300" }
);
await aprsIs.StartAsync(cancellationToken);

// Serve until cancelled; the connection reconnects by itself.
await using var feed = new GatewayFeed(server);
await feed.AddSource("aprs-is", aprsIs.ReadPacketsAsync(cancellationToken));
```

Connect a client: `aprscli listen --server 127.0.0.1:14580 -f "r/38.98/-94.67/50 t/w"`. Each client's filter narrows
the feed; messages to a client's callsign reach it whatever its filter.

## Add a TNC

Add what a TNC hears as another source. A packet heard on the air and again on APRS-IS within 30 seconds is served
once, and the feed counts each source's packets and duplicates:

<!-- snippet: gateway-tnc -->
```csharp
await using var tnc = new KissTcpConnection("localhost");
var hearing = feed.AddSource("tnc", tnc.ReadPacketsAsync(cancellationToken));

// Later: what each source delivered, and how much of it the other source had already heard.
foreach (var (source, counts) in feed.GetSourceCounts())
{
    Console.WriteLine($"{source}: {counts.Packets} packets, {counts.Duplicates} duplicates");
}
```

The [TNC guide](tnc.md) covers setting up Dire Wolf.

## History, stations and more clients

`AprsIsServerOptions` sets what the server keeps and who may connect:

<!-- snippet: gateway-history -->
```csharp
var options = new AprsIsServerOptions
{
    ServerName = "MYGATE",
    EndPoint = new IPEndPoint(IPAddress.Any, 14580), // serve the whole network
    History = TimeSpan.FromMinutes(30), // new clients get the last 30 minutes their filter passes
    Stations = new StationStore(), // last positions, for m/ and f/ filters
    MaxClients = 50,
};
```

With a `StationStore`, the server remembers where stations were last heard, so clients can use `m/` (near me) and
`f/` (near a friend) filters. A client that falls 1,000 lines behind is disconnected rather than slowing the others.

## Watch it

The `AprsNet.Gateway` meter counts clients, logins, packets served and duplicates by source; add it to OpenTelemetry
with `AddMeter("AprsNet.Gateway")`. Add `AprsNet.AprsIsTcpClient` and `AprsNet.Kiss` for the upstream connection and
the TNCs. [docs/metrics.md](../metrics.md) lists the instruments.

## Next

- Clients are read-only for now: what they send is ignored.
- The [gateway design notes](../design/gateway.md) explain how filters, history and slow clients are handled.
