---
id: aprs12.symbol-overlays
spec: APRS 1.2 Addendum (draft/proposals) — symbol overlay & extension tables
status: de-facto standard (living list; snapshot of symbols-new.txt dated 17 Mar 2021)
topic: Overlay-character meanings on alternate symbols; 2007 weather-symbol consolidation
affects_aprs101_pages: ["Appendix 2 symbol tables"]
sources:
  - https://www.aprs.org/symbols/symbols-new.txt
  - https://www.aprs.org/symbols/symbols-overlays.txt
  - source/aprs.org/symbols/symbols-new.txt
  - source/aprs.org/symbols/symbols-overlays.txt
see_also:
  - ../aprs11/symbols.md
---

# APRS 1.2 — Symbol overlays and extensions

## Rules

- **A12-SYM-01** Since **1 Oct 2007**, overlays `0`–`9`, `A`–`Z` are allowed on **all** alternate
  symbols (up to 36 × 94 combinations). A decoder must represent a symbol as
  `(table, code, overlay?)` — never reject an unknown overlay.
- **A12-SYM-02** An overlay may simply be drawn as a character on the base symbol, **or** a
  specific `overlay+symbol` combination may get its own dedicated graphic (e.g. `S[` skier,
  `KY` Kenwood, `A\` AvMap). Fallback for unknown combos: base alternate symbol + overlay character.
- **A12-SYM-03** Notation: `#X` = the overlayable family of alternate symbol `X`;
  `OX` (e.g. `Ra`) = overlay `O` on alternate symbol `X`, i.e. on-air table byte `O`, code `X`.
  `/X` and `\X` rows below are the primary / un-overlaid alternate meanings for reference.
- **A12-SYM-04** On-air: overlay goes in the **table byte** position (uncompressed: after latitude;
  compressed: `a`–`j` map to `0`–`9`, `A`–`Z` used directly; Mic-E: the table byte after the symbol).
- **A12-SYM-05** Lower-case overlay ⇒ precedence; `O` ⇒ operator present
  (`operator-precedence-rr-bits.md`).
- **A12-SYM-06** Unused overlay definitions have undefined results on older software; use with care.

## 2007/2008 base-symbol redefinitions

These alternate symbols were (re)defined as overlay families (25 Mar 2008):
`\=` (was undefined), `\0` numbered circle, `\A` box (APRStt/DTMF, RFID, XO), `\'` crash →
incident sites, `\%` power plant, `\H` haze → hazards, `\Y` radios/devices, `\k` special vehicles,
`\u` trucks, `\<` advisories, `\8` network nodes, `\[` wall cloud → humans, `\h` ham store → buildings.

### Weather consolidation (April 2007 proposal; reflected in symbolsX "AVAIL" notes)

| Old symbol | New code | Meaning |
|---|---|---|
| `\E` | `\E` / `HE` / `SE` | Smoke family → *Visibility*: haze, smoke |
| `\B` | `BE` | Blowing snow |
| `\b` | `DE` | Blowing dust/sand |
| `\{` | `FE` | Fog |
| `` \` `` | `` R` `` | Rain (`` \` `` with no overlay = generic precipitation) |
| `\F` | `` F` `` | Freezing rain |
| `\:` | `` H` `` | Hail |
| `\D` | `` D` `` | Drizzle |
| `\e` | `` E` `` | Sleet |
| `\*` | `` S` `` | Snow |
| `\I` | `RI` | Rain shower |
| `\G` | `SI` | Snow shower |
| `\J` | `LI` | Lightning |
| `\(` | `\(` / `P(` / `W(` / `F(` | Cloud family: partly cloudy (was `\p`), wall cloud (was `\[`), funnel (`\f` retained) |

Freed families for future use: `#H` hazards, `#[` humans, `#\` GPS/nav devices, and
`#B #b #{ #* #: #D #F #e #G #J #p` (TBD). **Decoders should still recognise the old single
symbols** (e.g. `\*` as snow) because legacy stations transmit them.

## Overlay definitions (generated from `symbols-new.txt`)

### ADVISORIES: #<  (new expansion possibilities)

| Code | Meaning |
|---|---|
| `/<` | motorcycle |
| `\<` | Advisory (single gale flag) |

### AIRCRAFT

| Code | Meaning |
|---|---|
| `/^` | LARGE Aircraft |
| `\^` | top-view originally intended to point in direction of flight |
| `A^` | Autonomous (2015) |
| `D^` | Drone   (new may 2014) |
| `E^` | Electric aircraft (2015) |
| `H^` | Hovercraft    (new may 2014) |
| `J^` | JET     (new may 2014) |
| `M^` | Missle   (new may 2014) |
| `P^` | Prop (new Aug 2014) |
| `R^` | Remotely Piloted (new 2015) |
| `S^` | Solar Powered  (new 2015) |
| `V^` | Vertical takeoff   (new may 2014) |
| `X^` | Experimental (new Aug 2014) |

### ATM Machine or CURRENCY:  #$

| Code | Meaning |
|---|---|
| `/$` | original primary Phone |
| `\$` | Bank or ATM (generic) |
| `U$` | US dollars |
| `L$` | Brittish Pound |
| `Y$` | Japanese Yen |

### ARRL or DIAMOND: #a

| Code | Meaning |
|---|---|
| `/a` | Ambulance |
| `Aa` | ARES |
| `Da` | DSTAR (had been ARES Dutch) |
| `Ga` | RSGB Radio Society of Great Brittan |
| `Ra` | RACES |
| `Sa` | SATERN Salvation Army |
| `Wa` | WinLink |
| `Ya` | C4FM Yaesu repeaters |

### BALLOONS and lighter than air #O (All new Oct 2015)

| Code | Meaning |
|---|---|
| `/O` | Original Balloon (think Ham balloon) |
| `\O` | ROCKET (amateur)(2007) |
| `BO` | Blimp           (2015) |
| `MO` | Manned Balloon  (2015) |
| `TO` | Teathered       (2015) |
| `CO` | Constant Pressure - Long duration (2015) |
| `RO` | Rocket bearing Balloon (Rockoon)  (2015) |
| `WO` | World-round balloon (2018) |

### BOX SYMBOL: #A (and other system inputted symbols)

| Code | Meaning |
|---|---|
| `/A` | Aid station |
| `\A` | numbered box |
| `9A` | Mobile DTMF user |
| `7A` | HT DTMF user |
| `HA` | House DTMF user |
| `EA` | Echolink DTMF report |
| `IA` | IRLP DTMF report |
| `RA` | RFID report |
| `AA` | AllStar DTMF report |
| `DA` | D-Star report |
| `XA` | OLPC Laptop XO |
| … | etc. |

### BUILDINGS: #h

| Code | Meaning |
|---|---|
| `/h` | Hospital |
| `\h` | Ham Store       ** <= now used for HAMFESTS |
| `Ch` | Club (ham radio) |
| `Eh` | Electronics Store |
| `Fh` | HamFest (new Aug 2014) |
| `Hh` | Hardware Store etc.. |

### CARS: #> (Vehicles)

| Code | Meaning |
|---|---|
| `/>` | normal car (side view) |
| `\>` | Top view and symbol POINTS in direction of travel |
| `#>` | Reserve overlays 1-9 for numbered cars (new Aug 2014) |
| `3>` | Model 3 (Tesla) |
| `B>` | BEV - Battery EV(was E for electric) |
| `D>` | DIY - Do it yourself |
| `E>` | Ethanol (was electric) |
| `F>` | Fuelcell or hydrogen |
| `H>` | Hybrid |
| `L>` | Leaf |
| `P>` | PHEV - Plugin-hybrid |
| `S>` | Solar powered |
| `T>` | Tesla  (temporary) |
| `V>` | Volt (temporary) |
| `X>` | Model X |

### CIVIL DEFENSE or TRIANGLE: #c

| Code | Meaning |
|---|---|
| `/c` | Incident Command Post |
| `\c` | Civil Defense |
| `Dc` | Decontamination (new Aug 2014) |
| `Rc` | RACES |
| `Sc` | SATERN mobile canteen |

### DEPOT

| Code | Meaning |
|---|---|
| `/D` | was originally undefined |
| `\D` | was drizzle (moved to ' ovlyD) |
| `AD` | Airport  (new Aug 2014) |
| `FD` | Ferry Landing (new Aug 2014) |
| `HD` | Heloport (new Aug 2014) |
| `RD` | Rail Depot  (new Aug 2014) |
| `BD` | Bus Depot (new Aug 2014) |
| `LD` | LIght Rail or Subway (new Aug 2014) |
| `SD` | Seaport Depot (new Aug 2014) |

### DIGIPEATERS

| Code | Meaning |
|---|---|
| `/#` | Generic digipeater |
| `1#` | WIDE1-1 digipeater |
| `A#` | Alternate input (i.e. 144.990MHz) digipeater |
| `E#` | Emergency powered (assumed full normal digi) |
| `I#` | I-gate equipped digipeater |
| `L#` | WIDEn-N with path length trapping |
| `P#` | PacComm |
| `S#` | SSn-N digipeater (includes WIDEn-N) |
| `X#` | eXperimental digipeater |
| `V#` | Viscous https://github.com/PhirePhly/aprx/blob/master/ViscousDigipeater.README |
| `W#` | WIDEn-N, SSn-N and Trapping |

### EMERGENCY: #!

| Code | Meaning |
|---|---|
| `/!` | Police/Sheriff, etc |
| `\!` | Emergency! |
| `E!` | ELT or EPIRB  (new Aug 2014) |
| `V!` | Volcanic Eruption or Lava  (new Aug 2014) |

### EYEBALL (EVENT) and VISIBILITY  #E

| Code | Meaning |
|---|---|
| `/E` | Eyeball for special live events |
| `\E` | (existing smoke) the symbol with no overlay |
| `HE` | (H overlay) Haze |
| `SE` | (S overlay) Smoke |
| `BE` | (B overlay) Blowing Snow         was \B |
| `DE` | (D overlay) blowing Dust or sand was \b |
| `FE` | (F overlay) Fog                  was \{ |

### GATEWAYS: #&

| Code | Meaning |
|---|---|
| `/&` | HF Gateway  <= the original primary table definition |
| `I&` | Igate Generic (please use more specific overlay) |
| `L&` | Lora Igate |
| `R&` | Receive only IGate (do not send msgs back to RF) |
| `P&` | PSKmail node |
| `T&` | TX igate with path set to 1 hop only) |
| `W&` | WIRES-X as opposed to W0 for WiresII |
| `2&` | TX igate with path set to 2 hops (not generally good idea) |

### GPS devices: #\

| Code | Meaning |
|---|---|
| `/\` | Triangle DF primary symbol |
| `\\` | was undefined alternate symbol |
| `A\` | Avmap G5      * <= Recommend special symbol |

### HAZARDS: #H

| Code | Meaning |
|---|---|
| `/H` | hotel |
| `\H` | Haze |
| `MH` | Methane Hazard (new Apr 2017) |
| `RH` | Radiation detector (new mar 2011) |
| `WH` | Hazardous Waste |
| `XH` | Skull&Crossbones |

### HUMAN SYMBOL: #[

| Code | Meaning |
|---|---|
| `/[` | Human |
| `\[` | Wall Cloud (the original definition) |
| `B[` | Baby on board (stroller, pram etc) |
| `S[` | Skier      * <= Recommend Special Symbol |
| `R[` | Runner |
| `H[` | Hiker |

### HOUSE: #-

| Code | Meaning |
|---|---|
| `/-` | House |
| `\-` | (was HF) |
| `5-` | 50 Hz if non standard |
| `6-` | 60 Hz if non standard |
| `B-` | Battery or off grid |
| `C-` | Combined alternatives |
| `E-` | Emergency power (grid down) |
| `G-` | Geothermal |
| `H-` | Hydro powered |
| `O-` | Operator Present |
| `S-` | Solar Power |
| `W-` | Wind power |

### INCIDENT SITES: #'

| Code | Meaning |
|---|---|
| `/'` | Small Aircraft (original primary symbol) |
| `\'` | Airplane Crash Site  <= the original alternate deifinition |
| `A'` | Automobile crash site |
| `H'` | Hazardous incident |
| `M'` | Multi-Vehicle crash site |
| `P'` | Pileup |
| `T'` | Truck wreck |

### NUMBERED CIRCLES: #0

| Code | Meaning |
|---|---|
| `A0` | Allstar Node (A0) |
| `E0` | Echolink Node (E0) |
| `I0` | IRLP repeater (I0) |
| `S0` | Staging Area  (S0) |
| `V0` | Echolink and IRLP (VOIP) |
| `W0` | WIRES (Yaesu VOIP) |

### NETWORK NODES: #8

| Code | Meaning |
|---|---|
| `88` | 802.11 network node (88) |
| `G8` | 802.11G  (G8) |

### PORTABLE SYMBOL: #;

| Code | Meaning |
|---|---|
| `/;` | Portable operation (tent) |
| `\;` | Park or Picnic |
| `F;` | Field Day |
| `I;` | Islands on the air |
| `S;` | Summits on the air |
| `W;` | WOTA |

### POWER and ENERGY: #%

| Code | Meaning |
|---|---|
| `/%` | DX cluster  <= the original primary table definition |
| `C%` | Coal |
| `E%` | Emergency  (new Aug 2014) |
| `G%` | Gas Turbine |
| `H%` | Hydroelectric |
| `N%` | Nuclear |
| `P%` | Portable (new Aug 2014) |
| `R%` | Renewable (hydrogen etc fuels) |
| `S%` | Solar |
| `T%` | Thermal (geo) |
| `W%` | Wind |

### RAIL Symbols: #=

| Code | Meaning |
|---|---|
| `/=` | generic train (use steam engine shape for quick recognition) |
| `\=` | tbd    (use same symbol for now) |
| `B=` | Bus-rail/trolley/streetcar/guiderail |
| `C=` | Commuter |
| `D=` | Diesel |
| `E=` | Electric |
| `F=` | Freight |
| `G=` | Gondola |
| `H=` | High Speed Rail (& Hyperloop?) |
| `I=` | Inclined Rail |
| `L=` | eLevated |
| `M=` | Monorail |
| `P=` | Passenger |
| `S=` | Steam |
| `T=` | Terminal (station) |
| `U=` | sUbway (& Hyperloop?) |
| `X=` | eXcursion |

### RESTAURANTS: #R

| Code | Meaning |
|---|---|
| `\R` | Restaurant (generic) |
| `7R` | 7/11 |
| `KR` | KFC |
| `MR` | McDonalds |
| `TR` | Taco Bell |

### RADIOS and APRS DEVICES: #Y

| Code | Meaning |
|---|---|
| `/Y` | Yacht  <= the original primary symbol |
| `\Y` | <= the original alternate was undefined |
| `AY` | Alinco |
| `BY` | Byonics |
| `IY` | Icom |
| `KY` | Kenwood       * <= Recommend special symbol |
| `YY` | Yaesu/Standard* <= Recommend special symbol |

### SPECIAL VEHICLES: #k

| Code | Meaning |
|---|---|
| `/k` | truck |
| `\k` | SUV |
| `4k` | 4x4 |
| `Ak` | ATV (all terrain vehicle) |

### SHELTERS: #z

| Code | Meaning |
|---|---|
| `/z` | was available |
| `\z` | overlayed shelter |
| `Cz` | Clinic (new Aug 2014) |
| `Ez` | Emergency Power |
| `Gz` | Government building  (new Aug 2014) |
| `Mz` | Morgue (new Aug 2014) |
| `Tz` | Triage (new Aug 2014) |

### SHIPS: #s

| Code | Meaning |
|---|---|
| `/s` | Power boat (ship) side view |
| `\s` | Overlay Boat (Top view) |
| `6s` | Shipwreck ("deep6") (new Aug 2014) |
| `Bs` | Pleasure Boat |
| `Cs` | Cargo |
| `Ds` | Diving |
| `Es` | Emergency or Medical transport |
| `Fs` | Fishing |
| `Hs` | High-speed Craft |
| `Js` | Jet Ski |
| `Ls` | Law enforcement |
| `Ms` | Miltary |
| `Os` | Oil Rig |
| `Ps` | Pilot Boat (new Aug 2014) |
| `Qs` | Torpedo |
| `Ss` | Search and Rescue |
| `Ts` | Tug (new Aug 2014) |
| `Us` | Underwater ops or submarine |
| `Ws` | Wing-in-Ground effect (or Hovercraft) |
| `Xs` | Passenger (paX)(ferry) |
| `Ys` | Sailing (large ship) |

### TRUCKS: #u

| Code | Meaning |
|---|---|
| `/u` | Truck (18 wheeler) |
| `\u` | truck with overlay |
| `Bu` | Buldozer/construction/Backhoe  (new Aug 2014) |
| `Gu` | Gas |
| `Pu` | Plow or SnowPlow (new Aug 2014) |
| `Tu` | Tanker |
| `Cu` | Chlorine Tanker |
| `Hu` | Hazardous |

### WATER #w

| Code | Meaning |
|---|---|
| `/w` | Water Station or other H2O |
| `\w` | flooding (or Avalanche/slides) |
| `Aw` | Avalanche |
| `Gw` | Green Flood Gauge |
| `Mw` | Mud slide |
| `Nw` | Normal flood gauge (blue) |
| `Rw` | Red flood gauge |
| `Sw` | Snow Blockage |
| `Yw` | Yellow flood gauge |
