---
id: aprs12.dao
spec: APRS 1.2 Addendum (draft/proposals)
status: de-facto standard (aprs.fi, APRSIS32, Xastir, Dire Wolf, many trackers)
topic: "!DAO! high-precision position and datum extension"
affects_aprs101_pages: [21, 23, 24]
sources:
  - https://www.aprs.org/aprs12/datum.txt   # 18 Apr 2004, upd. 7 May 2008
  - source/aprs.org/aprs12/datum.txt
---

# APRS 1.2 — `!DAO!` precision and datum option

## Format

A fixed 5-byte token **`!DAO!`** anywhere in the position comment (recommended: **at the end**,
after other text; it may extend beyond the 43/57-byte viewable limit).

| Byte | Meaning |
|---|---|
| `!` | delimiter |
| `D` | Datum identifier |
| `A` | Extra **latitude** precision |
| `O` | Extra **longitude** precision |
| `!` | delimiter |

## Datum byte (`D`)

| `D` | Meaning |
|---|---|
| `A`–`Z` | One of 26 predefined datums; **upper case ⇒ A/O are human-readable decimal digits** |
| `a`–`z` | Same datums; **lower case ⇒ A/O are base-91** |
| `0`–`9` | Locally defined custom datums (closed events, e.g. map numbers) |
| other | 28 remaining codes unassigned |

Predefined examples: **`W` = WGS84**, `N` = NAD27, `O` = OSGB36 (full table never published).
Default datum when absent: WGS84 (UK: OSGB36 by local agreement).

## Precision modes

1. **Human-readable** (`D` upper case): `A`, `O` are ASCII digits `0`–`9` = the **3rd decimal digit
   of minutes** (≈ 6 ft). `DDMM.mm` → `DDMM.mmA`.
2. **Base-91** (`D` lower case): `A`, `O` are base-91 chars (`value = byte − 33`, range 0–90) giving
   ~2 more digits (≈ 1 ft).
3. **Null**: `A`, `O` = space ⇒ no added precision; only declares the datum.

## Decoding (A12-DAO-01)

Let `lat_min` / `lon_min` be the minutes decoded from the base position (2 decimal places).

```
upper-case D:  lat_min += (A - '0') * 0.001          lon_min += (O - '0') * 0.001
lower-case D:  lat_min += (A - 33) / 91 * 0.01       lon_min += (O - 33) / 91 * 0.01
space A/O:     no change
```

The source text describes the base-91 case as "multiply by 1.10 and append the digits" (e.g.
`A`=32 → 35.2 → `DDMM.mm352`); `/91*0.01` is the exact equivalent (32/91·0.01 = 0.003516′) and is
what aprs.fi/FAP implement. Sign: add in the same hemisphere direction (i.e. apply to the absolute
value before applying N/S/E/W sign).

- **A12-DAO-02** Applies to uncompressed positions, objects and Mic-E. For Mic-E, `!DAO!` must not
  precede the `xxx}` altitude or the frequency field; place it last before the `Mv` type code.
- **A12-DAO-03** If both base-91 comment telemetry and DAO are present, DAO comes **after** the
  telemetry (`|...|!DAO!`). Parse telemetry first to avoid false DAO matches inside it.
- **A12-DAO-04** Strip the `!DAO!` token from the displayed comment.
- **A12-DAO-05** If the base position has ambiguity (spaces), DAO precision should be ignored.
  *(Implementation choice — not stated in source.)*

## Test vectors

| Base | DAO | Result |
|---|---|---|
| `4903.50N` / `07201.75W` | `!W23!` | lat 49°03.502′ N, lon 72°01.753′ W, WGS84 |
| `4903.50N` / `07201.75W` | `!wAb!` | `A`=32 → +0.003516′; `b`=65 → +0.007143′ ⇒ 49°03.503516′, 72°01.757143′ |
| — | `!w:\!` | `:`=25 → +0.002747′; `\`=59 → +0.006484′ (source text says "27" and "59" — the 59 is unscaled; 59×1.1 = 64.9 — trust the formula) |
| — | `!W  !` | WGS84 declared, no extra precision |
