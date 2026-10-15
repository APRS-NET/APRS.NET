# Metrics

`AprsNet.AprsIsTcpClient` publishes counters through `System.Diagnostics.Metrics` on the meter
**`AprsNet.AprsIsTcpClient`**. Nothing is recorded unless something listens: add the meter to an OpenTelemetry
`MeterProvider` (`.AddMeter("AprsNet.AprsIsTcpClient")`), or use a `MeterListener`. `aprscli` exports them over OTLP
when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

Every tag value comes from a fixed list below, so no series grows with the traffic (no callsigns, servers or text).

## Receiving

| Instrument | Unit | Tags | Counts |
| --- | --- | --- | --- |
| `aprsnet.aprsis.lines.received` | `{line}` | `kind`: `packet`, `server_comment` | Every line read from the server |
| `aprsnet.aprsis.packets.received` | `{packet}` | `type`, `outcome` (below) | Every packet read with `ReadPacketsAsync`, after parsing |
| `aprsnet.aprsis.packets.addressed_to_user` | `{packet}` | `kind`: `message`, `ack`, `rej` | Messages, acknowledgements and rejections addressed to the login callsign (any case, exact SSID), read with `ReadPacketsAsync`; bulletins are not counted |
| `aprsnet.aprsis.packets.dropped` | `{packet}` | `reason`: `queue_full`, `too_long` | Lines discarded before delivery: the bounded queue was full under `OverflowPolicy.DropOldest`, or a line was longer than `MaxLineLength` |

`packets.received` tags:

- `type`: `unknown`, `position`, `mic_e`, `object`, `item`, `message`, `status`, `weather`, `telemetry`,
  `third_party`, `capabilities`, `user_defined` (the `PacketType` names in snake case).
- `outcome`: `parsed` (decoded), `unsupported` (a valid packet of a type APRS.NET does not decode, or no known data
  type), `malformed` (could not be decoded; the packet's `Issues` say why).

Only `ReadPacketsAsync` parses, so an app that reads raw lines with `ReadLinesAsync` gets `lines.received` but not
`packets.received`.

## Sending

| Instrument | Unit | Tags | Counts |
| --- | --- | --- | --- |
| `aprsnet.aprsis.packets.sent` | `{packet}` | | Packets written to the server by `SendAsync` |
| `aprsnet.aprsis.bytes.sent` | `By` | | Every byte written: the login, filter commands and packets, CR LF included |

## Connection

| Instrument | Unit | Tags | Counts |
| --- | --- | --- | --- |
| `aprsnet.aprsis.connection.attempts` | `{attempt}` | `outcome`: `connected`, `failed`, `timeout` | Connect and login attempts |
| `aprsnet.aprsis.login.results` | `{login}` | `result`: `verified`, `unverified`, `rejected` | Server login responses |
| `aprsnet.aprsis.connection.active` | `{connection}` | | Up-down counter: 1 while a client is logged in and reading, back to 0 when its connection ends |
| `aprsnet.aprsis.connection.idle_timeouts` | `{timeout}` | | Connections ended because the server sent nothing, not even a keepalive, for `IdleTimeout` |
| `aprsnet.aprsis.connection.reconnects` | `{reconnect}` | `cause` (below) | Automatic reconnects by `AprsIsConnection` |

`connection.reconnects` causes: `server_closed` (the server ended the connection), `socket_error` (the link failed),
`keepalive_timeout` (nothing for `IdleTimeout`), `packet_silence` (keepalives but no packets for
`MaxPacketSilence`), `connect_failed` (a reconnect attempt itself failed).

## Useful queries

- **Feed health:** `rate(lines.received{kind="packet"})` should be steady for a fixed filter; a drop to zero while
  `kind="server_comment"` continues is a stalled server feed, the case `MaxPacketSilence` reconnects on.
- **Decoding coverage:** `packets.received{outcome="unsupported"}` and `{outcome="malformed"}` against the total show
  how much traffic the parser does not understand, by `type`.
- **Flapping:** a rising `connection.reconnects` by `cause` says whether the network, the server or the feed is at
  fault.

## Not yet

Planned but not implemented yet: bytes received, parse-failure reasons, the queue length, and seconds since the last
keepalive.
