# Test corpus

Real APRS traffic for parser development and tests. Raw captures never go in here (`captures/` is git-ignored);
only files run through the anonymizer below.

Tests built from this traffic live in `tests/AprsNet.Packets.CorpusTests`, separate from the spec-derived vectors in
`tests/AprsNet.Packets.SpecTests`. `CorpusFileTests` already checks every file here: the line format, timestamps in
order, the file listed in the table below, and that callsigns and e-mail domains are anonymized (it fails on a raw
capture).

## Files

| File | Source | Lines |
| --- | --- | --- |
| `aprs-is/2026-10-12-r20km.log` | APRS-IS, read-only login, filter `r/38.98/-94.67/20` (20 km radius), three captures on 2026-10-12 UTC totalling about 2 h 10 min | 1,113 |
| `aprs-is/2026-10-12-sf-r30km.log` | APRS-IS, read-only login, filter `r/37.77/-122.42/30` (30 km around San Francisco), 1 h on 2026-10-12 UTC. Simulated stations (`DEMO-*`, `MARSQ-*`) are sampled to their first 10 packets each (1,040 dropped) | 392 |
| `aprs-is/2026-10-12-ruhr-r40km.log` | APRS-IS, read-only login, filter `r/51.46/7.01/40` (40 km around Essen, Germany), 1 h on 2026-10-12 UTC (evening local time). Four reconnects lose about 95 s each | 1,656 |

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

Deliberately kept negative cases: one IGate inserts `" <UI>:"` or `" <<UI>>:"` before the data type, so those
packets start with a space; a lower-case source callsign; Mic-E with control bytes; and weather with a trailing
software tag. Beacons repeat, so there are far fewer distinct packets than lines; dedupe when building test cases.

## Anonymization

`tools/corpus/Anonymize.cs` rewrote the raw captures:

- **Callsigns** (source, path, IGate, object names, message addressees and mentions in text) became same-length
  pseudonyms starting with `Q`, keeping the call-area digit and SSID. Q is not an ITU national prefix, so no
  pseudonym can be a real station. The mapping is consistent across the file.
- **Named stations** (digis, hospitals and other tactical names) became same-length `X…` names, keeping any SSID.
  SvxLink's `EL-`/`ER-` tags are kept and the callsign after them is mapped.
  APRS-IS servers (`T2…`, `FIRST`…`TENTH`, `APRSFI`) and simulators (`DEMO`, `MARSQ`) are kept.
- **E-mail addresses** are masked (`x` for letters, `0` for digits), and personal names in comments (one in San Francisco,
  13 first names in Rhine-Ruhr) were replaced with same-length `Xxxx` words.
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
names spotted in comments); review the free text of every new capture for those before committing.
