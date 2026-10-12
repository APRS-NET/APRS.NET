---
id: aprs11.symbols
spec: APRS 1.1 Addendum (symbol table maintained continuously; snapshot of symbolsX.txt dated 25 Nov 2015)
status: approved (living table)
topic: Symbol tables, overlay rules, mobile/weather symbol sets, GPSxyz TOCALL symbols, special-processing symbols
affects_aprs101_pages: [11, 24, 29, 92, "Appendix 2 symbol tables"]
sources:
  - https://www.aprs.org/symbols/symbolsX.txt
  - https://www.aprs.org/symbols.html
  - source/aprs.org/symbols/symbolsX.txt
  - source/aprs.org/symbols/symbols-background.txt
see_also:
  - ../aprs12/symbol-overlays.md   # overlay-letter definitions (thousands of combos)
---

# APRS 1.1 — Symbols

## Symbol table identifier (byte after latitude N/S, or the compressed table byte)

| Table char | Meaning |
|---|---|
| `/` | Primary table (mostly stations) |
| `\` | Alternate table (mostly objects) |
| `0`–`9`, `A`–`Z` | Alternate table **with overlay** character drawn on the symbol |
| `&` | **Reserved** for possible auxiliary tables (Aug 2009) |

Notation used throughout: `/$` = table `/` + symbol `$`. Overlay notation: `#X` means
"alternate symbol `X` that may carry an overlay"; `SX` means overlay `S` on alternate `X`.

## Overlay rules

- **A11-SYM-01** Originally only the "numbered" (`#`) alternate symbols accepted overlays:
  circle, square, car, truck, van, digis, gates, civil-defense, NWS sites, WX stations, triangle.
- **A11-SYM-02** Since **1 Oct 2007 (APRS 1.2)**, **any** alternate symbol may carry an overlay
  (0–9, A–Z → 36 variants × 94 symbols). Decoders must accept an overlay on any alternate symbol.
  Definitions live in `../aprs12/symbol-overlays.md`.
- **A11-SYM-03** In the table below, the *Alt flags* column marks `#` = original overlayable subset,
  `O` = significant overlay definitions exist in `symbols-new.txt`.
- **A11-SYM-04** Unassigned symbols should display the international "not" symbol
  (circle with a slash) — 04 Feb 2004.
- **A11-SYM-05** Weather: `/W` and `\W` (NWS sites) are accepted interchangeably with `/_` and `\_`
  as weather-station symbols (see `weather.md`).
- **A11-SYM-06** Lower-case overlay byte = *precedence* bit (APRS 1.2 proposal); `O` overlay =
  operator present. See `../aprs12/operator-precedence-rr-bits.md`.

## Symbol classification sets (used for filtering)

Current per `symbolsX.txt` (Nov 2008 revision). The aprs11.html page shows an older alternate
list — both are recorded; **prefer "current"**.

