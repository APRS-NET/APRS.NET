# Gateway design

Phase 3 of the roadmap: a local server that behaves like APRS-IS, so existing APRS clients (APRSdroid, Xastir, YAAC,
`aprscli listen`) can connect to it and each get their own filter, fed by APRS-IS, a KISS TNC, or both.

It is built in layers, each its own pull request:

| Layer | Type | Pull request |
| --- | --- | --- |
| Filter expressions | `AprsFilter`, `AprsFilterContext` (`AprsNet.Filters`) | #67 |
| Server | `AprsIsServer`, `AprsIsServerOptions` (`AprsNet.Gateway`) | #68 |
| De-duplication and history | `GatewayFeed`, `AprsIsServerOptions.History` | #69, #106 |
| Metrics | the `AprsNet.Gateway` meter | #71 |
| Station store | `StationStore`, `AprsIsServerOptions.Stations` | #72 |
| Command line | `aprscli serve` | #70 |

Neither project is packable yet.

## Filters

`AprsFilter` parses the server-side filter syntax of javAPRSSrvr and aprsc and evaluates it against parsed packets.
A packet passes when any term matches and no `-` term does, so a filter with only exclusions passes nothing, as on
APRS-IS.

Where the servers' documentation is vague, the corpus decides. Every capture in `tests/corpus` was filtered by a real
server, and `CorpusFilterTests` runs the same filter locally:

- the 7 type captures (`t/i`, `t/m`, `t/n`, `t/s`, `t/t`, `t/u`, `t/w`) match on every packet;
- the 34 region captures disagree on 6 positions in all. Servers also pass packets by the sender's last known
  position, and a position with ambiguity on the edge of the circle can fall either side.

That check settled two choices. `t/m` includes NWS bulletins (they are messages), and `t/n` includes `NWS_`
addressees as well as `NWS-`. Type letters map to payloads:

| Letter | Payloads |
| --- | --- |
| `p` | position reports and Mic-E (a station's own position; objects and items have their own letters) |
| `o`, `i` | objects, items |
| `m` | every message, and telemetry definitions (which are messages) |
| `q` | directed queries, and `?` general queries |
| `s`, `t`, `u` | status, telemetry reports and definitions, user-defined |
| `n` | NWS bulletins (`NWS-` and `NWS_`) |
| `w` | positionless weather, raw weather station data, and any position with weather |

Terms that need a station's position (`m/`, `f/`, `t/…/call/km`) look it up through `AprsFilterContext` and pass
nothing without one. With a `StationStore` the gateway supplies one, as aprsc does: `m/` measures from wherever the
client's own login callsign was last heard, and `f/` and `t/…/call/km` from the named station's last position.

Not supported: `q/con/I` (it needs a store of IGate positions) and `os/` (strict object names with spaces). Third-party
packets are matched on their outer header.

## Server

The wire protocol follows what APRS-IS clients expect from aprsc:

1. On connect, a banner: `# <software> <version>`.
2. The client sends `user CALL [pass N] [vers NAME VERSION] [filter …]` within `LoginTimeout` (30 s), or is
   disconnected. A line that is not a login also disconnects.
3. `# logresp CALL verified|unverified, server NAME`. Verified means the passcode matches the callsign.
4. A login filter gets `# filter … active`, or `# filter … is invalid: …`, which keeps the previous filter (none at
   login).
5. Then the stream: packets that pass the filter, messages addressed to the login callsign whatever the filter, and a
   keepalive comment every `KeepAliveInterval` (20 s). `#filter …` replaces the filter at any time.

Decisions:

- **Loopback by default.** `EndPoint` defaults to `127.0.0.1:14580`, so nothing is exposed unless asked for.
- **Read-only clients.** Lines from clients other than `#filter` are ignored, verified or not. Accepting packets from
  clients needs the roadmap's authentication and rate limits, and a decision on where they go (upstream, RF, or only
  to other local clients).
