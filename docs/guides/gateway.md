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

## Gate to APRS-IS

An IGate uploads what its TNC hears to APRS-IS. `RfToInternetGate` from `AprsNet.IGate` applies the aprs-is.net
rules: it refuses packets that came from APRS-IS (`TCPIP`, `TCPXX`), that ask not to be gated (`NOGATE`, `RFONLY`),
general queries and copies already sent, and adds `qAO` and the IGate's callsign to the rest. Send them over a
verified login:

<!-- snippet: gateway-igate -->
```csharp
// A verified login as the IGate. Nothing reads its lines here, so let old ones go rather than stall the link.
await using var uplink = new AprsIsConnection(
    new AprsIsClientOptions
    {
        Callsign = "N0CALL-10",
        Passcode = passcode,
        SendInterval = TimeSpan.FromMilliseconds(200), // a busy channel sends more than one station
        OverflowPolicy = OverflowPolicy.DropOldest,
    }
);
await uplink.StartAsync(cancellationToken);

var gate = new RfToInternetGate(Callsign.Parse("N0CALL-10")); // receive-only: adds qAO
await foreach (var heard in tnc.ReadPacketsAsync(cancellationToken))
{
    var decision = gate.Gate(heard);
    if (decision.IsGated)
    {
        await uplink.SendAsync(decision.Packet, cancellationToken);
    }
}
```

`aprscli serve --igate` does this alongside serving, and beacons the IGate's position to APRS-IS as IGates are expected
to. Each refusal has a reason (`decision.Refusal`), counted on the `AprsNet.IGate` meter.

This is a receive-only IGate, and receive-only IGates break messaging. A station nearby sends a message, sees it reach
APRS-IS and expects the reply on RF, but nothing brings the reply back. Run one only where no two-way IGate covers the
area, or for a short while, and say so in the beacon comment (`aprscli` does). A two-way IGate, which gates messages
back to RF, comes in a later release.

## Watch it

The `AprsNet.Gateway` meter counts clients, logins, packets served and duplicates by source; add it to OpenTelemetry
with `AddMeter("AprsNet.Gateway")`. Add `AprsNet.AprsIsTcpClient` and `AprsNet.Kiss` for the upstream connection and
the TNCs. [docs/metrics.md](../metrics.md) lists the instruments.

## Next

- To run the gateway as a service on Linux, see [apps/services](../../apps/services/README.md).
- Clients are read-only for now: what they send is ignored.
- The IGate is receive-only for now.
- The [gateway design notes](../design/gateway.md) explain how filters, history and slow clients are handled.
