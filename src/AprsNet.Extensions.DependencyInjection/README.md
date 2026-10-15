# AprsNet.Extensions.DependencyInjection

Registers an `AprsIsConnection` and its `AprsIsClientOptions` with `Microsoft.Extensions.DependencyInjection`, bound
from configuration.

```json
{
  "AprsIs": {
    "Server": "noam",
    "Callsign": "N0CALL-10",
    "Filter": "r/38.98/-94.67/100",
    "ReconnectDelay": "00:00:05"
  }
}
```

```csharp
builder.Services.AddAprsIsConnection(builder.Configuration.GetSection("AprsIs"));

var connection = app.Services.GetRequiredService<AprsIsConnection>();
await connection.StartAsync();
```

`Server` is a region key (`noam`, `soam`, `euro`, `asia`, `aunz`) or `host[:port]`; it defaults to
`rotate.aprs2.net:14580`. Every other key is a property of `AprsIsClientOptions`. Keep `Passcode` out of files you
commit: set it with an environment variable (`AprsIs__Passcode`) or user secrets.

Not published yet.
