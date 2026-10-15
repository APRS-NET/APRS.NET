# Test corpus

Real APRS traffic for parser development and tests. Raw captures never go in here (`captures/` is git-ignored);
only files run through the anonymizer below.

Tests built from this traffic live in `tests/AprsNet.Packets.CorpusTests`, separate from the spec-derived vectors in
`tests/AprsNet.Packets.SpecTests`. `CorpusFileTests` already checks every file here: the line format, timestamps in
order, the file listed in the table below, and that callsigns and e-mail domains are anonymized (it fails on a raw
capture). [CAPTURE-PLAN.md](CAPTURE-PLAN.md) lists the areas still to capture.

## Files

| File | Source | Lines |
| --- | --- | --- |
| `aprs-is/2026-10-12-r20km.log` | APRS-IS, read-only login, filter `r/38.98/-94.67/20` (20 km radius), three captures on 2026-10-12 UTC totalling about 2 h 10 min | 1,113 |
| `aprs-is/2026-10-12-sf-r30km.log` | APRS-IS, read-only login, filter `r/37.77/-122.42/30` (30 km around San Francisco), 1 h on 2026-10-12 UTC. Simulated stations (`DEMO-*`, `MARSQ-*`) are sampled to their first 10 packets each (1,040 dropped) | 392 |
| `aprs-is/2026-10-12-ruhr-r40km.log` | APRS-IS, read-only login, filter `r/51.46/7.01/40` (40 km around Essen, Germany), 1 h on 2026-10-12 UTC (evening local time). Four reconnects lose about 95 s each | 1,656 |
| `aprs-is/2026-10-12-tokyo-r40km.log` | APRS-IS, read-only login, filter `r/35.68/139.77/40` (40 km around Tokyo), 2 h from 22:56 UTC (Tuesday morning local time) | 1,533 |
| `aprs-is/2026-10-12-sydney-r50km.log` | APRS-IS, read-only login, filter `r/-33.87/151.21/50` (50 km around Sydney), 2 h from 22:56 UTC (Tuesday morning local time) | 672 |
| `aprs-is/2026-10-12-denver-r50km.log` | APRS-IS, read-only login, filter `r/39.74/-104.99/50` (50 km around Denver), 2 h from 22:56 UTC (Monday afternoon local time) | 1,521 |
| `aprs-is/2026-10-12-la-r50km.log` | APRS-IS, read-only login, filter `r/34.05/-118.24/50` (50 km around Los Angeles), 2 h from 22:56 UTC (Monday afternoon local time). 4 lines carrying leaked KISS frames were dropped (see below) | 1,502 |
| `aprs-is/2026-10-13-munich-r40km.log` | APRS-IS, read-only login, filter `r/48.14/11.58/40` (40 km around Munich), 2 h from 11:13 UTC (Tuesday afternoon local time) | 1,541 |
| `aprs-is/2026-10-13-prague-r40km.log` | APRS-IS, read-only login, filter `r/50.08/14.44/40` (40 km around Prague), 2 h from 11:13 UTC (Tuesday afternoon local time) | 982 |
| `aprs-is/2026-10-13-london-r40km.log` | APRS-IS, read-only login, filter `r/51.51/-0.13/40` (40 km around London), 2 h from 11:13 UTC (Tuesday midday local time) | 1,150 |
| `aprs-is/2026-10-13-johannesburg-r60km.log` | APRS-IS, read-only login, filter `r/-26.20/28.05/60` (60 km around Johannesburg), 2 h from 11:13 UTC (Tuesday afternoon local time) | 819 |
| `aprs-is/2026-10-13-berlin-r30km.log` | APRS-IS, read-only login, filter `r/52.52/13.40/30` (30 km around Berlin), 2 h from 13:16 UTC (Tuesday afternoon local time) | 1,783 |
| `aprs-is/2026-10-13-randstad-r50km.log` | APRS-IS, read-only login, filter `r/52.20/4.60/50` (50 km around the Randstad, Netherlands), 2 h from 13:16 UTC (Tuesday afternoon local time) | 2,522 |
| `aprs-is/2026-10-13-saopaulo-r60km.log` | APRS-IS, read-only login, filter `r/-23.55/-46.63/60` (60 km around São Paulo), 2 h from 13:16 UTC (Tuesday morning local time) | 1,299 |
| `aprs-is/2026-10-13-orlando-r50km.log` | APRS-IS, read-only login, filter `r/28.54/-81.38/50` (50 km around Orlando), 2 h from 13:16 UTC (Tuesday morning local time) | 1,347 |
| `aprs-is/2026-10-13-dfw-r50km.log` | APRS-IS, read-only login, filter `r/32.78/-96.80/50` (50 km around Dallas–Fort Worth), 2 h from 15:17 UTC (Tuesday morning local time) | 2,024 |
| `aprs-is/2026-10-13-seattle-r50km.log` | APRS-IS, read-only login, filter `r/47.61/-122.33/50` (50 km around Seattle), 2 h from 15:17 UTC (Tuesday morning local time) | 2,139 |
| `aprs-is/2026-10-13-type-items.log` | APRS-IS, read-only login, filter `t/i` (items worldwide), 1 h from 11:18 UTC | 2,136 |
| `aprs-is/2026-10-13-type-messages.log` | APRS-IS, read-only login, filter `t/m` (messages worldwide), 2 h from 11:18 UTC. Every ack, reject, bulletin, NWS, query and telemetry definition is kept; person-to-person text is sampled to the first 2 per sender and addressee, 400 in all (2,914 dropped) | 2,236 |
| `aprs-is/2026-10-13-type-user.log` | APRS-IS, read-only login, filter `t/u` (user-defined worldwide), 2 h from 11:18 UTC | 703 |
| `aprs-is/2026-10-13-type-weather-positionless.log` | APRS-IS, read-only login, filter `t/w` (weather worldwide), 1 h from 11:18 UTC, keeping only positionless (`_`) and raw station (`!!`, `$ULTW`, `#`, `*`) lines (34,260 position weather lines dropped) | 754 |
| `aprs-is/2026-10-13-type-nws.log` | APRS-IS, read-only login, filter `t/n` (NWS bulletins worldwide), 5 h 45 min from 11:21 UTC. A `t/q` (query) capture over the same hours received nothing | 500 |
| `aprs-is/2026-10-14-osaka-r40km.log` | APRS-IS, read-only login, filter `r/34.69/135.50/40` (40 km around Osaka), 2 h from 01:19 UTC (Wednesday morning local time). 8 lines carrying leaked KISS frames were dropped | 593 |
| `aprs-is/2026-10-14-melbourne-r50km.log` | APRS-IS, read-only login, filter `r/-37.81/144.96/50` (50 km around Melbourne), 2 h from 01:19 UTC (Wednesday midday local time) | 722 |
| `aprs-is/2026-10-14-auckland-r50km.log` | APRS-IS, read-only login, filter `r/-36.85/174.76/50` (50 km around Auckland), 2 h from 01:19 UTC (Wednesday afternoon local time) | 470 |
| `aprs-is/2026-10-14-kualalumpur-r50km.log` | APRS-IS, read-only login, filter `r/3.14/101.69/50` (50 km around Kuala Lumpur), 2 h from 01:19 UTC (Wednesday morning local time) | 2,200 |
| `aprs-is/2026-10-14-seoul-r50km.log` | APRS-IS, read-only login, filter `r/37.57/126.98/50` (50 km around Seoul), 2 h from 05:34 UTC (Wednesday afternoon local time) | 194 |
| `aprs-is/2026-10-14-taipei-r50km.log` | APRS-IS, read-only login, filter `r/25.03/121.56/50` (50 km around Taipei), 2 h from 05:34 UTC (Wednesday afternoon local time) | 1,542 |
| `aprs-is/2026-10-14-manila-r50km.log` | APRS-IS, read-only login, filter `r/14.60/120.98/50` (50 km around Manila), 2 h from 05:34 UTC (Wednesday afternoon local time) | 718 |
| `aprs-is/2026-10-14-perth-r50km.log` | APRS-IS, read-only login, filter `r/-31.95/115.86/50` (50 km around Perth), 2 h from 05:34 UTC (Wednesday afternoon local time) | 596 |
| `aprs-is/2026-10-14-bangalore-r60km.log` | APRS-IS, read-only login, filter `r/12.97/77.59/60` (60 km around Bengaluru), 2 h from 07:36 UTC (Wednesday afternoon local time) | 681 |
| `aprs-is/2026-10-14-bangkok-r50km.log` | APRS-IS, read-only login, filter `r/13.75/100.50/50` (50 km around Bangkok), 2 h from 07:36 UTC (Wednesday afternoon local time) | 3,370 |
| `aprs-is/2026-10-14-jakarta-r60km.log` | APRS-IS, read-only login, filter `r/-6.21/106.85/60` (60 km around Jakarta), 2 h from 07:36 UTC (Wednesday afternoon local time) | 2,196 |
| `aprs-is/2026-10-14-moscow-r50km.log` | APRS-IS, read-only login, filter `r/55.75/37.62/50` (50 km around Moscow), 2 h from 07:36 UTC (Wednesday morning local time) | 982 |
| `aprs-is/2026-10-14-warsaw-r40km.log` | APRS-IS, read-only login, filter `r/52.23/21.01/40` (40 km around Warsaw), 2 h from 09:37 UTC (Wednesday late morning local time) | 2,634 |
| `aprs-is/2026-10-14-milan-r50km.log` | APRS-IS, read-only login, filter `r/45.46/9.19/50` (50 km around Milan), 2 h from 09:37 UTC (Wednesday late morning local time) | 2,606 |
| `aprs-is/2026-10-14-madrid-r50km.log` | APRS-IS, read-only login, filter `r/40.42/-3.70/50` (50 km around Madrid), 2 h from 09:37 UTC (Wednesday late morning local time) | 2,645 |
| `aprs-is/2026-10-14-telaviv-r50km.log` | APRS-IS, read-only login, filter `r/32.08/34.78/50` (50 km around Tel Aviv), 2 h from 09:37 UTC (Wednesday midday local time) | 106 |
| `aprs-is/2026-10-14-type-status.log` | APRS-IS, read-only login, filter `t/s` (status reports worldwide), 1 h from 05:34 UTC. Sampled to one line per sender and per distinct text (digits ignored), keeping every timestamped, Maidenhead, beam-heading and non-ASCII status and every second plain one (25,510 dropped) | 2,547 |
| `aprs-is/2026-10-14-type-telemetry.log` | APRS-IS, read-only login, filter `t/t` (telemetry worldwide), 1 h from 07:36 UTC. Sampled to the first odd `T#` report of every sender and, for every fifth sender, its first `T#` report and first `PARM.`/`UNIT.`/`EQNS.`/`BITS.` (38,288 dropped) | 2,330 |

