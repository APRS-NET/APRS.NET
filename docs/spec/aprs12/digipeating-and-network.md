---
id: aprs12.network
spec: APRS 1.2 Addendum (draft/proposals) — operating improvements
status: proportional pathing & decay = recommended (in Kenwood D710, KPC-3+ digis); RGATING / smart digis = proposals
topic: Decay algorithm, proportional pathing, network stewardship, IS-to-RF gating, local info, ALOHA circle, Voice Alert
affects_aprs101_pages: [11, 12]
sources:
  - https://www.aprs.org/newN/ProportionalPathing.txt   # 10 Nov 2008
  - https://www.aprs.org/txt/stewardship.txt            # 5 Sep 2004 / May 2005
  - https://www.aprs.org/aprs12/IS-to-RF-b.txt          # 28 Dec 2011
  - https://www.aprs.org/digis/smartdigis.txt
  - https://www.aprs.org/localinfo.html
  - https://www.aprs.org/aloha.html
  - https://www.aprs.org/VoiceAlert3.html
  - https://www.aprs.org/fix14439.html
  - source/aprs.org/ (same paths)
see_also:
  - ../aprs11/ax25-ssid-digipeating.md
  - operator-precedence-rr-bits.md   # preemptive digipeating
---

# APRS 1.2 — Digipeating and network operation

## A12-NET-01 — Decaying beacon algorithm (fundamental)

- New/changed data is sent **now**; if unchanged, **double** the interval each time, down to a
  maximum of **30 min** (10 min for operator-present/local-net cases).
- Applies to positions, objects, messages (retries) and status. Add **randomisation** (±10 %) to all
  timers; spread multiple objects.
- Example (mobile set to 1 min): stops → 2, 4, 8, 16, 30 min; resets to 1 min on movement.
- Net-cycle default by hops: **10 min** for local/1-hop, **30 min** for longer paths.

## A12-NET-02 — Proportional pathing

Beacon rate drops with hop count. Mobile defaults (base rate 1 min while changing):

| Every | Path | (RATE /2, HOPS 2) |
|---|---|---|
| 1 min | direct (no digi) | |
| 2 min | `WIDE1-1` | |
| 4 min | `WIDE1-1,WIDE2-1` | |
| 8 min | `WIDE1-1,WIDE2-2` | only if HOPS = 3 |

RATE `/3` variant: 1, 3, 9, 27 min. User settings should be just **HOPS 2|3** and **RATE /2|/3**.
Digis: direct every 10 min, 1-hop every 30 min, 2-hop every 60 min (A11-DIGI-01).
Combined with decay this cuts per-mobile load ~8× moving and ~30× stopped.

## A12-NET-03 — Network stewardship features (client checklist)

Auto net-cycle (10 local / 30 wide); ALOHA circle display; default path `WIDE2-2`; challenge &
confirm hops > 3 and paths past a gate; no `RELAY`; force TNC `HID OFF` and TNC beacon OFF; decay
messages/posits/objects; randomise timings; terminate message retries after N; retry on heard;
redundant ACKs and reply-ACKs; analysis tools (ALOHA range, hops, paths, PHG circles); heard counts
per station per hour; HID monitor; channel-load display.

## A12-NET-04 — ALOHA circle

A 1200-baud channel supports roughly **60–100** stations in an RF domain. The ALOHA circle is the
radius containing the nearest ~60 stations (findu used "60 nearest"). Clients should compute and
always display it; your packets should not routinely go beyond it.

## A12-NET-05 — IS-to-RF selective gating ("RGATING", rev b proposal)

- IS clients (phone apps) injecting positions must use path `CALL>APxxxx,TCPIP*:`, obey normal rates
  (30 min fixed, 10 min operator-present, ≤ 1/min mobiles), use the decay algorithm, and include
  their voice frequency (`FFF.FFFMHz`) if they have a radio.
- RGate sysop parameters: `RGATE0/1/2 RANGE` (direct/1-hop/2-hop coverage areas),
  `DIRECT/ONEHOP/TWOHOP RATE` (max per-station rate), `RGATE0/1/2 LIMIT` (packets/min failsafe),
  `NOFREQ-NOGATE`, block list. Rev A (TCPIP* in any digi field) abandoned.

## A12-NET-06 — Local info initiative

Each digi transmits **direct**, every 10 min, up to 3 local-info objects: recommended travelers'
voice repeater, EchoLink/IRLP/AllStar nodes, nets in progress — formats in `frequency-afrs.md`.
IGates pass selected VoIP-node objects to RF via ≤ 1 hop.

## A12-NET-07 — Voice Alert

Mobiles keep the APRS radio on 144.39 with **CTCSS 100 Hz** decode (Europe 136.5 Hz; Australia
91.5 Hz) and transmit their beacons with CTCSS 100, so only *direct* (undigipeated) mobile pings are
heard — a simplex proximity alert and voice calling channel.

## A12-NET-08 — Alternate input channel

Local users transmit on an alternate input (e.g. **144.99**) that a digi cross-digipeats to 144.39,
avoiding out-of-area QRM at the digi input. Digi overlay `A#`.

## A12-NET-09 — Smart digis (informational)

WB4APR is skeptical (non-determinism, inconsistent adoption); the notes list proposed directives
for reference only. Not normative. See `source/aprs.org/digis/smartdigis.txt`.

## A12-NET-10 — Digipeater object caching

See `objects.md` (A12-OBJ-03).