| Set | Table | Symbol characters |
|---|---|---|
| Just-mobile (current) | primary `/` | `` !'<=>()*0123456789CFOPRSUXY[\^abefgjkpsuv `` |
| Just-mobile (current) | alternate `\` | `>KOSY[^ksuv\` |
| Just-mobile (aprs11.html text) | primary | `` !'<=>()*0CFOPRSUXY[\^abefgjkpsuv `` |
| Just-mobile (aprs11.html text) | alternate | `>KOS^ksuv` (removed `/0An` from older list) |
| Just-mobile (pre-Nov 2008, obsolete) | primary / alternate | `'<=>()*0COPRSUXY[^abefgjkpsuv` / `/0>AKOS[^knsuv` |
| Just-weather | primary | `_` and `W` |
| Just-weather | alternate | `([*:<@BDEFGHIJTUW_efgptwy{` |

> Note: several weather alternates (`B b { * : D F e G J p`) were *moved to overlays* in 2007–2014
> (see `../aprs12/symbol-overlays.md`), so the "just-weather alternate" list is partly historical.

## Heading (top-down) symbols

All symbols should show a heading line (length ∝ log speed). These were designed as top-down
views that **rotate to point along course**: `\>` (overlay car), `\s` (overlay ship),
`\^` (overlay aircraft), `/^` (aircraft), `/g` (glider), `\n` (overlay triangle).

## Special-processing symbols

### Area objects — `\l` (lower-case L)
The 7 bytes normally used for `CSE/SPD` are replaced by `lTyy/Cxx`:

| Field | Meaning |
|---|---|
| `T` | Shape: `0`=circle, `1`=line, `2`=ellipse, `3`=triangle, `4`=box; **+5** = color-filled (`5`–`9`). `6` = line drawn down-and-**left** (lower-right quadrant special case). |
| `yy` | √(latitude offset in 1/100 deg) — see corrected formula below |
| `/` or `1` | Separator; `/` is replaced with `1` when color > 9 (so `C` is effectively 2 digits 10–15) |
| `C` / `xx` | Color 0–15; `xx` = √(longitude offset) |

Corrections (A11, from `aprs11/areaobjects.txt`):
- **A11-AREA-01** Size formula in APRS101 is wrong. Use `offset_degrees = (offset_in_packet^2) / 1500`.
- **A11-AREA-02** The **transmitted lat/long is the *lower-right* reference point** (or the center of a
  circle); offsets go back up-and-left to the upper-left "offset reference". Exception: type-6 line,
  drawn down and to the left.
- **A11-AREA-03** Triangles are isosceles, vertical; reference points are the top apex and lower-right corner.
  Circles are defined by a square box referenced by upper-left corner and center. Lines are referenced to the upper end.
  Ellipses were never implemented by any client.
- Offsets are always positive (right and down) except type 6.

### Value signpost — `\m`
Search the comment for a 1–3 character string in braces, e.g. `{55}`; display as a yellow sign
with that text. Only shown at ≤ 8-mile range scale, no callsign label.

### Waypoint — `\/`
Red dot marking a mobile's **destination**; draw a line from the mobile to its waypoint (added Jan 2004).

### Map-embedded symbols (APRSdos map labels)
Label `#` + table + symbol + hex color 1–F, then up to 8 label chars, e.g. `#\VFANC`.

### Hurricanes / tropical storms — `\@`
Red/yellow/blue for hurricane/tropical storm/depression; wind radii drawn if the WX.txt storm format is used.

## Stand-alone trackers: symbol in the TOCALL (`GPSxyz`)

Raw-NMEA trackers can't carry a symbol in the info field, so the AX.25 destination carries it:

| TOCALL | Use |
|---|---|
| `GPSxyz` | Stand-alone trackers |
| `SPCxyz` | Stand-alone trackers at special events (SPCL mode) |
| `SYMxyz` | Other TNC-only stations (e.g. WX stations) |

`xy` = the two-character code in the *GPSxyz* columns below; `z` = optional overlay character.
(SSID → symbol, the 1992 scheme, is obsolete; see `ax25-ssid-digipeating.md`.)

## Base symbol table (generated from `symbolsX.txt`, 25 Nov 2015)

Meanings are as written in the master file (abbreviations retained). `(SSID-n)` = the SSID convention
historically associated with that symbol. Entries like `AVAIL (Hail ==> \` ovly H)` mean the symbol
was freed and its old meaning moved to an overlay.

| Sym | Primary `/` meaning | Pri GPSxyz | Alternate `\` meaning | Alt GPSxyz | Alt flags |
|---|---|---|---|---|---|
| `!` | Police, Sheriff | BB | EMERGENCY (and overlays) | OB | O |
| `"` | reserved  (was rain) | BC | reserved | OC |  |
| `#` | DIGI (white center) | BD | OVERLAY DIGI (green star) | OD | # |
| `$` | PHONE | BE | Bank or ATM  (green box) | OE | O |
| `%` | DX CLUSTER | BF | Power Plant with overlay | OF | O |
| `&` | HF GATEway | BG | I=Igte R=RX T=1hopTX 2=2hopTX | OG | # |
| `'` | Small AIRCRAFT (SSID-11) | BH | Crash (& now Incident sites) | OH | O |
| `(` | Mobile Satellite Station | BI | CLOUDY (other clouds w ovrly) | OI | O |
| `)` | Wheelchair (handicapped) | BJ | Firenet MEO, MODIS Earth Obs. | OJ | O |
| `*` | SnowMobile | BK | AVAIL (SNOW moved to \` ovly S) | OK |  |
| `+` | Red Cross | BL | Church | OL |  |
| `,` | Boy Scouts | BM | Girl Scouts | OM |  |
| `-` | House QTH (VHF) | BN | House (H=HF) (O = Op Present) | ON | O |
| `.` | X | BO | Ambiguous (Big Question mark) | OO |  |
| `/` | Red Dot | BP | Waypoint Destination | OP |  |
| `$` | Z PRIMARY SYMBOL TABLE | XY | ALTERNATE SYMBOL TABLE (\) | XY | Z |
| `0` | # circle (obsolete) | P0 | CIRCLE (IRLP/Echolink/WIRES) | A0 | # |
| `1` | TBD (these were numbered) | P1 | AVAIL | A1 |  |
| `2` | TBD (circles like pool) | P2 | AVAIL | A2 |  |
| `3` | TBD (balls.  But with) | P3 | AVAIL | A3 |  |
| `4` | TBD (overlays, we can) | P4 | AVAIL | A4 |  |
| `5` | TBD (put all #'s on one) | P5 | AVAIL | A5 |  |
| `7` | TBD (for new uses?) | P7 | AVAIL | A7 |  |
| `8` | TBD (They are often used) | P8 | 802.11 or other network node | A8 | O |
| `:` | FIRE | MR | AVAIL (Hail ==> \` ovly H) | NR |  |
| `;` | Campground (Portable ops) | MS | Park/Picnic + overlay events | NS | O |
| `<` | Motorcycle     (SSID-10) | MT | ADVISORY (one WX flag) | NT | O |
| `=` | RAILROAD ENGINE | MU | avail. symbol overlay group | NU | O |
| `>` | CAR            (SSID-9) | MV | OVERLAYED CARs & Vehicles | NV | # |
| `?` | SERVER for Files | MW | INFO Kiosk  (Blue box with ?) | NW |  |
| `@` | HC FUTURE predict (dot) | MX | HURICANE/Trop-Storm | NX |  |
| `A` | Aid Station | PA | overlayBOX DTMF & RFID & XO | AA | # |
| `B` | BBS or PBBS | PB | AVAIL (BlwngSnow ==> E ovly B | AB |  |
| `C` | Canoe | PC | Coast Guard | AC |  |
| `D` |  | PD | DEPOTS (Drizzle ==> ' ovly D) | AD | O |
| `E` | EYEBALL (Events, etc!) | PE | Smoke (& other vis codes) | AE |  |
| `F` | Farm Vehicle (tractor) | PF | AVAIL (FrzngRain ==> \`F) | AF |  |
| `G` | Grid Square (6 digit) | PG | AVAIL (Snow Shwr ==> I ovly S) | AG |  |
| `H` | HOTEL (blue bed symbol) | PH | \Haze (& Overlay Hazards) | AH | O |
| `I` | TcpIp on air network stn | PI | Rain Shower | AI |  |
| `J` |  | PJ | AVAIL (Lightening ==> I ovly L) | AJ |  |
| `K` | School | PK | Kenwood HT (W) | AK |  |
| `L` | PC user (Jan 03) | PL | Lighthouse | AL |  |
| `M` | MacAPRS | PM | MARS (A=Army,N=Navy,F=AF) | AM | O |
| `N` | NTS Station | PN | Navigation Buoy | AN |  |
| `O` | BALLOON        (SSID-11) | PO | Overlay Balloon (Rocket = \O) | AO |  |
| `P` | Police | PP | Parking | AP |  |
| `Q` | TBD | PQ | QUAKE | AQ |  |
| `R` | REC. VEHICLE   (SSID-13) | PR | Restaurant | AR | O |
| `S` | SHUTTLE | PS | Satellite/Pacsat | AS |  |
| `T` | SSTV | PT | Thunderstorm | AT |  |
| `U` | BUS            (SSID-2) | PU | SUNNY | AU |  |
| `V` | ATV | PV | VORTAC Nav Aid | AV |  |
| `W` | National WX Service Site | PW | # NWS site (NWS options) | AW | # |
| `X` | HELO           (SSID-6) | PX | Pharmacy Rx (Apothicary) | AX |  |
| `Y` | YACHT (sail)   (SSID-5) | PY | Radios and devices | AY | O |
| `Z` | WinAPRS | PZ | AVAIL | AZ |  |
| `[` | Human/Person   (SSID-7) | HS | W.Cloud (& humans w Ovrly) | DS | O |
| `\` | TRIANGLE(DF station) | HT | New overlayable GPS symbol | DT | O |
| `]` | MAIL/PostOffice(was PBBS) | HU | AVAIL | DU |  |
| `^` | LARGE AIRCRAFT | HV | other Aircraft ovrlys (2014) | DV | # |
| `_` | WEATHER Station (blue) | HW | # WX site (green digi) | DW | # |
| `` ` `` | Dish Antenna | HX | Rain (all types w ovrly) | DX |  |
| `$` | Z LOWER CASE SYMBOL TABLE | XY | SECONDARY SYMBOL TABLE (\) | XY | Z |
| `a` | AMBULANCE     (SSID-1) | LA | ARRL,ARES,WinLINK,Dstar, etc | SA | #O |
| `b` | BIKE          (SSID-4) | LB | AVAIL(Blwng Dst/Snd => E ovly) | SB |  |
| `c` | Incident Command Post | LC | CD triangle RACES/SATERN/etc | SC | #O |
| `d` | Fire dept | LD | DX spot by callsign | SD |  |
| `e` | HORSE (equestrian) | LE | Sleet (& future ovrly codes) | SE |  |
| `f` | FIRE TRUCK    (SSID-3) | LF | Funnel Cloud | SF |  |
| `g` | Glider | LG | Gale Flags | SG |  |
| `h` | HOSPITAL | LH | Store. or HAMFST Hh=HAM store | SH | O |
| `i` | IOTA (islands on the air) | LI | BOX or points of Interest | SI | # |
| `j` | JEEP          (SSID-12) | LJ | WorkZone (Steam Shovel) | SJ |  |
| `k` | TRUCK         (SSID-14) | LK | Special Vehicle SUV,ATV,4x4 | SK | O |
| `l` | Laptop (Jan 03)  (Feb 07) | LL | Areas      (box,circles,etc) | SL |  |
| `m` | Mic-E Repeater | LM | Value Sign (3 digit display) | SM |  |
| `n` | Node (black bulls-eye) | LN | OVERLAY TRIANGLE | SN | # |
| `o` | EOC | LO | small circle | SO |  |
| `p` | ROVER (puppy, or dog) | LP | AVAIL (PrtlyCldy => ( ovly P | SP |  |
| `q` | GRID SQ shown above 128 m | LQ | AVAIL | SQ |  |
| `r` | Repeater         (Feb 07) | LR | Restrooms | SR |  |
| `s` | SHIP (pwr boat)  (SSID-8) | LS | OVERLAY SHIP/boats | SS | # |
| `t` | TRUCK STOP | LT | Tornado | ST |  |
| `u` | TRUCK (18 wheeler) | LU | OVERLAYED TRUCK | SU | # |
| `v` | VAN           (SSID-15) | LV | OVERLAYED Van | SV | # |
| `w` | WATER station | LW | Flooding (Avalanches/Slides) | SW | O |
| `x` | xAPRS (Unix) | LX | Wreck or Obstruction ->X<- | SX |  |
| `y` | YAGI @ QTH | LY | Skywarn | SY |  |
| `z` | TBD | LZ | OVERLAYED Shelter | SZ | # |
| `{` |  | J1 | AVAIL? (Fog ==> E ovly F) | Q1 |  |
| `\|` | TNC Stream Switch | J2 | TNC Stream Switch | Q2 |  |
| `}` |  | J3 | AVAIL? (maybe) | Q3 |  |
| `~` | TNC Stream Switch | J4 | TNC Stream Switch | Q4 |  |
