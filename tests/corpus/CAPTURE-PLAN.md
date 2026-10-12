# Capture plan

Areas to capture for the corpus, chosen for variety rather than volume: different callsign formats, coordinate
quadrants, languages and text encodings, device mixes and network habits. Traffic levels are estimates; do a 2-minute
probe before committing to an hour.

All captures use a read-only login and a radius filter (`r/<lat>/<lon>/<km>`):

```sh
aprscli listen --callsign <CALL> --filter r/<lat>/<lon>/<km> --timestamps --duration 1h --reconnect --quiet --capture captures/<name>.log
```

Then anonymize the capture into `aprs-is/` (see [README.md](README.md)) and list it there.

## Areas

| # | Area | Filter | What it adds | Status |
| --- | --- | --- | --- | --- |
| | **Germany and Central Europe** | | | |
| 1 | Rhine-Ruhr (Essen/Dortmund) | `r/51.46/7.01/40` | Densest German area: `DLx`/`DOx`/`DBx` calls, fixed digis and IGates, LoRa APRS IGates, German comments | Captured 2026-10-12 (`ruhr-r40km`) |
| 2 | Munich | `r/48.14/11.58/40` | Alpine digis, LoRa trackers, `DL` and `OE` calls near the Austrian border | |
| 3 | Berlin | `r/52.52/13.40/30` | Big-city German mix; a cross-check against Rhine-Ruhr | |
| 4 | Prague | `r/50.08/14.44/40` | `OK` calls, an active LoRa APRS community, diacritics in comments | |
| 5 | Netherlands (Randstad) | `r/52.20/4.60/50` | `PA`/`PD` calls, dense IGates, many weather stations | |
| 6 | London | `r/51.51/-0.13/40` | `G`/`M`/`2E0` calls (digit-first prefixes), longitudes either side of Greenwich | |
| | **Japan** | | | |
| 7 | Tokyo / Kanto | `r/35.68/139.77/40` | `JA`/`JH`/`JR`/`7Kx` calls, Kenwood and Yaesu Mic-E, D-STAR DPRS gateways, likely Shift-JIS or UTF-8 text | |
| 8 | Osaka / Kansai | `r/34.69/135.50/40` | A second Japanese region, so findings are not Tokyo-specific | |
| | **Southern hemisphere and elsewhere** | | | |
| 9 | Sydney | `r/-33.87/151.21/50` | South and east quadrant, `VK` calls | |
| 10 | Melbourne | `r/-37.81/144.96/50` | Active VK3 WIDE network | |
| 11 | Auckland | `r/-36.85/174.76/50` | Longitudes near +180, `ZL` calls | |
| 12 | Johannesburg / Gauteng | `r/-26.20/28.05/60` | `ZS` calls, an active regional network, a different device mix | |
| 13 | São Paulo | `r/-23.55/-46.63/60` | South and west quadrant, `PY` calls, Portuguese text | |
| | **North America** | | | |
| 14 | Denver / Front Range | `r/39.74/-104.99/50` | Mountaintop digis with long paths, high `/A=` altitudes, balloon launches | |
| 15 | Los Angeles | `r/34.05/-118.24/50` | Very high volume, many objects and items | |
| 16 | Seattle / Puget Sound | `r/47.61/-122.33/50` | Dense digis, maritime stations, `VE7` calls across the border | |
| 17 | Dallas–Fort Worth | `r/32.78/-96.80/50` | Weather-heavy (CWOP), Skywarn objects and bulletins | |
| 18 | Orlando / Central Florida | `r/28.54/-81.38/50` | Weather stations, hurricane-season objects, mobiles | |

Already captured: Overland Park, KS (`r/38.98/-94.67/20`) and San Francisco (`r/37.77/-122.42/30`).

If only a few: Rhine-Ruhr, Tokyo, London, Sydney and Denver cover the most callsign formats, quadrants and encodings.

## Notes

- **Text encodings.** The client, `--capture` and the anonymizer all use Latin-1, so Shift-JIS or UTF-8 comments
  keep their raw bytes. Decoding them is the parser's job; the corpus never re-encodes text.
- **Anonymizer.** After each capture, run the leak check and review the new `X…` names; object and tactical naming
  differs between countries, and that is where earlier captures found gaps.
- **Timing.** Capture in local daytime, ideally at a weekend: Tokyo (UTC+9) about 00:00–09:00 UTC, Germany (UTC+1/+2)
  about 07:00–17:00 UTC, Sydney (UTC+10/+11) about 21:00–07:00 UTC.
- **Other filters.** `p/JA` or `p/DL` (callsign prefix) samples a whole country thinly; `t/w` (weather only) or
  `b/RS0ISS` (the ISS digipeater) targets rarer packet types.
