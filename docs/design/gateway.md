# Gateway design

Phase 3 of the roadmap: a local server that behaves like APRS-IS, so existing APRS clients (APRSdroid, Xastir, YAAC,
`aprscli listen`) can connect to it and each get their own filter, fed by APRS-IS, a KISS TNC, or both.

It is built in layers, each its own pull request:

| Layer | Type | Pull request |
| --- | --- | --- |
| Filter expressions | `AprsFilter`, `AprsFilterContext` (`AprsNet.Filters`) | #67 |
| Server | `AprsIsServer`, `AprsIsServerOptions` (`AprsNet.Gateway`) | #68 |
| De-duplication and history | `PacketDeduplicator`, `AprsIsServerOptions.History` | #69 |
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
- **`Publish` is the only input.** The host decides where packets come from. `aprscli serve` reads APRS-IS; a KISS
  source would call `Publish` the same way.
- **Lines go out as received.** The server sends each packet's original bytes, so nothing is re-encoded.

## De-duplication and history

`PacketDeduplicator` applies the APRS-IS rule: the same source, destination and information field within 30 s of the
first copy is a duplicate, whatever its path. The window runs from the first copy and is not extended by later ones,
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

## Still to do

- Accepting packets from verified clients, with per-client rate limits.
- A WebSocket endpoint streaming JSON. This needs a JSON form of packets in a library; the CLI's `--format json` is
  the obvious starting point.
- Track history in the station store, persistence, and `q/con/I` (which needs IGate positions).
- A KISS source in `aprscli serve`, merged with APRS-IS through the deduplicator.
- A health endpoint and a container image.
