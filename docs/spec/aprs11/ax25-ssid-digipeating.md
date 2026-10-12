---
id: aprs11.ax25
spec: APRS 1.1 Addendum
status: approved (operating conventions; New-N Paradigm adopted USA ~2004, Europe 20 Nov 2008)
topic: SSID conventions, AX.25 C-bits, HID, paths, New-N Paradigm, digipeater IDs and overlays, HF offsets
affects_aprs101_pages: [11, 12, 91, 95]
sources:
  - https://www.aprs.org/aprs11/SSIDs.txt
  - https://www.aprs.org/aprs11/C-bits-SSID.txt
  - https://www.aprs.org/aprs11/HID.txt
  - https://www.aprs.org/aprs11/hacks.txt
  - https://www.aprs.org/fix14439.html
  - https://www.aprs.org/digis/digi-rates.txt
  - https://www.aprs.org/D7xx/d700digi.txt
  - https://www.aprs.org/digi-overlays.html
  - https://www.aprs.org/aprs11/hf-freqs.txt
  - source/aprs.org/ (same paths)
see_also:
  - ../aprs12/digipeating-and-network.md
  - ../aprs12/operator-precedence-rr-bits.md   # RR bits in the SSID byte
---

# APRS 1.1 — AX.25, SSIDs and digipeating conventions

## SSID recommendations (6 Feb 2012 revision)

Informal conventions for *recognising station type at a glance* — **not a decoding standard**.
SSID 0 is normally not displayed.

| SSID | Typical use |
|---|---|
| -0 | Primary station, usually fixed and message capable |
| -1 … -4 | Generic additional station (digi, mobile, wx, …) |
| -5 | Other networks (D-Star, iPhone, Android, BlackBerry …) |
| -6 | Special activity, satellite ops, camping, 6 m |
| -7 | Walkie-talkies, HTs, other human-portable |
| -8 | Boats, sailboats, RVs, second main mobile |
| -9 | Primary mobile (usually message capable) |
| -10 | Internet, IGates, EchoLink, Winlink, AVRS, APRN … |
| -11 | Balloons, aircraft, spacecraft |
| -12 | APRStt, DTMF, RFID, devices, **one-way trackers** |
| -13 | Weather stations |
| -14 | Truckers / full-time drivers |
| -15 | Generic additional station |

- **A11-SSID-01** Objects and APRS-IS-originated names aren't limited by AX.25, so they may use
  non-numeric "SSIDs": `-63` (PSK63 HF), `-tt` (APRStt), `-ID` (RFID), `-A`…`-Z` (D-Star).
  A parser must not assume `-n` with 0 ≤ n ≤ 15 outside the AX.25 header.
- **A11-SSID-02** SSID → symbol mapping (1992) is obsolete; symbol comes from info field or `GPSxyz`.

## AX.25 SSID byte

```
bit:  7 6 5 4 3 2 1 0
      H R R S S S S E      H = has-been-digipeated (digi fields) / C-bit (src/dst)
                           RR = reserved (default 11)   SSID = 0-15   E = address extension (1 = last)
```

- **A11-AX25-01 C-bits.** AX.25 v2 says dest/src C-bits should be opposite (command = dst 1/src 0).
  All four combinations are seen in the wild (Kenwood/Yaesu send "response"; UI-View/APRSdroid
  "command"; KPC-3/Xastir/TinyTrak 00; OpenTracker 11). **Ignore C-bits on receive.**
  (When transmitting, "command" (dst C=1, src C=0) is the most defensible choice per WB2OSZ.)
- **A11-AX25-02 RR bits** are proposed for APRS use in 1.2 — see `../aprs12/operator-precedence-rr-bits.md`.
  Receivers must ignore/pass them.

## HID (TNC ID beacons)

- **A11-HID-01** TNC `HID` must be **OFF** on every APRS station and digi. HID packets are not APRS,
  go every 10 min along the full UNPROTO path, and can be 60–67 % of a digi's traffic. The APRS
  position beacon replaces them.

## Paths — the New-N Paradigm (fix14439)

- **A11-PATH-01** Abandon `RELAY`, `WIDE`, `TRACE`, `TRACEn-N` (they create 3–5× dupes).
  `WIDE1-1` replaces `RELAY`; `WIDEn-N` replaces `WIDE`.
