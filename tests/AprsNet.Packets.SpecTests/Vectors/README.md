# APRS101 test vectors

Language-neutral test cases for the `AprsNet.Packets` decoder, taken from the APRS Protocol Reference 1.0.1
(`docs/spec/APRS101.PDF`). `VectorFileTests` checks that these files are well-formed, `VectorParsingTests` parses every
vector's header and type, and a test per packet type (`PositionVectorTests`, `MicEVectorTests` and the rest) decodes
`packet` and compares each field with `expect`.

`docs/spec/coverage/` records which vectors have also been seen in real traffic (`tests/corpus`). Regenerate it with
`dotnet run tools/corpus/Coverage.cs` after changing vectors, features or the corpus. "Seen" means one real packet
matches every feature the vector lists: the same *format* occurred on air, not the same values. Give a vector specific
features (for example `mice.lon.100-109` rather than just `mice`) so that "seen" stays meaningful.

## Files

- `aprs101-chNN-*.json`: vectors, one file per spec chapter.
- `features.json`: named packet features. Each has a .NET regular expression (`match`) and a `target`:
  `info` (text after the first `:`), `line` (the whole TNC-2 line) or `dest` (the AX.25 destination without SSID).
  Coverage reports and the self-check use these patterns.
- `vectors.schema.json`: JSON Schema for the vector files.

## A vector

```json
{
  "id": "ch08.pos-ts-csespd",
  "title": "Position with timestamp and course/speed",
  "spec": { "document": "APRS101", "page": 33, "section": "Position Reports" },
  "status": "normative",
  "features": ["dti.pos.ts.msg", "ext.csespd"],
  "packet": "N0CALL>APRS:@092345/4903.50N/07201.75W>088/036",
  "expect": { "type": "position", "messaging": true, "latitude": 49.058333, "longitude": -72.029167, "course": 88, "speed": 36 }
}
```

- `packet` is a TNC-2 monitor line. Spec examples only give the information field, so they use `N0CALL>APRS:` unless
  the destination matters (Mic-E, symbols in the destination, ALTNETs). Control bytes are JSON escapes (`\u001c`).
- `features` must all match `packet`. `VectorFileTests` enforces this, which keeps the coverage patterns honest.
- `status`:
  - `normative`: an example printed in the spec, used as is.
  - `derived`: built from the spec's tables and rules (boundary values, the other hemisphere, alternate encodings).
  - `erratum-corrected`: a spec example that is wrong as printed. `notes` says what was fixed.
  - `superseded`: APRS101 behaviour that APRS 1.1/1.2 changed (see `docs/spec/aprs11`, `docs/spec/aprs12`). Keep it
    for legacy decoding, or skip it in a strict 1.2 mode.

## Comparing `expect`

`expect` is a **partial** match. Every property it lists must match the decoded packet; anything it does not list is
unconstrained.

- `null` means "present in the format but no value" (for example `.../...` course and speed, or `b.....` pressure).
- Latitude and longitude are decimal degrees (north and east positive), compared to **±0.000001**. Other numbers are
  compared to **±0.5** unless `notes` says otherwise.
- Units are the on-air units, named in the key: `speed` in knots, `altitudeFeet`, `altitudeMeters`, `rangeMiles`,
  `temperatureF`, `pressureMb`. Weather `windSpeed` is the raw on-air value. The spec says knots on p.27 and mph on
  p.64; real stations send mph.
- `comment`, `text` and `statusText` are the **raw** remaining text. Values extracted from it (such as `/A=`, a
  locator or a `{xx}` signpost) are reported separately but **not** removed. Leading spaces are kept.
- Ambiguous positions (`4903.  N`) decode with the ambiguous digits as zero, and `ambiguity` holds the number of
  blanked digits (1–4). A decoder that reports the centre of the ambiguity box must document that and adjust.
- `{ "reject": true, "reason": "…" }` means the decoder must refuse the packet (for example Mic-E shorter than 9 bytes).
- `expect.type` values: `position`, `weather`, `mice`, `object`, `item`, `nmea`, `grid`, `rawWeather`, `telemetry`,
  `telemetryParameters`, `telemetryUnits`, `telemetryEquations`, `telemetryBits`, `message`, `ack`, `rej`,
  `bulletin`, `announcement`, `groupBulletin`, `nwsBulletin`, `capabilities`, `query`, `status`, `thirdParty`,
  `userDefined`, `invalid`, `unknown`.

These names describe *what* must be decoded, not the shape of the C# API; the parser's model can map them however it
likes.

## Spec examples that are not vectors

| Page | Example | Why |
| --- | --- | --- |
| 33 | `…p001…h00b10138dU2k`, `…p000…dUII` weather | Printed with a literal ellipsis for omitted fields |
| 66 | `;BRENDA   *4903.50N/…` (no timestamp) | Objects require a timestamp. Used with a timestamp added |
| 66 | `;BRENDA   *092345z…b0990` | Pressure has 4 digits instead of 5 |
| 78–79 | Query responses ending in `…` | Truncated in the spec |
