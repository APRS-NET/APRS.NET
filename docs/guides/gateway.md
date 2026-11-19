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
and general queries, and adds `qAO` and the IGate's callsign to the rest. Copies heard through digipeaters go up too:
APRS-IS drops duplicates itself. Send them over a verified login:

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

`SendAsync` waits while the connection is down, and then sends. An IGate should not wait: drop what it hears until the
connection is back (`uplink.Login` is `null` while it is down), as aprsc's
[IGate hints](https://github.com/hessu/aprsc/blob/main/doc/IGATE-HINTS.md) ask. Another IGate has probably gated those
packets already, and a late copy makes a moving station jump back on the maps. `aprscli serve --igate` drops them, and
drops any packet it could not send within 10 s.

This is a receive-only IGate, and receive-only IGates break messaging. A station nearby sends a message, sees it reach
APRS-IS and expects the reply on RF, but nothing brings the reply back. Run one only where no two-way IGate covers the
area, or for a short while, and say so in the beacon comment (`aprscli` does).

### Reception metadata for IS2 servers (experimental)

IS2 servers (aprsc 3.0, the `is2.aprs2.net` pool) can carry how each packet was heard: RSSI, SNR, modem, bit rate and
frequency. Give the gate a `ReceptionMetadata` for the TNC and send the decision's `Reception` with the packet; the
uplink writes an `#is2-meta` line just before it, as
[aprsc's IS2PLAIN](https://github.com/hessu/aprsc/blob/main/doc/IS2PLAIN.md) describes. Other servers ignore the line.
The metadata never changes what the gate decides, and refused packets send nothing:

<!-- snippet: gateway-igate-is2 -->
```csharp
// How this TNC hears: a 1200 bit/s AX.25 receiver on 144.390 MHz. A KISS TNC reports no RSSI or SNR.
var reception = new ReceptionMetadata
{
    Modem = ReceptionModem.Ax25,
    BitRate = 1200,
    FrequencyHz = 144_390_000,
};
await foreach (var heard in tnc.ReadPacketsAsync(cancellationToken))
{
    var decision = gate.Gate(heard, reception);
    if (decision.IsGated)
    {
        await uplink.SendWithReceptionAsync(decision.Packet, decision.Reception, cancellationToken);
    }
}
```

The API is marked `[Experimental("APRSNET_IS2")]`, as IS2 is still changing upstream: suppress `APRSNET_IS2` to use
it. The types are in `AprsNet.Packets.Is2`.

### A two-way IGate

A two-way IGate also carries messages from APRS-IS back to RF, so stations nearby can message anyone. It sends very
little: only messages, acknowledgements and rejections for stations it has heard on RF recently over few hops, and the
sender's next position once, so the station can see who is writing. `InternetToRfGate` decides, from a
`HeardStations` that records everything the TNC hears; see [AprsNet.IGate](../../src/AprsNet.IGate/README.md#from-aprs-is-to-rf).

```sh
APRS_PASSCODE=12345 aprscli serve -c KD9XYZ-10 --tnc localhost --igate --igate-to-rf --lat 49.0583 --lon -72.0292
```

With `--igate-to-rf`, `serve` gates up with `qAR` instead of `qAO`, so APRS-IS knows it can reach stations it heard.
The server then sends it the messages for those stations, and their senders' positions, with no filter needed. It
beacons with the `I&` symbol.

- **Licence:** it transmits on its own, through the TNC, as the IGate's callsign: you need a licence, and the call must
  be yours.
- **Local stations:** a station counts as local when heard on RF within 180 minutes over at most the hops of
  `--rf-path` (default `WIDE1-1`, one hop): what it sends must reach the station. Use `WIDE1-1,WIDE2-1` only where one
  hop doesn't cover the stations the IGate hears.
- **Rate limits:** at most 6 packets a minute and 10 in five minutes go to RF, as Dire Wolf's `IGTXLIMIT`; more are
  refused, and the TNC's own transmit limit applies too. Copies of a message from two APRS-IS servers go out once.
- **Never:** bulletins, the rest of the feed, anything with `TCPXX`, `NOGATE` or `RFONLY`, anything gated from RF, or
  a message whose sender is local too.
- **One TNC:** `--igate-to-rf` takes exactly one `--tnc`, the one it transmits on.
- **Testing:** test against a Dire Wolf with `ADEVICE null null` before putting it on the air.

The summary at the end and `/health` count what went to RF (`igate.rf.gated`, `igate.rf.sent`), the local stations
(`igate.rf.local_stations`) and what was refused, by reason (`igate.rf.refused.not_local` and so on).

## Watch it

The `AprsNet.Gateway` meter counts clients, logins, packets served and duplicates by source; add it to OpenTelemetry
with `AddMeter("AprsNet.Gateway")`. Add `AprsNet.AprsIsTcpClient` and `AprsNet.Kiss` for the upstream connection and
the TNCs. [docs/metrics.md](../metrics.md) lists the instruments.

## Next

- To run the gateway as a service on Linux, see [apps/services](../../apps/services/README.md).
- Clients are read-only for now: what they send is ignored.
- The [gateway design notes](../design/gateway.md) explain how filters, history and slow clients are handled.
