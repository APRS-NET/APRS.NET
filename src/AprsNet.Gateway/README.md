# AprsNet.Gateway

A local APRS-IS server. Clients log in as they would to a public APRS-IS server and receive the packets you publish
that pass their filter.

```csharp
await using var server = new AprsIsServer(new AprsIsServerOptions { ServerName = "APRSNET" });
server.Start();
server.Publish(packet);
```

Not published yet: this is groundwork for the phase 3 gateway.
