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

For web apps, an `AprsWebSocketHub` streams the same packets as JSON (in the `AprsNet.Packets.Json` format), each
client with its own filter, and `GatewayWebServer` accepts WebSocket connections for it without ASP.NET Core:

```csharp
await using var hub = new AprsWebSocketHub(server);
await using var web = new GatewayWebServer(hub);   // ws://127.0.0.1:14590/aprs?filter=r/35/-97/200
web.Start();

// With a health check at /health: 200 while any source is up, 503 when none is. With CountQuietAsDown, a TNC quiet
// for its QuietWarning counts as down, as when it is switched off behind a USB port that stays open.
var health = new GatewayHealth(feed) { CountQuietAsDown = true };
health.AddSource("aprs-is", () => aprsIs.State == AprsIsConnectionState.Connected);
health.AddSource("tnc", () => tnc.IsConnected, () => tnc.IsQuiet);
health.AddCounter("igate.gated", () => gated); // counts reported with each check, such as an IGate's
await using var withHealth = new GatewayWebServer(hub, new GatewayWebServerOptions { Health = health });

// Only health checks, for a service with nothing to stream such as one sharing a TNC: /aprs answers 404.
await using var healthOnly = new GatewayWebServer(new GatewayWebServerOptions { Health = health });

// Or, in an ASP.NET Core app:
app.Map("/aprs", async context => await hub.HandleAsync(
    await context.WebSockets.AcceptWebSocketAsync(), context.Request.Query["filter"], context.Request.Query["call"],
    context.RequestAborted));
```

Start with the [gateway guide](https://github.com/APRS-NET/APRS.NET/blob/main/docs/guides/gateway.md).

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).
