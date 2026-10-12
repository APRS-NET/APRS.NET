---
id: aprs12.weather
spec: APRS 1.2 Addendum (draft/proposals) — "APRS 1.2.1 weather updates"
status: proposal (radiation `X` is implemented by some decoders; water gauge objects seen on Firenet)
topic: Radiation field, water level/flood field, battery volts, device type, which symbols carry WX
affects_aprs101_pages: [62, 63, 64, 65]
sources:
  - https://www.aprs.org/aprs12/weather-new.txt   # 24 Mar 2011
  - https://www.aprs.org/aprs12/watergage.txt     # 29 Jun 2006, upd. 24 Mar 2011
  - source/aprs.org/aprs12/weather-new.txt
  - source/aprs.org/aprs12/watergage.txt
see_also:
  - ../aprs11/weather.md
---

# APRS 1.2 — Weather and sensor additions

## A12-WX-01 — Which symbols carry weather (1.2.1 simplification, Mar 2011)

Parse weather/sensor fields only on:

- **`*_`** — weather station with **any** table/overlay (`/_`, `\_`, `R_`, `F_`, …)
- **`*W`** — NWS-site symbols (accepted as WX per A11-WX-07)
- **`*H`** — **hazard** family (`\H`, `RH` radiation, `FH` flood, `MH` methane, …): a sensor
  switches to a hazard symbol once a threshold is exceeded.

The 2006 water/flood symbols `/w`, `\w` are **no longer parsed for weather data** (but see legacy
water-gauge objects below).

- **A12-WX-02** The **complete** format (position + WX in one packet) is strongly preferred;
  positionless WX and split position/WX packets are discouraged.

## A12-WX-03 — New fields

| Field | Width | Meaning |
|---|---|---|
| `Xnnn` | 3 | **Radiation**, "resistor code" in **nSv/h**: first two digits = mantissa, last digit = power of ten. `X123` = 12×10³ nSv/h = 12 µSv/h; `X456` = 45×10⁶ nSv/h = 45 mSv/h. |
| `Fnnnn` | 4 | **Water level** above/below flood stage or mean tide, **tenths of a foot**, signed: −999…+999 ⇒ −99.9…+99.9 ft. (Meters not used on air.) |
| `Vnnn` | 3 | **Battery volts** in tenths (`V128` = 12.8 V). |
| `Zxx` | 2 | Device type (e.g. `/ZFA` in the examples; values undefined). |

Symbol conventions: background radiation → `/_`; elevated → `R_` (WX station with `R` overlay);
hazard threshold → `RH`. Benign water reading in WX → `/_`; important flood reading → `F_`;
flood condition → `FH`.

Minimum reports:
```
WX + flood:     <WX symbol>.../...t...VxxxFxxxx
water gauge:    <water symbol>VxxxFxxxx/ZFA
```

Examples:
```
DDMM.hhN/DDDMM.hhW_ddd/sss/Gggg/Tttt/Fxxxx...
DDMM.hhN/DDDMM.hhW_ddd/sss/Gggg/Tttt/Fxxxx/Vvvv...
DDMM.hhN/DDDMM.hhW_ddd/sss/Gggg/Tttt/Rrrr/PppppFxxxx...
DDMM.hhNFDDDMM.hhW_FxxxxVvvv/ZFA                  (F_ = WX station w/ flood gauge)
DDMM.hhNFDDDMM.hhWHddd/sss/Gggg/Tttt/FxxxxVvvv/ZFA (FH = flood hazard)
```

> ⚠ The examples in the source use `/Gggg/Tttt` with slashes and capital `T` — inconsistent with
> APRS101 (`gggtttt` without slashes, lower-case `t` for temperature; `T` is not a WX field). Treat the
> canonical field letters from APRS101 as authoritative and parse `F`/`V`/`X`/`Z` as **additional**
> fields. Note `F` vs the 1.1 note "`Fxxxx` water height in feet / `fxxxx` in meters".

## A12-WX-04 — Legacy water-gauge objects (Firenet, observed 2011)

```
;09428508 *061713z3401.40N/11424.75Ww3.57gh/82cfs
```
Standard object, symbol `/w`, comment `3.57gh/82cfs` = gauge height 3.57 (ft) and 82 cubic ft/s.
Parse opportunistically (`(?<gh>[\d.]+)gh/(?<cfs>\d+)cfs`); not a WX format.

## Overlay codes (from `symbol-overlays.md`)

Water `#w`: `Nw` normal (blue), `Gw` green, `Yw` yellow, `Rw` red flood gauge; `Aw` avalanche,
`Mw` mudslide, `Sw` snow blockage. Hazards `#H`: `RH` radiation detector, `MH` methane, `WH`
hazardous waste, `XH` skull & crossbones.
