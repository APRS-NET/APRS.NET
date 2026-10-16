# Metrics

The packages publish counters through `System.Diagnostics.Metrics`, each on a meter named after the package:
**`AprsNet.AprsIsTcpClient`**, **`AprsNet.Kiss`** ([below](#kiss)) and **`AprsNet.Gateway`** ([below](#gateway)).
Nothing is recorded unless something listens: add the meters to an OpenTelemetry `MeterProvider`
(`.AddMeter("AprsNet.AprsIsTcpClient")`), or use a `MeterListener`. `aprscli` exports them over OTLP when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set.

No series grows with the traffic: tag values come from fixed lists, or name something the app set up (a TNC, a gateway
server or source), never a callsign or packet text. The APRS-IS client's instruments come first.

## Receiving

| Instrument | Unit | Tags | Counts |
| --- | --- | --- | --- |
| `aprsnet.aprsis.lines.received` | `{line}` | `kind`: `packet`, `server_comment` | Every line read from the server |
| `aprsnet.aprsis.packets.received` | `{packet}` | `type`, `outcome` (below) | Every packet read with `ReadPacketsAsync`, after parsing |
| `aprsnet.aprsis.packets.addressed_to_user` | `{packet}` | `kind`: `message`, `ack`, `rej` | Messages, acknowledgements and rejections addressed to the login callsign (any case, exact SSID), read with `ReadPacketsAsync`; bulletins are not counted |
| `aprsnet.aprsis.packets.parse_failures` | `{packet}` | `reason`: `empty`, `bad_header`, `bad_path`, `truncated`, `bad_body` | Malformed packets read with `ReadPacketsAsync`, by their first error; the packet's `Issues` hold the detail |
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

## KISS

On the meter `AprsNet.Kiss`. Every instrument has a `tnc` tag: the TNC's `host:port`, the port name for a
`KissSerialConnection`, or `stream` for a `KissStreamClient` on a stream the app opened.

| Instrument | Unit | Tags | Counts |
| --- | --- | --- | --- |
| `aprsnet.kiss.frames.received` | `{frame}` | `tnc` | AX.25 frames read from the TNC on the client's radio port |
| `aprsnet.kiss.frames.sent` | `{frame}` | `tnc` | Frames sent to the TNC to transmit |
| `aprsnet.kiss.frames.dropped` | `{frame}` | `tnc` | KISS frames dropped for an invalid escape or for being too long: a noisy link |
| `aprsnet.kiss.connection.active` | `{connection}` | `tnc` | Up-down counter: connections to the TNC open now, over TCP or a serial port |
| `aprsnet.kiss.connection.reconnects` | `{reconnect}` | `tnc`, `cause`: `tnc_closed`, `socket_error`, `connect_failed` | Waits before a `KissTcpConnection` or `KissSerialConnection` reconnects; `socket_error` is any failure of the link, a serial port's included |

## Gateway

On the meter `AprsNet.Gateway`. Every instrument has a `server` tag, the server's `ServerName`; the packet counters
from a `GatewayFeed` also have a `source` tag, the name the app gave the source.

| Instrument | Unit | Tags | Counts |
| --- | --- | --- | --- |
| `aprsnet.gateway.clients.connected` | `{client}` | `server`, `protocol`: `aprs-is`, `websocket` | Up-down counter: clients connected now, including any still logging in |
| `aprsnet.gateway.logins` | `{login}` | `server`, `verified`: `true`, `false` | Client logins, by whether the passcode verified them |
| `aprsnet.gateway.clients.disconnected` | `{client}` | `server`, `protocol`, `reason`: `closed`, `invalid_login`, `login_timeout`, `invalid_message` (WebSocket), `overflow`, `server_stopping`, `server_full`, `error` | Clients disconnected or refused |
| `aprsnet.gateway.packets.received` | `{packet}` | `server`, `source` | Packets a `GatewayFeed` read, duplicates included |
| `aprsnet.gateway.packets.duplicates` | `{packet}` | `server`, `source` | Packets a `GatewayFeed` dropped because a source had already heard them |
| `aprsnet.gateway.packets.published` | `{packet}` | `server` | Packets given to the server to send |
| `aprsnet.gateway.packets.queued` | `{packet}` | `server`, `protocol` | Packets queued for a client because its filter passed them; one packet may be queued for many clients |

## Useful queries

- **Feed health:** `rate(lines.received{kind="packet"})` should be steady for a fixed filter; a drop to zero while
  `kind="server_comment"` continues is a stalled server feed, the case `MaxPacketSilence` reconnects on.
- **Decoding coverage:** `packets.received{outcome="unsupported"}` and `{outcome="malformed"}` against the total show
  how much traffic the parser does not understand, by `type`.
- **Flapping:** a rising `connection.reconnects` by `cause` says whether the network, the server or the feed is at
  fault. The KISS causes match, so one query covers APRS-IS and TNCs.
- **RF against APRS-IS:** in a gateway, `packets.duplicates{source="tnc"}` against `packets.received{source="tnc"}`
  shows how much of what the TNC hears already reached APRS-IS through other IGates.

## Not yet

Planned but not implemented yet: bytes received, the queue length, and seconds since the last keepalive.
