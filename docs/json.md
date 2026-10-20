# Packets as JSON

`AprsNet.Packets.Json` writes each packet as one JSON object. `aprscli listen --format json` prints the same objects,
one per line, and the gateway's WebSocket endpoint will stream them. Field names, units and values are public API:
they change only in a breaking release.

```csharp
var json = AprsPacketJson.Serialize(packet, receivedAt);      // one line of JSON
AprsPacketJson.Write(writer, packet, receivedAt);             // into a Utf8JsonWriter you own
```

The format is for writing only. To get a packet back, parse its `raw` line with `AprsPacket.Parse`. Text is written as
is (`°`, `é`), with only the characters JSON requires escaped.

## Every packet

| Field | Type | Meaning |
| --- | --- | --- |
| `received` | string | When the packet was received, UTC, to the millisecond: `2026-10-12T23:00:00.123Z` |
| `raw` | string | The packet line as received |
| `source` | string | The sender's callsign; absent when the line has no header |
| `destination` | string | The destination (TOCALL, or Mic-E data); absent without a header |
| `path` | string[] | The path entries as sent, `*` included; absent without a header |
| `type` | string | `position`, `micE`, `object`, `item`, `status`, `message`, `telemetry`, `weather`, `thirdParty`, `capabilities`, `userDefined`, `unknown`, and the other `PacketType` names in camelCase |
| `outcome` | string | `parsed`, `unsupported` or `malformed` |
| `issues` | object[] | Parse issues: `code` (camelCase `ParseIssueCode`), `severity` (`warning` or `error`), `offset` (byte index in the line) |

Then, for a decoded payload, one of the fields below. A packet with nothing decoded has none of them.

Numbers that may be missing are `null` rather than absent. Measured values are rounded to 2 decimal places, and
coordinates to 6.

## `position`, `micE`, `object`, `item`

| Field | Type | Meaning |
| --- | --- | --- |
| `name`, `live` | string, bool | Objects and items only: the name, and `false` for a killed one |
| `latitude`, `longitude` | number | Decimal degrees, north and east positive |
| `ambiguity` | number | Blanked digits, 0 to 4 |
| `unknown` | bool | The sender marked the position as unknown |
| `symbol` | object | `table`, `code`, `overlay` (when there is one) and `name` from the symbol catalogue |
| `timestamp` | object | When sent: `sent` as on the wire, `utc` when it can be resolved; absent when not sent |
| `messaging` | bool | Position and Mic-E reports: whether the station can take messages |
| `message`, `currentFix`, `device`, `locator` | | Mic-E only: the Mic-E message (`inService`, `custom0`…), whether the fix is current, the device and the Maidenhead locator from the comment |
| `compressed` | bool | Sent in the compressed format |
| `course`, `speedKnots`, `altitudeFeet`, `rangeMiles` | number? | As sent |
| `phg` | object | `powerWatts`, `heightFeet`, `gainDbi`, `directivity`, `rangeMiles` |
| `dfs` | object | `strength`, `heightFeet`, `gainDbi`, `directivity` |
| `df` | object | `bearing`, `hits`, `rangeMiles`, `quality`, `fixedStation` |
| `compressionType` | object | `gpsFix`, `nmeaSource`, `origin` |
| `area` | object | `shape`, `type`, `filled`, `color`, `latitudeOffsetDegrees`, `longitudeOffsetDegrees`, `corridorMiles` |
| `signpost`, `datum` | string | When sent |
| `weather` | object | Readings, as for `weather` below, without `timestamp` and `comment` |
| `telemetry` | object | Comment telemetry, as for `telemetry` below |
| `comment` | string | The comment, without the parts decoded above |

## `status`

`timestamp`, `locator`, `symbol` (when sent), `beamHeading`, `erpWatts`, `text`.

## `message`

`addressee`, `kind` (`message`, `acknowledgement`, `rejection`, `bulletin`, `announcement`, `nwsBulletin`, `query`), `text`, `messageId`, `replyAck`,
`ackRequired`, `bulletinId`, `bulletinGroup`, `queryType`, `queryArgument`.

## `telemetryDefinition`

A `PARM.`, `UNIT.`, `EQNS.` or `BITS.` message: `addressee`, `kind` (`parameterNames`, `units`, `equations`,
`bitSense`), `labels`, `equations` (`a`, `b`, `c` each), `bitSense`, `projectName`.

## `telemetry`

`sequence` as sent, `sequenceNumber` (null for `MIC`), `analog` (numbers or null), `digital` (eight `0`/`1`
characters, or null), `comment`.

## `weather`

`timestamp`, then `windDirection`, `windSpeedMph`, `windGustMph`, `temperatureF`, `rainLastHourInches`,
`rainLast24HoursInches`, `rainSinceMidnightInches`, `humidityPercent`, `pressureMillibars`,
`luminosityWattsPerSquareMeter`, `snowLast24HoursInches`, `rainCounter`, then `comment`.

## `thirdParty`

The carried packet, as a whole object of this format.

## `nmea`

A raw NMEA sentence (`$`): `talker` (`GP`, `GN` …), `sentence` (`RMC`, `GGA` …), `checksumValid` (true when there is
no checksum), then what it says about the fix: `mode` (`unknown`, `noFix`, `fix`, `fix2D`, `fix3D`), `latitude` and
`longitude` (null without a fix), `utcDate` (`yyyy-MM-dd`, RMC only), `utcTime` (`HH:mm:ss` with any fraction),
`speedKnots`, `course`, `altitudeMeters`, `satellites` and `hdop`. Values the sentence doesn't give are null.

## `query`

A general query (`?`): `queryType` as sent (`APRS`, `IGATE`, `WX` …), `kind` (`positionAndStatus`, `iGate`,
`weather`, `unknown` …), `footprint` (`latitude`, `longitude`, `radiusMiles`, or null for every station) and `argument`,
the text after the closing `?` as sent. Queries directed at one station are a `message` of kind `query`.

## `capabilities`, `userDefined`, `rawWeather`

- `capabilities`: an array of `token` and, when given, `value`.
- `userDefined`: `userId`, `packetType`, `experimental`, `data`.
- `rawWeather`: `format`, `station` (the weather station's name) and `data` as sent.

`tests/AprsNet.Packets.Json.Tests/golden.jsonl` has an example of each.
