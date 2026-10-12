---
id: aprs12.freq
spec: APRS 1.2 Addendum (draft/proposals) — AFRS (Automatic Frequency Reporting System)
status: de-facto standard (Kenwood D710/D72/D74, Yaesu FTM-350+ insert and parse it)
topic: Voice frequency, tone, offset, range in comments; frequency objects; EchoLink/IRLP/WiRES/Winlink objects; QSY messages
affects_aprs101_pages: []
sources:
  - https://www.aprs.org/info/freqspec.txt   # 09 May 2012
  - https://www.aprs.org/afrs.html
  - https://www.aprs.org/localinfo.html
  - source/aprs.org/info/freqspec.txt
---

# APRS 1.2 — Frequency in APRS (AFRS)

Four categories: **FREQ objects** (name is the frequency, sent by local digis), **fixed FREQ
comment** (in any position/object comment), **FREQ status** (`>` status packets for NMEA/Mic-E
trackers), **auto FREQ insertion** (radios insert their voice frequency).

## 1. Frequency in a position/object comment (first 10 bytes)

The 10-byte frequency field is the **first** field of the comment text (after any 7-byte data
extension such as `CSE/SPD`, `PHGphgd`, `DFSshgd`, `RNGrrrr`, with or without a `/` or space
delimiter; for Mic-E, after the optional `aaa}` altitude).

| 10-byte form | Meaning |
|---|---|
| `FFF.FFFMHz` | frequency to 1 kHz |
| `FFF.FF MHz` | frequency to 10 kHz |

- **A12-FREQ-01** Transmit `MHz` exactly (mixed case); **parse case-insensitively**.
- **A12-FREQ-02** `+`/`-` are **not** allowed inside the first 10-byte field.

### Optional second/third 10-byte groups (each begins with a SPACE)

| Group | Meaning |
|---|---|
| ` Tnnn` | PL/CTCSS tone, 3 digits, tenths dropped (`T107` = 107.2 Hz, `T079` = 79.7 Hz; receivers map to the nearest standard tone). |
| ` Cnnn` | CTCSS (synonym form) |
| ` Dnnn` | DCS code |
| ` 1750` | 1750 Hz tone burst (wide) |
| ` l750` | 1750 Hz tone burst, **narrow** (lower-case L) |
| ` Toff` | No tone/DCS |
| lower-case `t`/`c`/`d` | same as upper case but **narrow** FM |
| ` +ooo` / ` -ooo` | offset in **10 kHz** units (`+060` = +600 kHz, `-500` = −5 MHz, `+100` = +1 MHz); `-000` = forced simplex; formally `oXXX` (may always follow `Tnnn`) |
| ` Rnnm` / ` Rnnk` | nominal range in miles / km |
| ` Ennm Wnnm` | directional range (N/S/E/W) if different |
| ` FFF.FFFrx` | alternate **receive** frequency (non-standard split) |

If the offset is omitted, receivers apply the **standard offset** for the band/region.

Examples:
```
146.52 MHz Enroute Alabama
147.105MHz AARC Radio Club
146.82 MHz T107 AARC Repeater
146.835MHz C107 R25m AARC
146.805MHz D256 R25k Repeater
146.40 MHz T067 +100 Repeater
442.440MHz T107 -500 Repeater
145.50 MHz t077 Simplex
FFF.FF MHz FFF.FFFrx comment
!DDMM.mmN/DDDMM.mmW$PHGphgd/FFF.FFFMHz ...
!DDMM.mmN/DDDMM.mmW$CSE/SPDFFF.FFFMHz ...      (no delimiter)
```

- **A12-FREQ-03 Leading delimiter.** A single `/` or space directly after a 7-byte data extension
  is an optional delimiter and should be skipped by parsers. (Kenwood radios display it, so
  encoders may omit it.)

## 2. Frequency objects (name = frequency)

Sent by digis **direct only** (no digipeat), every 10 min, with permanent timestamp `111111z`.

| Object name pattern | Meaning |
|---|---|
| `FFF.FFF-z` | 5 kHz-step repeater, 1 unique ID char `z` (62 options) |
| `FFF.FFFyz` | 5 kHz-step, 2 unique chars |
| `FFF.FF-yz` | 10 kHz-step, 2 unique chars |
| `FFF.FFxyz` | 10 kHz-step, 3 unique chars |