- **Slow clients are dropped.** Each client has a bounded queue (`ClientQueueLength`, 1,000 lines). `Publish` never
  blocks; a client whose queue is full is disconnected, as aprsc does, rather than delaying everyone.
- **`Publish` is the only input.** The host decides where packets come from, usually through a `GatewayFeed`, which
  merges APRS-IS and KISS sources and publishes each packet once; `aprscli serve` uses one.
- **Lines go out as received.** The server sends each packet's original bytes, so nothing is re-encoded.

## De-duplication and history

`GatewayFeed` applies the APRS-IS rule through `DuplicateFilter` from `AprsNet.Packets` across all its sources, and
counts what it drops by source: the same source, destination and information field within 30 s of the first copy is a duplicate, whatever its
path. The window runs from the first copy and is not extended by later ones,
so a beacon every 30 s is never dropped. On a 10-minute full-feed capture, already de-duplicated by the server, it drops
2 of 54,468.

`History` (off by default) keeps recent published packets, at most `MaxHistoryPackets`. A client that logs in first
gets the ones its filter passes, then the live feed. `Publish` and a client joining take turns under one lock, so a
joining client gets each packet exactly once and in order.

## Station store

`StationStore` remembers, per callsign, when it was last heard and its last position. Only a station's own position
counts (position reports, Mic-E, positioned weather), not the objects and items it places elsewhere. The server records
every published packet before filtering it, so a station's own packet is measured from where it has just reported. It
is in memory and bounded (100,000 stations by default), forgetting the station heard least recently first.

## WebSocket clients

`AprsWebSocketHub` subscribes to an `AprsIsServer`, so WebSocket clients get the same packets, history and station
store as APRS-IS clients, published once. `GatewayWebServer` accepts them without ASP.NET Core, so `aprscli` runs on
the plain .NET runtime; an ASP.NET Core app hands its own accepted sockets to `HandleAsync` instead.

1. A client connects to `ws://host:port/aprs`, optionally with `?filter=…` (APRS-IS syntax) and `?call=…`.
   `GatewayWebServer` listens on 127.0.0.1:14590 by default, answers other requests with a short HTTP error, and can
   limit browsers to `AllowedOrigins`. It does not serve TLS; put a reverse proxy in front for `wss://`.
2. The server sends `{"server":…,"software":…,"filter":…}`, then the history its filter passes, then the live feed:
   one text message per packet, in the [JSON format](../json.md).
3. `{"filter":"…"}` from the client replaces the filter; the answer is `{"filter":…,"active":true}` or
   `{"filter":…,"error":…}`. Anything else gets `{"error":…}`. Messages to `call` always pass, and `m/` measures from
   where `call` was last heard.

Decisions:

- **Read-only**, as for APRS-IS clients.
- **Slow clients are dropped** when their queue (1,000 packets) fills: the connection is cut at once, without a close
  handshake, since the client isn't reading.
- **Pings** keep connections open every 30 seconds.
- **Metrics** are the APRS-IS server's, with a `protocol` tag (`aprs-is` or `websocket`) on the client instruments.

## Health

`GatewayHealth` says whether the gateway is serving. The app adds each source with a check that says whether it is
connected (an APRS-IS connection's `State`, a TNC's `IsConnected`); given the `GatewayFeed`, it also reports when each
source last delivered a packet. With every source connected, or none added, it is healthy; with some, degraded; with
none, unhealthy.

`GatewayWebServer` answers `GET /health` with it: `200` while healthy or degraded, `503` when unhealthy, so an
orchestrator restarts only a gateway that serves nothing new. The body is JSON:
`{"status":"degraded","checkedAt":…,"clients":{"aprsIs":2,"webSocket":1},"sources":{"aprs-is":{"connected":true,"lastPacket":…},"tnc":{"connected":false,"lastPacket":null}}}`.
`aprscli serve --http` serves both routes.

## Still to do

- Accepting packets from verified clients, with authentication and per-client rate limits.
- Track history in the station store, persistence, and `q/con/I` (which needs IGate positions).
