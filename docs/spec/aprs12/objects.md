---
id: aprs12.objects
spec: APRS 1.2 Addendum (draft/proposals)
status: permanent objects (111111z) = in use for frequency/VoIP objects; caching = proposal (APRSdos only)
topic: Permanent objects, APOBJ, object caching / object manager (AP0Cxy)
affects_aprs101_pages: [57, 58, 59]
sources:
  - https://www.aprs.org/info/object-perm.txt   # 03 Feb 2007
  - https://www.aprs.org/aprs12/cached.txt      # 07 Jun 2004
  - source/aprs.org/info/object-perm.txt
  - source/aprs.org/aprs12/cached.txt
see_also:
  - ../aprs11/objects-items-names.md
  - messaging-extensions.md   # ITEM-in-MSG, MESSAGE OBJECT server
---

# APRS 1.2 — Objects: permanence and caching

## Baseline client rules (restated)

1. An object is unique by **name**; a new object with the same name is an update.
2. Retain the **sending station** of each object (attribution).
3. Name comparison is **case-sensitive**.

## A12-OBJ-01 — Permanent objects (`111111z`)

- An object with timestamp **`111111z`** is **permanent**: it must **not** be overwritten by an
  identically named object **unless** it comes from the **same originating station**.
- `111111z` was already the recommended "timeless" timestamp, so this is backward compatible.
- Motivation: many identically-named repeater objects (e.g. `146.940-…`) on APRS-IS would otherwise
  ping-pong. Alternatively make names unique with an ID suffix (see `frequency-afrs.md`).
- Implementation: store-update rule
  `if existing.Timestamp == "111111z" && incoming.Source != existing.Source ⇒ keep both / ignore incoming`
  (APRS-IS-wide displays should key such objects by `(name, source)`).

## A12-OBJ-02 — `APOBJ` 3rd-party permanent objects (NOT USED)

Proposed: a 3rd-party pseudo-station with TOCALL `APOBJ` flags permanence, e.g.
`BT }146.760->APOBJ:!DDMM.hhN/DDDMM.hhWrPHG5320 PL 107.3 Net Tu 9PM AARC W3VPR`.
Abandoned because UI-View treats every 3rd-party source as an IGate, and many IGates drop 3rd-party
packets. Recognise if seen; don't originate.

## A12-OBJ-03 — Object caching / Object Manager

Improves channel efficiency ≥ 2× by letting a well-sited station (digi) take over transmission of
objects, removing uplink contention.

**Event Object Manager (APRSdos "NET-CONTROL"):** takes over **all** objects it hears from others;
retransmits each with the decay algorithm (immediately, +30 s, +1 m, +2 m, +4 m, +8 m … down to 10 min
(local/1-hop event) or 30 min (multi-hop)), ±10 % randomisation; re-starts decay when an object moves;
relinquishes only on a KILL. Originators automatically stop sending once they hear the takeover (A11-OWN-02).

**On-call Object Manager (cache request):**

| Item | Rule |
|---|---|
| Request | Object packet with **TOCALL `AP0Cxy`** (zero), path whose **first** digi is the manager's explicit call; manager must have heard it **direct**. |
| `x` | expiration in **hours** from now (counts down in subsequent transmissions) |
| `y` | final periodicity in **tens of minutes** |
| Response / ack | Manager transmits the object with TOCALL **`AP0Oxy`** — the first copy is the ack; originator stops sending. |
| Timing | Decay: +1, +2, +4, +8, +16 min … until reaching `y×10` min. |
| Path | **Direct only**, unless the sysop configures a fixed path for the venue. |
| Cancel | Any other identically named object from anyone cancels the cache entry (normal 1.0 behaviour). |

Parser: TOCALL regex `^AP0([CO])([0-9A-Z])([0-9A-Z])$` → cache request/response metadata.
