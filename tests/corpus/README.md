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

Format: one packet per line, `<UTC receive timestamp> <raw APRS-IS line>`, Latin-1 so every byte round-trips (Mic-E
data is not valid UTF-8). The timestamp is the moment `aprscli` read the line.

## What is in it

Positions (`=`, `!`, `@`), objects (`;`, including frequency objects, a radiosonde with `!DAO!` precision and
high-altitude data, Winlink and DAPNET objects), Mic-E from Kenwood and Yaesu radios, complete-format weather (CWOP,
Davis, UI-View), status, telemetry, an IGate capability packet, messages (telemetry `UNIT.` and a `rej`), D-Star
and MMDVM gateways.

Deliberately kept negative cases: one IGate inserts `" <UI>:"` or `" <<UI>>:"` before the data type, so those
packets start with a space; a lower-case source callsign; Mic-E with control bytes; and weather with a trailing
software tag. Beacons repeat, so there are far fewer distinct packets than lines; dedupe when building test cases.

## Anonymization

`tools/corpus/Anonymize.cs` rewrote the raw captures:

- **Callsigns** (source, path, IGate, object names, message addressees and mentions in text) became same-length
  pseudonyms starting with `Q`, keeping the call-area digit and SSID. Q is not an ITU national prefix, so no
  pseudonym can be a real station. The mapping is consistent across the file.
- **Named stations** (digis, hospitals and other tactical names) became same-length `X…` names.
- **E-mail addresses** are masked (`x` for letters, `0` for digits), and a personal name in one comment was replaced.
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