The four 2026-10-12 2-hour captures ran in parallel through a mobile router whose connection drops every 15–17
minutes, so each has six gaps of about 95 seconds where the client detected the dead connection and reconnected. The
2026-10-14 captures ran over a fixed home connection and have no reconnects. A 6-hour capture of
packets digipeated by the ISS (`d/RS0ISS*`) received nothing; its digipeater was off.

Format: one packet per line, `<UTC receive timestamp> <raw APRS-IS line>`, Latin-1 so every byte round-trips (Mic-E
data is not valid UTF-8). The timestamp is the moment `aprscli` read the line.

## What is in it

Positions (`=`, `!`, `@`), objects (`;`, including frequency objects, a radiosonde with `!DAO!` precision and
high-altitude data, Winlink and DAPNET objects), Mic-E from Kenwood and Yaesu radios, complete-format weather (CWOP,
Davis, UI-View), status, telemetry, an IGate capability packet, messages (telemetry `UNIT.` and a `rej`), D-Star
and MMDVM gateways.

The San Francisco capture adds telemetry definitions (`PARM.`, `UNIT.`, `EQNS.`, `BITS.`) and `T#` reports, IGate
capability packets with many fields, LoRa and OpenWebRX IGates, a regional `NCA2-1` path alias, a beacon with no APRS
data type (`UIDIGI 1.9`), and APRS-IS simulators whose SSIDs (`-002`, `-052`) are outside the AX.25 range of 0–15.

