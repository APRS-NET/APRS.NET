---
id: aprs11.weather
spec: APRS 1.1 Addendum
status: approved (clarifications); the 2006/2011 additions are APRS 1.2 proposals
topic: Weather field clarifications
affects_aprs101_pages: [62, 63, 64, 65]
sources:
  - https://www.aprs.org/aprs11/spec-wx.txt
  - source/aprs.org/aprs11/spec-wx.txt
see_also:
  - ../aprs12/weather-sensors.md   # radiation X, water level F, volts V, device Z, symbol rules
---

# APRS 1.1 — Weather clarifications

- **A11-WX-01 `s` ambiguity.** In the recommended **complete** WX format (position + `_` symbol),
  wind speed is **positional** (`ddd/sss` after the symbol) and has no letter. Therefore an `s`
  field in a complete report is **snowfall**. Only the WinAPRS *positionless* format uses `s` for
  wind; that format can't carry snow and is not recommended.
- **A11-WX-02 Rain fields.**
  - `r` = rain in the **last hour** (1/100 in)
  - `p` = rain in the **last 24 hours, rolling** (1/100 in) — many clients wrongly reset at midnight
  - `P` = rain **since local midnight** (1/100 in)
- **A11-WX-03 Field width.** "Other parameter" fields are **3 digits** unless otherwise specified.
- **A11-WX-04 Luminosity.** `Lnnn` = 0–999 W/m²; `lnnn` = **1000 + nnn** W/m² (e.g. `l123` = 1123).
- **A11-WX-05 Snow.** `s` is 3 bytes: `s001` = 1 in, `s1.5` = 1.5 in, `s010` = 10 in
  (no fractions above 9 in).
- **A11-WX-06 Raw rain counter `#`.** The 3 least-significant digits of a raw bucket counter in
  whatever units; mainly lets viewers see change. Not recommended.
- **A11-WX-07 Symbols.** `/W` and `\W` (NWS sites) are accepted interchangeably with `/_` and `\_`
  as weather stations. They were meant to distinguish calibrated NWS reports from amateur ones.
- **A11-WX-08 Missing data.** Any field may be `...` or spaces (width-matched) to mean
  "no sensor / no data"; dots preferred for column alignment. Any non-required field may be
  omitted entirely. (Wind dir, wind speed and temperature remain required in the complete format.)
- **A11-WX-09 Variable fields.** The additional WX fields are variable/optional in order after the
  required ones.
- **A11-WX-10 Water height (listed in spec-wx.txt header).** `Fxxxx` water height in feet
  (usually tenths) and `fxxxx` in meters — superseded by the 1.2 definition of `F` in tenths of a
  foot relative to flood stage; see `../aprs12/weather-sensors.md`.
- **A11-WX-11 Raw weather formats** (`#`, `*`, `$ULTW`, etc.) not recommended on RF; convert to
  complete format in the station's micro-controller. Decoders should still accept them.
- **A11-WX-12** Weather data may appear inside **objects and items** (A11-OBJ-02).

### Software-type letters (weather-format suffix)

`spec-wx.txt` notes additional software-type identifiers assigned for the WX software/unit
type bytes: `O` = OpenTracker, `K` = Kenwood, `B` = Byonics, `Y` = Yaesu.

## Test vectors

| Fragment (after symbol `_`) | Decoded |
|---|---|
| `090/005g010t068r001p012P008` | wind 090° 5 mph gust 10, 68 °F, rain 1h 0.01", 24h 0.12", since-midnight 0.08" |
| `.../...t050` | no wind sensor, 50 °F |
| `090/005t068s010` *(complete fmt)* | `s` here = snowfall 10 in (wind speed is the positional `005`) |
| `L850` / `l123` | 850 W/m² / 1123 W/m² |
