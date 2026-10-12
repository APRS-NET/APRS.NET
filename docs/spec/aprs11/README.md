---
id: aprs11.index
spec: APRS 1.1 Addendum
status: approved by APRS-WG (vote called 30 July 2004); page maintained as "always current" (last noted update 3 Nov 2011)
author: Bob Bruninga, WB4APR (Technical Chair, APRS Working Group)
canonical_url: https://www.aprs.org/aprs11.html
pdf_available: false
---

# APRS Specification Addendum 1.1 — Index

APRS 1.1 is **errata and amplification of APRS 1.0.1** (`../APRS101.PDF`, 29 Aug 2000), frozen
June/July 2004. It was never re-published as a document; it is an HTML page linking to text notes.
"This page must be considered part of the spec." Symbols and TOCALLs are living lists.

| File | Covers |
|---|---|
| [errata.md](errata.md) | Page-by-page corrections to APRS101 (X1J, Mic-E altitude/table, PHG dBi, default path, HID, datum, deprecated formats) |
| [symbols.md](symbols.md) | Symbol tables, overlay rules, mobile/WX sets, area objects, signposts, GPSxyz |
| [objects-items-names.md](objects-items-names.md) | Object = position, kill semantics, ownership, case-sensitive single namespace |
| [messaging.md](messaging.md) | Reply-ACK `{MM}AA`, auto-answer `AA:` rules, message-entry limits |
| [weather.md](weather.md) | `s` snow vs wind, `r/p/P` rain, `L/l` luminosity, missing data, `/W` |
| [tocalls-altnets-experimental.md](tocalls-altnets-experimental.md) | TOCALL device IDs, ALTNETs, `{` experimental registry |
| [ax25-ssid-digipeating.md](ax25-ssid-digipeating.md) | SSID conventions, C-bits, HID, New-N paths, digi beacons/overlays, HF offsets |
| [aprs-is.md](aprs-is.md) | NOGATE/RFONLY, `!x!` no-archive, q-construct, IGate status |
| [display-and-timing.md](display-and-timing.md) | Receive timestamps, 80-min timeout, range scale, half-size PHG, vicinity plotting |

Background-only items on the page (not summarized further): APRS statistics 2004/2009
(`source/aprs.org/aprs11/aprsstats.txt`), APRS-WG membership (`aprs11/aprswg.txt`),
"Recommended digi paths" essay (`aprs11/hacks.txt`), Voice Alert (CTCSS 100 on 144.39;
`VoiceAlert3.html.md`).

## Rule ID convention

`A11-<AREA>-nn` (e.g. `A11-RACK-03`). Use these IDs in code comments / tests for traceability.
