# Spec compatibility

What APRS.NET follows, where it accepts traffic that breaks the spec, and what it deliberately leaves undecoded. The
spec documents are summarised in [README.md](README.md); the evidence for each deviation is the real traffic in
[`tests/corpus`](../../tests/corpus/README.md).

## Which spec

- **APRS101** (Protocol Reference 1.0.1) is the base.
- **APRS 1.1 errata** override it where they disagree ([`aprs11/errata.md`](aprs11/errata.md)).
- **APRS 1.2** items that are de facto standard are decoded: `!DAO!` precision, the Mic-E type and device codes, and
  the New-N paradigm aliases (`WIDEn-N`, `TRACEn-N`, regional `XXn-N`).

## Reading: one lenient parser

There is no strict mode. The parser decodes what it can and records a `ParseIssue` for anything off-spec. A
**warning** keeps the packet's `Outcome` as `Parsed`; an **error** makes it `Malformed`. Callers who want strict
behaviour reject packets with any issue, or with the codes they care about. See
[`docs/design/packet-api.md`](../design/packet-api.md#outcome-type-and-issues).

### Accepted with a warning

Each of these breaks APRS101 but is common enough in real traffic that dropping the packet would lose real data.

| Deviation | What the parser does | Issue |
| --- | --- | --- |
| A message addressee not padded to 9 characters, or padded past 9 | Finds the colon and reads the message | `UnpaddedAddressee` |
| A lower-case hemisphere letter (`n`, `s`, `e`, `w`) | Decodes the position | `LowercaseHemisphere` |
| A broken timestamp in front of a good position or object (seen from some firmware) | Keeps the position; only the time is lost | `InvalidTimestamp` |
| A status that starts with something shaped like a timestamp but impossible (day 32, hour 25) | Keeps it as status text | `InvalidTimestamp` |
| `EQNS.` or `BITS.` values that cannot be read | Keeps the definition without them | `InvalidTelemetryDefinition` |
| A space, control byte or byte above 0x7E in a header callsign | Keeps the callsign as received | `InvalidCallsign` |
| An information field over 512 bytes | Decodes the first 512; `Raw` keeps the whole line | `InformationTooLong` |

### Accepted silently

These are spec-compatible readings of ambiguous text, or extensions the spec never forbade:

- **Callsigns that are not AX.25.** APRS-IS carries server names (`T2PERTH`), simulators (`DEMO-002`), lower-case
  calls and SSIDs such as `-DS`. `Callsign.IsAx25` reports which are valid on air; none get an issue.
- **Bulletin groups longer than one letter.** APRS101 defines `BLNA`–`BLNZ`; senders also use `BLNALUX` and
  `BLNA-WEB`, read as groups.
- **`T#MIC` with a comma** before the first value, as well as APRS101's form without one.
- **Weather:** three-digit humidity (`h100`, `h092`); "no data" written as dots of any count (`b...` for `b.....`) or
  as spaces of the field's width; a field that repeats is read as the start of the comment, not a second reading;
  and `DIR/SPD` wind in the comment after a compressed position with no course and speed (some LoRa IGates).
- **Path aliases are matched case-insensitively** (`wide2-1`).

### Rejected

These make a packet `Malformed`, or leave its type `Unknown`, because there is no safe reading:

- **A data type identifier that is not at byte 0.** APRS101's X1J exception (scan up to 40 characters for `!`) was
  withdrawn in 2012 (`A11-ERR-01`). One IGate inserts `" <UI>:"` before the data type, so its packets start with a
  space and their type stays `Unknown`; the corpus keeps them as a negative case.
- **A Mic-E destination that is not six latitude characters**, such as the 7-character destination one bad
  digipeater produces (`InvalidMicEDestination`).
- **More than 10 path entries** (`TooManyPathEntries`): 8 digipeaters plus a q-construct and its station.
- **Positions, objects, items, messages and telemetry** that do not have their required fields; each has its own
  error code in `ParseIssueCode`.

### Not decoded

- **Raw weather station data** (`!!` Ultimeter, `$ULTW`, `#`, `*`) is recognised as `RawWeatherReport` but its
  readings are not decoded. APRS 1.1 discourages these formats, and they are rare.
- **Raw NMEA** sentences (`$GPRMC`, `$GPGGA`) are decoded as `NmeaReport`, but the symbol APRS101 p. 92 gives a
  tracker that sends them (from a `GPSxyz` or `GPSCnn` destination, or its SSID) is not.
- **Text is not re-encoded.** Comments keep their bytes; UTF-8 and Shift-JIS text reaches the caller as received.

## Writing: strict

`AprsPacket.Create` follows the spec even where the parser is lenient, then parses what it built and refuses the
packet if the result differs from what the caller asked for (`AprsEncoder.Mismatch`). So it never sends:

- a message addressee over 9 characters, ending in a space, or holding a `:` or a byte outside printable ASCII
  (it pads the addressee to 9 itself; service names such as `NWS_WARN` and `BLN3 SKYR` are fine);
- caller text with `|`, `~` or `{`, or over 67 characters;
- a humidity of 0% (`h00` means 100%);
- a value that cannot be written at the precision chosen: an uncompressed position is sent in hundredths of a minute
  (coarser with ambiguity), a compressed one is finer, and a `!DAO!` token adds precision below a hundredth.

A parsed packet is the exception: it rebuilds from its own bytes, byte for byte, including the deviations it was
received with.
