---
id: aprs12.comment
spec: APRS 1.2 Addendum (draft/proposals) — consolidated
status: mixed (see per-item status)
topic: Ordering and parsing of everything that can live in a position/object comment; PHGR probes; delimiters
affects_aprs101_pages: [27, 28, 29, 30, 31, 32, 33, 34, 35]
sources:
  - https://www.aprs.org/aprs12/utf-8.txt
  - https://www.aprs.org/aprs12/probes.txt       # PHGR, 26 Feb 2003
  - https://www.aprs.org/info/freqspec.txt
  - https://www.aprs.org/aprs12/mic-e-examples.txt
  - http://he.fi/doc/aprs-base91-comment-telemetry.txt
  - source/ (same files)
---

# APRS 1.2 — Comment-field extensions (consolidated parse order)

This file pulls together the ordering rules scattered across the 1.2 notes so a single comment
parser can be built.

## A12-CMT-01 — Canonical layout

```
<symbol>
  [7-byte data extension]          CSE/SPD | PHGphgd[r/] | RNGrrrr | DFSshgd | Tyy/Cxx (area, \l) | DIR/SPD + WX fields (_)
  [optional delimiter]             single '/' or ' ' — skip it (A12-CMT-02)
  [!EMERGENCY!-style code]         emergency-codes.md
  [FFF.FFFMHz | FFF.FF MHz]        first 10 bytes of text — frequency-afrs.md
  [ Tnnn][ oXXX][ Rnnm] ...        tone/offset/range groups
  free text (UTF-8 allowed here)
  [/A=nnnnnn]                      altitude — anywhere, preferably near the end
  [{XX}]                           value signpost text (\m only)
  [|base91 telemetry|]             telemetry.md
  [!DAO!]                          dao-precision-datum.md — last (before Mic-E Mv)
  [ Mv]                            Mic-E only — mic-e-type-codes.md
```

For Mic-E the prefix is the type byte `T` and optional `aaa}` altitude instead of the 7-byte extension.

## A12-CMT-02 — Leading delimiter

After a fixed-format extension, a single `/` or space was *intended* to be an optional delimiter
that parsers skip and don't display. Not codified in APRS101, and Kenwood radios display it.
- **Decoders:** accept and skip one `/` or space.
- **Encoders:** omit it when targeting radio displays (saves a byte), or use it for readability.

## A12-CMT-03 — PHGR "probes" (network reliability measurement)

```
PHGphgd       (standard)
PHGphgdr/     (PHGR)   r = packets per hour: 1-9, A-Z (A = 10 … Z = 35); '/' separator mandatory
PHGphgd0/     unscheduled (e.g. query response) — exclude from statistics
```
- Lets receivers compute reliability (received / expected) for a fixed reference station.
- Use only on **fixed**, typical-user stations beaconing between every 6 min and every 60 min;
  not on wide-area digis, moving stations, variable-rate stations, or DF reports.
- Parser: `^PHG(?<p>\d)(?<h>.)(?<g>\d)(?<d>\d)(?:(?<r>[0-9A-Z])/)?` (APRS101 allows `h` beyond digits
  for heights ≥ 10; keep the original PHG rules).

## A12-CMT-04 — UTF-8

See `messaging-extensions.md` (A12-MSG-04): free text may be UTF-8 after the fixed fields above.

## A12-CMT-05 — Speed > 670 kt

Mic-E only — see `mic-e-type-codes.md` (A12-MICE-07).

## Parser strategy (implementation guidance, not from source)

1. Peel fixed-position prefixes left-to-right (data extension → delimiter → emergency code →
   frequency + tone/offset/range).
2. Peel suffixes right-to-left in reverse order of the canonical layout:
   Mic-E `Mv` (only if recognised) → `!DAO!` → `|telemetry|`.
3. Extract anywhere-tokens from the remainder: `/A=nnnnnn`, `{XX}` (signpost symbols only).
4. What's left is display text.