The Rhine-Ruhr capture adds German traffic: many LoRa APRS and MeshCom IGates (compressed positions), SvxLink
EchoLink nodes named `EL-<call>` and `ER-<call>` with their `RXTLM-1` telemetry, DAPNET pager transmitter objects,
repeater objects, MMDVM gateways for DMR, C4FM, NXDN and D-Star, UTF-8 text in comments (`Süd`, `°C`), and a
station whose path uses a lower-case `wide2-1` alias. Odd path elements kept as sent: a bare `Q` from one LoRa
firmware and `QTH*` from a DMR hotspot.

The Tokyo capture adds Japanese digit-first callsigns (`7K`, `7M`, `JA`–`JR`), heavy Mic-E use, D-STAR DPRS and gateway
traffic, Geiger-counter radiation telemetry and weather stations named after airport codes. All its text is ASCII.
Sydney adds Rural Fire Service hazard-reduction objects, Pactor and Winlink nodes, and UTF-8 status text (`°C`,
kaomoji). Denver adds Front Range digipeaters with long paths, many objects and Winlink gateways. Los Angeles adds
Winlink gateway objects with position ambiguity (`3408.  N`), a seismograph station, and heavy object and Mic-E use.

The 2026-10-13 captures add Central Europe (Munich, Prague, Berlin, the Randstad), London, Johannesburg, São Paulo,
Orlando, Dallas–Fort Worth and Seattle, and five worldwide captures by packet type for types the area captures
rarely see: items (lighthouses, DMR gateways, Spanish and Uruguayan stations, a few with broken coordinates or names
over 9 characters), messages (bulletins, announcements, bots, queries, NTS radiograms, Turkish and Malaysian nets,
8-character and over-padded addressees), user-defined packets (`{{` telemetry, and a gateway that dumps whole decoded
packets as Python dictionaries), NWS bulletins (both `NWS-` and `NWS_` addressees) and positionless weather
(`_MMDDHHMM`, an upper-case `C018S000G000T068…` variant, a `_DDHHMMz` variant, and raw Ultimeter `!!` and `$ULTW`
data). Prague has a Mic-E tracker whose destination reaches APRS-IS with 7 characters after a bad digipeater.