Other separators (`_ = ! ^ @`) may replace `-` if needed (avoid `+`; findu doesn't display it).

```
;FFF.FFFxy*111111zDDMM.hhN/DDDMM.hhWrT079 R25m NETxxxxxx MTGxxxx...
;FFF.FFFxy*111111zDDMM.hhN/DDDMM.hhWrT079 oXXX NETxxxxxx MTGxxxx...
```

- **A12-FREQ-04** When the frequency is in the **object name**, the comment does **not** start with
  a 10-byte frequency; `Tnnn`/`oXXX`/`Rnnm` are **left-justified with no leading space** (08 May 2012
  clarification). If the comment *also* contains a frequency, the name = transmit freq and the
  comment freq = crossband/split receive freq.
- `NETxxxxxx` e.g. `Net Tu9PM`; `MTGxxxx` 7 bytes e.g. `Mg3rdTu`.
- Designed for 10×10×8 radio displays (D7/D700/HamHUD) and 20-char Yaesu displays.

## 3. Frequency in status packets

For trackers that can't change the position comment (NMEA, Mic-E): status beacon
`>FFF.FFFMHz Tnnn oXXX etc…`. Receivers should associate it with the station.

## 4. VoIP / gateway node objects

All use `111111z`, 1-mile ambiguity (two hundredths digits of lat/lon = spaces), max 43 chars:

```
;EL-123456*111111zDDMM.  NEDDDMM.  W0FFF.FFFMHz Tnnn oXXX %CALL..   EchoLink  (symbol E0)
;IRLP-1234*111111zDDMM.  NIDDDMM.  W0FFF.FFFMHz Tnnn oXXX STTS CALL IRLP      (symbol I0)
;WIR-1101D*111111z3348.43NW11802.32W0430.900MHz D023 oXXX STTS...     WiRES     (symbol W0)
;WL-AB9XYZ*111111zDDMM.  NWDDDMM.  WaFFF.FFFMHz 1200 oXXX comment     Winlink   (symbol Wa; baud in place of tone)
```

`STTS` = 4-byte node status (`busy`, `conf`, `off_`, `idle`); `%` = 1-byte status in the compact
EchoLink form. Mic-E forms: D710 EchoLink `MYCALL>LLLLLL,DIGI:'GGGCSD0E]FFF.FFFMHz Tnnn RXXm Ecolink node=`;
FTM-350 WiRES ``...'GGGCSD0W`FFF.FFFMHz Tnnn oXXX WiRES node _"``.

## 5. Microwave frequencies (> 999.999 MHz)

Either `X.XXXGHz` (1 MHz resolution) or a **letter prefix** replacing the hundreds digit:

| Prefix | Base | Example |
|---|---|---|
| `A` | 1200 MHz | `A96.000MHz` = 1296 |
| `B` | 2300 | `B20.000MHz` = 2320 |
| `C` | 2400 | `C01.000MHz` = 2401 |
| `D` | 3400 | `D01.000MHz` = 3401 |
| `E` | 5600 | `E51.000MHz` = 5651 |
| `F` | 5700 | `F60.000MHz` = 5760 |
| `G` | 5800 | `G30.000MHz` = 5830 |
| `H` | 10 100 | `H01.000MHz` = 10 101 |
| `I` | 10 200 | `I01.000MHz` = 10 201 |
| `J` | 10 300 | `J68.000MHz` = 10 368 |
| `K` | 10 400 | `K01.000MHz` = 10 401 |
| `L` | 10 500 | `L01.000MHz` = 10 501 |
| `M` | 24 000 | `M48.000MHz` = 24 048 |
| `N` | 24 100 | `N01.000MHz` = 24 101 |
| `O` | 24 200 | `O01.000MHz` = 24 201 |

## 6. Frequency in messages (QSY)

Two variants appear in the sources:

aprs12.html (message-proposal list):
```
:USER     :FFF.FFFMHz Tnnn QSY?...    request to QSY
:USER     :FFF.FFFMHz Tnnn QSY!...    command to QSY
```
freqspec.txt ("premature to nail down"):
```
QSY FFF.FFFMHz!   auto-QSY now
QSY FFF.FFFMHz?   auto-QSY unless user declines
QSY FFF.FFFMHz.   QSY if user presses TUNE
```
Treat as **unstable**; parse both if implementing.

## Parser regex hints

```regex
^(?<f>\d{3}\.\d{2}[\d ])MHz                       # first 10 bytes (case-insensitive "MHz")
^(?<name>\d{3}\.\d{2}[0-9A-Za-z_=!^@-]{3})$        # freq object name (9 chars)
(?:^|\s)(?<tt>[TtCcDd]\d{3}|1750|l750|Toff)\b     # tone
(?:^|\s)(?<off>[+-]\d{3})\b                         # offset, 10 kHz units
(?:^|\s)R(?<rng>\d{2})(?<u>[mk])\b                  # range
```
