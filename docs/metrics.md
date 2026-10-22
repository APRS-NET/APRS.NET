# Metrics

The packages publish counters through `System.Diagnostics.Metrics`, each on a meter named after the package:
**`AprsNet.AprsIsTcpClient`**, **`AprsNet.Kiss`** ([below](#kiss)), **`AprsNet.Gateway`** ([below](#gateway)) and
**`AprsNet.IGate`** ([below](#igate)).
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

On the meter `AprsNet.Kiss`. Every client instrument has a `tnc` tag: the TNC's `host:port`, the port name for a
`KissSerialConnection`, or `stream` for a `KissStreamClient` on a stream the app opened.

| Instrument | Unit | Tags | Counts |
| --- | --- | --- | --- |
| `aprsnet.kiss.frames.received` | `{frame}` | `tnc` | AX.25 frames read from the TNC on the client's radio port |
| `aprsnet.kiss.frames.sent` | `{frame}` | `tnc` | Frames sent to the TNC to transmit |
| `aprsnet.kiss.frames.dropped` | `{frame}` | `tnc` | KISS frames dropped for an invalid escape or for being too long: a noisy link |
| `aprsnet.kiss.bytes.discarded` | `By` | `tnc` | Bytes from the TNC that were not part of a frame passed on: noise, or text from a TNC not in KISS mode |
| `aprsnet.kiss.connection.active` | `{connection}` | `tnc` | Up-down counter: connections to the TNC open now, over TCP or a serial port |
| `aprsnet.kiss.connection.reconnects` | `{reconnect}` | `tnc`, `cause`: `tnc_closed`, `socket_error`, `connect_failed` | Waits before a `KissTcpConnection` or `KissSerialConnection` reconnects; `socket_error` is any failure of the link, a serial port's included |

A `KissTcpServer`'s instruments have a `server` tag instead: the address it listens on, such as `0.0.0.0:8001`.

| Instrument | Unit | Tags | Counts |
| --- | --- | --- | --- |
| `aprsnet.kiss.server.clients.connected` | `{client}` | `server` | Up-down counter: apps connected now |
| `aprsnet.kiss.server.clients.disconnected` | `{client}` | `server`, `reason`: `closed`, `overflow`, `server_stopping`, `server_full` | Apps that left, were dropped for falling behind (`overflow`), or were turned away because `maxClients` were connected (`server_full`) |
| `aprsnet.kiss.server.frames.published` | `{frame}` | `server` | Frames given to `Publish` while an app was connected; one frame counts once however many apps it went to |
| `aprsnet.kiss.server.frames.received` | `{frame}` | `server` | AX.25 frames apps sent |
| `aprsnet.kiss.server.frames.transmit_failures` | `{frame}` | `server` | Frames from apps that `Transmit` failed to send, such as while the TNC was not connected |

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

## IGate

On the meter `AprsNet.IGate`. Every instrument has an `igate` tag, the IGate's callsign.

| Instrument | Unit | Tags | Counts |
| --- | --- | --- | --- |
| `aprsnet.igate.packets.gated` | `{packet}` | `igate` | Packets heard on RF that `RfToInternetGate` passed to APRS-IS |
| `aprsnet.igate.packets.refused` | `{packet}` | `igate`, `reason`: `not_aprs`, `unreadable`, `from_internet`, `no_gate`, `already_gated`, `general_query`, `empty`, `too_long`, `duplicate` | Packets heard on RF and not passed on, by the `GateRefusal` |

## aprscli

`aprscli tnc share` adds one instrument of its own, on the meter `AprsNet.Cli`.

| Instrument | Unit | Tags | Counts |
| --- | --- | --- | --- |
| `aprscli.tnc_share.frame.wait` | `s` | `tnc` | Histogram: how long each frame from an app waited for the TNC to finish the one before, from when it arrived to when it was written to the TNC |

## Useful queries

- **Feed health:** `rate(lines.received{kind="packet"})` should be steady for a fixed filter; a drop to zero while
  `kind="server_comment"` continues is a stalled server feed, the case `MaxPacketSilence` reconnects on.
- **Decoding coverage:** `packets.received{outcome="unsupported"}` and `{outcome="malformed"}` against the total show
  how much traffic the parser does not understand, by `type`.
- **Flapping:** a rising `connection.reconnects` by `cause` says whether the network, the server or the feed is at
  fault. The KISS causes match, so one query covers APRS-IS and TNCs.
- **RF against APRS-IS:** in a gateway, `packets.duplicates{source="tnc"}` against `packets.received{source="tnc"}`
  shows how much of what the TNC hears already reached APRS-IS through other IGates.
- **A shared TNC:** `kiss.server.clients.disconnected{reason="overflow"}` is an app that stopped reading;
  `{reason="server_full"}` is one turned away. A `tnc_share.frame.wait` that keeps growing is apps sending faster than
  the channel carries; `kiss.server.frames.transmit_failures` rising is frames lost while the TNC was away.
- **IGate coverage:** `packets.gated` against `packets.refused{reason="duplicate"}` shows how often digipeaters repeat
  what the IGate hears; a high `from_internet` count is a two-way IGate nearby transmitting APRS-IS traffic.

## Not yet

Planned but not implemented yet: bytes received, the queue length, and seconds since the last keepalive.
