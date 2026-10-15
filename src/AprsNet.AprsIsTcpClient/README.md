# AprsNet.AprsIsTcpClient

Async APRS-IS client.

> **Alpha pre-release.** APRS.NET packages are versioned `0.x.y-alpha.N` until at least 1.0.0, and the API can change
> between any two releases. Install with `--prerelease`. Not for production use.

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

`AprsMessenger` handles messages: it gives each one an ID, resends it until the recipient acknowledges or rejects
it (after 30 s, 1, 2, 4 and 8 minutes by default), and acknowledges messages sent to this station. Pass it every
received packet:

```csharp
var address = PacketAddress.Parse("N0CALL-9", "APZ123", []);
await client.SendAsync(address.Create(new StatusReport { Text = "On the air" }));

using var messenger = new AprsMessenger(address, client.SendAsync);
_ = Task.Run(async () =>
{
    await foreach (var line in client.ReadLinesAsync())
        if (await messenger.HandleAsync(AprsPacket.Parse(line)) is { } message)
            Console.WriteLine($"Message: {message.Text}");
});

var delivery = await messenger.SendAsync("WU2Z", "Hello there");
Console.WriteLine(delivery.Status); // Acknowledged, Rejected or NotAcknowledged
```

A login filter of `g/<callsign>` makes sure messages and acknowledgements addressed to the station reach it.

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
