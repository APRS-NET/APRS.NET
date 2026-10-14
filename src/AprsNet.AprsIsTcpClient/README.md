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

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).

<sub>Developed with AI assistance; see the [AI declaration](https://github.com/APRS-NET/APRS.NET/blob/main/AI-DECLARATION.md).</sub>
