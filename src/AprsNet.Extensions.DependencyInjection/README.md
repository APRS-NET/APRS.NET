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

For TLS, set `"UseTls": true` and a server that offers it, such as `"Server": "ssl.aprs2.net:24580"`. A certificate
check of your own (`ServerCertificateValidation`) or client certificates (`ClientCertificates`) can't come from
configuration; set them with `services.Configure<AprsIsClientOptions>(o => …)`.

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).
