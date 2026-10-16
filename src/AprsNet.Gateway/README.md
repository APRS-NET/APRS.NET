# AprsNet.Gateway

A local APRS-IS server. Clients log in as they would to a public APRS-IS server and receive the packets you publish
that pass their filter.

```csharp
await using var server = new AprsIsServer(new AprsIsServerOptions { ServerName = "APRSNET" });
server.Start();
server.Publish(packet);
```

To serve what an APRS-IS connection and local TNCs hear, add each as a source of a `GatewayFeed`. A packet heard on
several sources within 30 seconds is published once:

```csharp
await using var feed = new GatewayFeed(server);
_ = feed.AddSource("aprs-is", aprsIs.ReadPacketsAsync());
_ = feed.AddSource("tnc", tnc.ReadPacketsAsync());   // a KissTcpConnection from AprsNet.Kiss
```

`feed.GetSourceCounts()` gives each source's packets and duplicates, and the `aprsnet.gateway.packets.received` and
`aprsnet.gateway.packets.duplicates` metrics count them with a `source` tag.

Not published yet: this is groundwork for the phase 3 gateway.