The 2026-10-14 captures fill in Asia and the Pacific. Osaka adds a second Japanese region with D-STAR gateways,
airport weather stations and a Dire Wolf station whose leaked KISS frames were dropped. Melbourne adds the VK3 network,
LoRa trackers and IGates, and a BBS object listing Winlink and FBB services by SSID. Auckland adds `ZL` calls at
longitudes near +175°, and repeater objects linked to IRLP and AllStarLink nodes. Kuala Lumpur is the
busiest: `9M`/`9W` digit-first calls, a dense LoRa APRS network (about a third of its lines), many telemetry
definitions, a weather service (`WXMY-n`) with `BLNnWXMY` bulletins, and message bots answering
`Unknown command`.

The later 2026-10-14 captures add East and Southeast Asia (Seoul, Taipei, Manila, Bangkok, Jakarta), India, Perth,
Moscow, Warsaw, Milan, Madrid and Tel Aviv. Bangkok has Thai UTF-8 weather summaries; Warsaw, Milan and Madrid have
dense LoRa APRS networks and many compressed positions; Madrid has a busy message network; Moscow has DX-cluster
objects. The worldwide status sample adds Chinese FMO network status in UTF-8, Turkish airport weather text and
Maidenhead-locator status; the telemetry sample adds SvxLink `RXTLM-1` reports from `EL-`, `ER-` and `RRF-` nodes,
`T#` reports with non-numeric or missing fields, and a `BITS.` line with only a title.