- **A11-PATH-02** One universal path: **`WIDEn-N`** with N ≤ 2 in most areas (3 only in sparse areas).
  Typical: fixed/general `WIDE2-2`; mobiles `WIDE1-1,WIDE2-1`.
- **A11-PATH-03** `WIDEn-N` is now **traceable** (digis implement it via UITRACE, inserting their
  call), so the path shows every digi used.
- **A11-PATH-04** Digis **trap large N** (e.g. above 2-2 or 3-3, depending on local density).
  Each reduction in N improves channel efficiency by ~3×.
- **A11-PATH-05 `SSn-N`** state/section/region paths (e.g. `MD6-6`) — flooded via UIFLOOD with
  `ID`, so a path `WIDE1-1,SSn-N` arrives as `DIGI1,WIDE1,DIGIn,SSn*` (first and last digi visible).
  Regions in Europe use ISO 3166-1/3166-2 codes.
- **A11-PATH-06 `TEMPn-N`** — D700 mobiles leave UITRACE set to `TEMPn-N`; normally inert, usable
  as emergency/field-day digis on demand.
- **A11-PATH-07** Fill-in digis use MYALIAS `WIDE1-1`. One-hop digis in extreme-density areas force
  1 hop.
- **A11-PATH-08** Clients should *warn* (on every transmission) about inconsiderate paths rather
  than silently allow them; auto-adjust net-cycle by hop count (local/1-hop 10 min; longer 30 min);
  use the decaying retransmit algorithm. (hacks.txt; see `../aprs12/digipeating-and-network.md`.)
- **A11-PATH-09** Generic paths to recognise in a path parser:
  `WIDEn-N`, `SSn-N`, `TEMPn-N`, legacy `RELAY`, `WIDE`, `TRACE`, `TRACEn-N`, plus control words
  `NOGATE`, `RFONLY`, `TCPIP`, `TCPXX`, q-constructs (see `aprs-is.md`).

## Digipeater identification

- **A11-DIGI-01 Beacon schedule** (proportional pathing for digis):
  local direct posit every **10 min**, 1-hop every **30 min**, 2-hop every **60 min**
  (sparse-area alternative: 10/20/40/160 min for 0/1/2/3 hops). Destination `APNxxx`
  (e.g. `APN383`), not `BEACON`.
- **A11-DIGI-02** Under New-N, KPC-3+ digis should use `UIFLOOD … ID` (not `NOID`).
  D700 digi settings differ (don't use `UIFLOOD WIDE,28,NOID`); see `D7xx/d700digi.txt`.
- **A11-DIGI-03 Digi overlay characters** on the digi symbol (`\#`) announce capability
  (see also DIGIPEATERS block in `../aprs12/symbol-overlays.md`):

| Overlay | Meaning |
|---|---|
| `S` | Full New-N: `WIDEn-N` + `SSn-N` state/section routing |
| `L` | New-N but **limited** (no SSn-N) |
| `P` | Old PacComm "T" digi reconfigured for `WIDEn-N` (aliases `WIDE1-1`, `WIDE2-2`, `WIDE2-1`, `WIDE3-3`) |
| `1` | Enforced one-hop digi or `WIDE1-1`-only fill-in |
| `T` | (legacy) PacComm TRACE digi |
| `A`,`E`,`I`,`W`,`X`,`V` | see 1.2 overlay list |

- **A11-DIGI-04** Digis should state their New-N settings in the position comment (e.g. `W2`,
  `SSn-N` support) and may name themselves by location (e.g. `WASHDC`) to help visitors.
- **A11-DIGI-05** Digis should transmit up to 3 local-info objects (recommended voice repeater,
  EchoLink/IRLP nodes) **direct** — see `../aprs12/frequency-afrs.md`.

## HF (300 baud) frequencies & TNC offsets

Global APRS HF virtual carriers: **7034.30 kHz** and **10149.30 kHz** (LSB/USB dial offsets depend on
TNC tones). Full table: `source/aprs.org/aprs11/hf-freqs.txt` (Bell 103 1070/1270 → LSB 7035.47 /
10150.47, USB 7033.13 / 10148.13; MFJ-1278 2100/2300 → LSB 7036.50 / 10151.50; etc.).
