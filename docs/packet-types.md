# Supported packet types

What `AprsPacket.Parse` decodes and `AprsPacket.Create` builds, by APRS data type. Corpus counts are from the
57,456 anonymized APRS-IS packets in `tests/corpus` (15 October 2026). "Malformed" packets are broken on the air
(template text, a latitude of 116°, a missing symbol), and the parser reports why in `Issues`.

| Data type | Identifier | APRS101 | Payload | Decode | Build | Corpus (parsed of total) |
| --- | --- | --- | --- | --- | --- | ---: |
| Position, no timestamp | `!` `=` | ch. 8, 9 | `PositionReport` | ✅ | ✅ | 28,320 of 28,354 (all positions) |
| Position with timestamp | `/` `@` | ch. 8, 9 | `PositionReport` | ✅ | ✅ | (included above) |
| Compressed position | as above | ch. 9 | `PositionReport` (`IsCompressed`) | ✅ | ✅ | (included above) |
| Mic-E | `` ` `` `'` | ch. 10 | `MicEReport` (latitude in the destination) | ✅ | ✅ | 3,138 of 3,139 |
| Object | `;` | ch. 11 | `ObjectReport` | ✅ | ✅ | 4,416 of 4,424 |
| Item | `)` | ch. 11 | `ItemReport` | ✅ | ✅ | 2,137 of 2,308 |
| Message, ack, rej | `:` | ch. 14 | `MessagePayload` | ✅ | ✅ | 7,203 of 7,220 (with bulletins and queries) |
| Bulletin, announcement, NWS | `:` | ch. 14 | `MessagePayload` (`Kind`) | ✅ | ✅ | (included above) |
| Directed query | `:` `?` | ch. 15 | `MessagePayload` (`Kind = Query`) | ✅ | ✅ | (included above) |
| Telemetry definitions | `:` `PARM.` … | ch. 13 | `TelemetryDefinition` | ✅ | ✅ | (included above) |
| Status | `>` | ch. 16 | `StatusReport` | ✅ | ✅ | 5,852 of 5,852 |
| Weather, positionless | `_` | ch. 12 | `WeatherReport` | ✅ | ✅ | 772 of 894 (with raw) |
| Weather, complete | position + `_` symbol | ch. 12 | `PositionedPayload.Weather` | ✅ | ✅ | in positions |
| Raw weather station | `!!` `$ULTW` `#` `*` | ch. 12 | `RawWeatherReport` | ✅ | ❌ | in weather |
| Telemetry report | `T#` | ch. 13 | `TelemetryReport` | ✅ | ✅ | 3,633 of 3,669 |
| Comment telemetry | `\|…\|` in a comment | APRS 1.2 | `PositionedPayload.Telemetry` | ✅ | ✅ | 568 positions |
| Station capabilities | `<` | ch. 15 | `CapabilitiesReport` | ✅ | ✅ | 669 of 669 |
| User-defined | `{` | ch. 18 | `UserDefinedPayload` | ✅ | ✅ | 764 of 764 |
| Third-party | `}` | ch. 17 | `ThirdPartyPayload` (the carried packet) | ✅ | ✅ | none (IGates unwrap it) |
| Raw GPS (NMEA) | `$GPRMC` … | ch. 5 | — | ❌ | ❌ | none (about 0.01% of the full feed) |
| General query | `?` | ch. 15 | — | ❌ | ❌ | none |
| Maidenhead grid beacon (obsolete) | `[` | ch. 5 | — | ❌ | ❌ | none |
| Agrelo DFJr / MicroFinder | `%` | ch. 5 | — | ❌ | ❌ | none |
| Anything else | | | `Payload` is null, `Outcome = Unsupported` | — | — | 163 (beacon text) |

Data extensions decoded on positions, objects and items: course and speed, PHG, RNG, DFS, area objects (`Tyy/Cxx`),
DF bearing and quality, `/A=` altitude, `!DAO!` extra precision, signposts (`{55}`), complete-format weather and
comment telemetry. Mic-E adds altitude, the message code and the device from its type bytes.

## Deliberately not decoded

- **The X1J exception**, a `!` position up to 40 characters into the information field: APRS 1.1 deprecated it
  (errata A11-ERR-01), so such packets stay unsupported. 4 corpus packets carry one.
- **Raw weather station formats** decode only as far as the station and its data string; the field layouts belong
  to the manufacturers, and APRS 1.1 discourages them.
- **Mic-E telemetry**, retired by APRS 1.2 in favour of comment telemetry.

## Building

`Create` encodes a payload, parses the result back and checks it decodes to the same values, so a packet that would be
misread is refused with the reason. Parsed packets keep the bytes they arrived with and rebuild byte for byte; every
type above that builds does so over the whole corpus. See [design/packet-api.md](design/packet-api.md) for the rules
of each type.