Deliberately kept negative cases: one IGate inserts `" <UI>:"` or `" <<UI>>:"` before the data type, so those
packets start with a space; a lower-case source callsign; Mic-E with control bytes; and weather with a trailing
software tag. Beacons repeat, so there are far fewer distinct packets than lines; dedupe when building test cases.

## Anonymization

`tools/corpus/Anonymize.cs` rewrote the raw captures:

- **Callsigns** (source, path, IGate, object names, message addressees and mentions in text) became same-length
  pseudonyms starting with `Q`, keeping the call-area digit and SSID. Q is not an ITU national prefix, so no
  pseudonym can be a real station. Digit-first calls (`7K1ABC`, `2E0ABC`) start with `0Q` instead, as no prefix starts
  with 0. The mapping is consistent across files.
- **Named stations** (digis, hospitals and other tactical names) became same-length `X…` names, keeping any SSID.
  SvxLink's `EL-`/`ER-`/`RRF-` tags are kept and the station after them is mapped; `EL-<node number>` is kept.
  Callsigns spelled out with spaces (`S P 4 X Y Z`) are mapped and keep their spacing.
  APRS-IS servers (`T2…`, `FIRST`…`TENTH`, `APRSFI`) and simulators (`DEMO`, `MARSQ`) are kept.
- **E-mail addresses** are masked (`x` for letters, `0` for digits; in the 2026-10-13 user-defined capture this also
  changed a few bytes of opaque base-91 `{{B` data that looked like an address), and personal names in comments (one in San Francisco,
  13 first names in Rhine-Ruhr, four in Sydney, Denver and Los Angeles, about 60 across the 2026-10-13 captures and about 90 in the 2026-10-14 captures,
  with a street address, a few phone numbers and a few IP addresses; names in Chinese status text became
  `某` ("so-and-so"), which keeps the byte length) were replaced with same-length `Xxxx` words.
- **Leaked KISS frames** (a gateway passing binary AX.25 frames into the information field, often re-encoded as UTF-8)
  hold callsigns as shifted bytes the text rules cannot see, so the tool refuses lines with a KISS frame delimiter;
  `--drop-binary` leaves them out.
- **Kept byte for byte:** positions (they are broadcast publicly and archived anyway), timestamps, Mic-E destination
  and data bytes, `!DAO!` and base-91 telemetry tokens, device TOCALLs, generic path aliases (`WIDEn-N`, `TCPIP*`,
  q-constructs) and APRS-IS server names.

Every substitution keeps its length, so each line is exactly as long as the original; the tool refuses to write
otherwise and also fails if a known original identifier survives.

To add a capture:

```sh
dotnet run tools/corpus/Anonymize.cs -- --map mappings/aprs-corpus-map.json --out tests/corpus/aprs-is/<name>.log <capture>...
```

The mapping file is the key to undo the anonymization, so it must **never be committed**. Keep it in `mappings/`, which is listed in `.git/info/exclude` (a local, per-clone ignore; add the same line in any new clone). Reusing it keeps
pseudonyms stable across captures. Its `Literals` section holds extra same-length replacements (such as personal
names spotted in comments); review the free text of every new capture for those before committing. This prints
each distinct stretch of text with the station that sent it:

```sh
dotnet run tools/corpus/FreeText.cs -- tests/corpus/aprs-is/<name>.log
```
