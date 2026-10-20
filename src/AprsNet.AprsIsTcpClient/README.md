# AprsNet.AprsIsTcpClient

Async APRS-IS client.

> **Alpha pre-release.** APRS.NET packages are versioned `0.x.y-alpha.N` until at least 1.0.0, and the API can change
> between any two releases. Install with `--prerelease`. Not for production use.

## Receiving

`ReadPacketsAsync` yields each line parsed; `ReadLinesAsync` yields the raw lines instead (use one or the other).
Parsing never throws, so a malformed packet arrives with its `Outcome` and `Issues`. Every packet is counted in the
`aprsnet.aprsis.packets.received` metric by `type` and `outcome`; [docs/metrics.md](https://github.com/APRS-NET/APRS.NET/blob/main/docs/metrics.md)
lists every metric the client records.

```csharp
await foreach (var packet in client.ReadPacketsAsync())
    if (packet.Payload is PositionedPayload position)
        Console.WriteLine($"{packet.Source} at {position.Position.Latitude:0.0000}, {position.Position.Longitude:0.0000}");
```

### Without a callsign

Someone without a licence can still receive. Set `ListenOnly`, and the client logs in with a generated listener ID such
as `SWL482913` (`SWL` and six random digits, which no callsign can be), always read-only, and refuses to send:

```csharp
var options = new AprsIsClientOptions { ListenOnly = true, Filter = "r/38.98/-94.67/50" };
options.Validate();                                    // fills in options.Callsign, such as SWL482913
Console.WriteLine($"Listening as {options.Callsign}");
```

`AprsIsLogin.IsListenerId` tells a listener ID from a callsign, to show "listening as …" or refuse to set up
transmitting. A listener ID is too long for an AX.25 address, so it can never be a packet's source.

## Server presets

`AprsIsServerPreset.All` lists the aprs2.net rotates for a server picker, worldwide first, each with a stable key to
store and a region name to show. `TryFind` maps a stored key or server back to its preset:

```csharp
foreach (var preset in AprsIsServerPreset.All)
    Console.WriteLine($"{preset.Key,-6} {preset.Name,-17} {preset.Endpoint}"); // euro   Europe and Africa euro.aprs2.net:14580

if (AprsIsServerPreset.TryFind("euro.aprs2.net:14580", out var region))
    Console.WriteLine(region.Name); // Europe and Africa
```

## Sending

On a verified login (the passcode for the callsign, from `AprsIsPasscode.Compute`), `SendAsync` sends a packet built
with `PacketAddress`. Sends are spread out, 5 at once and then one every 6 seconds by default (`SendBurst`,
`SendInterval`); a sender waits for its turn rather than failing. Unverified logins, packets received from APRS-IS
(they carry a q-construct) and lines APRS-IS cannot carry are refused.

An IGate's packets are the exception: a packet heard on RF whose path ends in `qAR` or `qAO` and the login callsign,
as `RfToInternetGate` from [AprsNet.IGate](https://www.nuget.org/packages/AprsNet.IGate) builds it, is sent. An IGate
on a busy channel gates more than one packet every 6 seconds, so give it a shorter `SendInterval`.

```csharp
var address = PacketAddress.Parse("N0CALL-9", "APZ123", []);
await client.SendAsync(address.Create(new StatusReport { Text = "On the air" }));
```

For messages, which are resent until the recipient acknowledges them, use `MessagingStation` from
[AprsNet.Messaging](https://www.nuget.org/packages/AprsNet.Messaging): hand it every received packet and send what
it hands back.

A login filter of `g/<callsign>` makes sure messages and acknowledgements addressed to the station reach it.

## Staying connected

`AprsIsClient` is single-use: when its connection ends, so does its stream. For an app that should stay online, use
`AprsIsConnection`, which reconnects by itself after the first login: it waits `ReconnectDelay` (5 s), doubling up to
`MaxReconnectDelay` (60 s) with ±20% jitter, and starts the backoff again after a connection that held. It reconnects
when the server closes, the link fails, the server sends nothing for `IdleTimeout` (90 s; APRS-IS sends a keepalive
about every 20 s), or, if set, no packet arrives for `MaxPacketSilence`.

```csharp
await using var connection = new AprsIsConnection(options, logger);
connection.StateChanged += (_, e) => Console.WriteLine($"{e.State} {e.RetryIn} {e.Error?.Message}");
await connection.StartAsync(); // throws if the first login fails, so wrong settings show at once

await foreach (var packet in connection.ReadPacketsAsync()) // one stream across reconnects
    Handle(packet);
```

`SendAsync` waits while the connection is down and sends once it is back; `SetFilterAsync` applies now and on every
later login; one send rate limit covers every connection.

A failed login is a `LoginException`. Two kinds have their own type, because retrying won't help with either:
`LoginRejectedException` when the server refuses the login, and `KissTncLoginException` when the other end is a KISS
TNC, such as Dire Wolf's port 8001, rather than a server. Connect to that with `AprsNet.Kiss` instead. Check
`e.Error` in `StateChanged` for these types to tell the user what to change.

## Traffic

For a troubleshooting console, `Traffic` on `AprsIsClient` and `AprsIsConnection` reports every line to and from the
server: the login (with the passcode masked by the library), the server's answer, comments and keepalives, packets,
filter changes and packets sent. Nothing extra is done while no one listens, and nothing is kept: keep your own history.
`AprsIsConnection.SendCommandAsync` sends a server command typed by hand, such as `#filter b/N0CALL`; only lines
starting with `#`, so packets still go through `SendAsync` and its checks.

```csharp
connection.Traffic += (_, e) => console.Add($"{e.At:HH:mm:ss} {e.Direction} {e.Kind}: {e.Line}");
```

## TLS

Set `UseTls` to connect over TLS. The server must offer it, usually on port 24580 (`AprsIsPorts.Tls`), and its
certificate is checked against the host name unless you decide with `ServerCertificateValidation`:

```csharp
var options = new AprsIsClientOptions
{
    Endpoint = new AprsIsEndpoint("ssl.aprs2.net", AprsIsPorts.Tls),
    Callsign = "N0CALL",
    UseTls = true,
};
```

A failed handshake throws `AprsIsTlsException`, an `IOException` whose `Failure` says why: `CertificateRejected` (with
the `PolicyErrors`, such as a name mismatch) or `HandshakeFailed`. `AprsIsConnection` retries it like any other
connection failure.

Few APRS-IS servers offer TLS. Known: `ssl.aprs2.net:24580`, the aprs2.net servers running aprsc's experimental TLS
support, where a Logbook of The World certificate can replace the passcode (pass it in `ClientCertificates`; see
[APRSdroid's notes](https://aprsdroid.org/ssl)). APRS.NET's tests cover TLS against a local server; we have not yet
checked these servers ourselves.

## Errors in the user's language

Settings a person types (callsign, software name and version, filter, server address, passcode callsign) are refused
with `AprsValidationException`, an `ArgumentException` whose `Code` is a stable `AprsValidationErrorCode` to translate,
with `Index` and `Character` pointing at the offending character where there is one. `AprsIsEndpoint.Parse` does the
same for a typed `host:port`:

```csharp
try
{
    var endpoint = AprsIsEndpoint.Parse(serverText);
}
catch (AprsValidationException ex)
{
    ShowError(Localize(ex.Code), ex.Index); // ServerPortInvalid at 19, for "rotate.aprs2.net:14a80"
}
```

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).

<sub>Developed with AI assistance; see the [AI declaration](https://github.com/APRS-NET/APRS.NET/blob/main/AI-DECLARATION.md).</sub>
