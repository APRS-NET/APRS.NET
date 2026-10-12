# APRS.NET

An async-first, dependency-light .NET library family for APRS: parse and build packets, connect to APRS-IS, and later bridge radios and clients through KISS, TCP and WebSocket services.

Status: early scaffolding (M0).

## Layout

| Path | Contents |
| --- | --- |
| `src/AprsNet.Packets` | Packet models, parser, encoder (no I/O) |
| `src/AprsNet.AprsIsTcpClient` | APRS-IS client |
| `apps/AprsNet.AprsIsCli` | The `aprscli` dotnet tool |
| `tests/` | Unit tests and the packet corpus |

## Build

```sh
dotnet build
dotnet test
```

Licensed under the [MIT License](LICENSE).
