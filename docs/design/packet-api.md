# Packet API design

Status: **agreed; milestone 1 is implemented (the header, position reports, status reports and messages), plus objects, items and Mic-E** (`AprsNet.Packets`). This is the shape of `AprsPacket` and how text
and bytes get into it. Packet payload types (positions, Mic-E, messages…) build on it and get their own notes as they
land.

## Goals

- One packet model for every source: APRS-IS lines, a TNC in text monitor mode, captured logs, and later AX.25 frames
  from KISS.
- The exact input is kept and can be recalled, byte for byte.
- Malformed input never throws. Real traffic is messy, and the corpus already holds a leading space before the data
  type, control bytes, a bare `Q` path element and lower-case callsigns.
- A small, fixed vocabulary for what went wrong, usable as metric tags without risking unbounded cardinality.
- No I/O and no dependencies beyond the base class library.

## Input and encoding

The parser works on **TNC2-format** lines: `SOURCE>DESTINATION[,PATH...]:INFORMATION`. That is what APRS-IS sends,
what a TNC prints in text monitor mode, and what `tests/corpus` holds.

APRS has no text encoding. The information field is bytes: Mic-E packets carry binary data, and comments arrive as
ASCII, UTF-8, Latin-1 or Shift-JIS depending on the sender. So:

- **Bytes are the real input.** `Parse(ReadOnlySpan<byte>)` does the work.
- **Strings are Latin-1.** `Parse(string)` encodes with Latin-1 and calls the bytes overload. Latin-1 maps bytes
  0–255 one to one onto `U+0000`–`U+00FF`, so a line read as Latin-1 (as `AprsIsClient` does) round-trips exactly.
- A string with a character above `U+00FF` was decoded some other way (usually UTF-8) and has already lost bytes.
  `Parse(string)` throws `ArgumentException` for it instead of silently writing `?`.
- A trailing CR, LF or CRLF is not part of the packet and is dropped before the bytes are stored. Nothing else is
  trimmed: a trailing space is data (205 of 1,656 lines in the Rhine-Ruhr capture end with one).
- Decoding comment text into a .NET string is a payload concern, done lazily and only where the spec defines text.

## Entry points

```csharp
public static AprsPacket Parse(ReadOnlySpan<byte> line);
public static AprsPacket Parse(string line);
```

`Parse` always returns a packet, including for an empty line, a server `#` comment or random bytes. It throws only
when the caller misuses it:

| Exception | When |
| --- | --- |
| `ArgumentNullException` | `line` is `null` (string overload) |
| `ArgumentException` | the string holds a character above `U+00FF` |

This differs from `int.Parse` and `IPAddress.Parse`, which throw `FormatException`. The XML docs and the package
README say so in their first sentence. A throwing variant is not planned; callers who want one can check `Outcome`.

No public constructor: the packet's invariants (header fields agree with `Raw`) hold only if the parser builds it.

## `AprsPacket`

```csharp
public sealed class AprsPacket
{
    public static AprsPacket Parse(ReadOnlySpan<byte> line);
    public static AprsPacket Parse(string line);

    // The input
    public ReadOnlyMemory<byte> Raw { get; }               // exact input, line ending removed
    public byte[] GetBytes();                               // a fresh copy of Raw
    public override string ToString();                      // Raw as Latin-1, cached

    // TNC2 header
    [MemberNotNullWhen(true, nameof(Source), nameof(Destination), nameof(Path))]
    public bool HasHeader { get; }
    public Callsign? Source { get; }
    public Callsign? Destination { get; }                   // a TOCALL, an APRS-IS name, or Mic-E data
    public IReadOnlyList<PathEntry>? Path { get; }          // empty when there is no path
    public ReadOnlyMemory<byte> Information { get; }        // after the first ':'; empty without a header

    // Decoding
    public PacketType Type { get; }
    public AprsPayload? Payload { get; }                    // null unless Outcome is Parsed
    public ParseOutcome Outcome { get; }
    public IReadOnlyList<ParseIssue> Issues { get; }        // empty for a clean packet
}
```

Notes:

- **`Raw` is a private copy.** `Parse` copies the input once, so changes to the caller's buffer cannot change the
  packet. `Information` and other byte fields are slices of that copy, not more copies.
- **`Raw` is not a `byte[]` property.** An array property hands out the packet's internals (CA1819). `GetBytes()`
  allocates a new array and says so by being a method.
- **`ToString()` returns the original line**, so logging and the debugger show the packet as received. Use
  `GetBytes()` or `Raw` when the bytes matter; `ToString()` is the Latin-1 view of them.
- **`HasHeader`** is false when the line has no `>` before the first `:`, or the source is empty. In that case
  `Source`, `Destination` and `Path` are null, `Information` is empty, `Type` is `Unknown` and `Outcome` is
  `Malformed`. `[MemberNotNullWhen]` lets callers skip null checks after `if (packet.HasHeader)`.
- **The data type** is the first byte of `Information` and is exposed through `Type`; it is not a separate public
  property until a caller needs one. Mic-E is identified by that byte, with its latitude in `Destination`.
- **Immutable and thread-safe.** Lazily created values (the `ToString()` text, decoded comment text) are cached with
  a benign race.
- **Equality is by reference.** "The same packet" means different things to different callers (APRS-IS duplicate
  detection compares source, destination and information but not the path), so comparers come later, by name.

### `Callsign`

A station identifier as it appeared in the header, which is not always a valid AX.25 callsign: APRS-IS carries
`T2PERTH`, `FIRST`, `DEMO-002`, `QA0AW-DS` and lower-case calls.

```csharp
public sealed record Callsign
{
    public string Value { get; }        // as received: "N0CALL-10", "EL-N0CALL", "qAR"
    public string Base { get; }         // before the first '-': "N0CALL"
    public string Ssid { get; }         // after the first '-', or "": "10", "DS", "002"
    public bool IsAx25 { get; }         // 1–6 upper-case letters/digits and an SSID of 0–15
}
```

`Value` is kept as received, never normalized, so a packet can be written back unchanged. Comparing callsigns is
case-sensitive by default (`qAR` and `QAR` are different things); a case-insensitive comparer can come later.

It is a class rather than a struct so that `Source` and `Destination` can be nullable references that
`[MemberNotNullWhen]` understands. A nullable struct would be `Nullable<Callsign>`, and callers would write
`packet.Source.Value.Value`. Callers never construct one; the constructor is internal.

A header entry with a space, a control byte or a byte above 0x7E gets an `InvalidCallsign` warning. Non-AX.25
identifiers do not: they are normal on APRS-IS, and `IsAx25` reports them.

### `PathEntry`

```csharp
public readonly record struct PathEntry(Callsign Station, bool HasBeenRepeated, PathEntryKind Kind);

public enum PathEntryKind { Station, Alias, QConstruct, Server }
```

- `HasBeenRepeated` is the trailing `*`; it is not part of `Station.Value`.
- `Alias` covers generic hops (`WIDEn-N`, `TRACEn-N`, `RELAY`, regional `XXn-N`, `TCPIP`, `NOGATE`, `RFONLY`),
  matched case-insensitively.
- `QConstruct` is `qA?`; the entry after it is the station that sent the packet into APRS-IS (`Server` or `Station`).

## Outcome, type and issues

These enums are the stable vocabulary the metrics plan asks for (`type`, `outcome`, `reason`). Adding a member is
fine, but renaming or reusing one breaks dashboards, so treat them as public contracts.

```csharp
public enum ParseOutcome
{
    Parsed,       // header and payload decoded; Issues may hold warnings
    Unsupported,  // header decoded, but this data type is not implemented yet
    Malformed,    // the header, or a payload this library implements, could not be decoded
}

public enum PacketType
{
    Unknown,              // no header, or an unrecognised data type identifier
    Position,             // ! = / @
    MicE,                 // ` ' (and old 0x1c 0x1d)
    Object,               // ;
    Item,                 // )
    Message,              // : (including ack, rej, bulletins, telemetry definitions)
    Status,               // >
    Weather,              // _ (positionless)
    Telemetry,            // T#
    ThirdParty,           // }
    Capabilities,         // <
    UserDefined,          // {
    // later: Query, Nmea, …
}

public readonly record struct ParseIssue(ParseIssueCode Code, ParseIssueSeverity Severity, int Offset);

public enum ParseIssueSeverity { Warning, Error }
```

- **One lenient parser, no strict mode.** The parser decodes what it can and records an issue for anything off-spec.
  Callers who want strict behaviour reject packets with any issue, or with specific codes. This replaces the
  "strict versus lenient mode" item in the plan.
- **`Offset`** is the byte position in `Raw` where the problem was found, so tools can point at it.
- **`ParseIssueCode`** is an enum with one member per distinct problem (`MissingHeader`, `EmptySource`,
  `EmptyPathEntry`, `InvalidCallsign`, …). It grows with each payload type. Messages
  for humans come from a `Describe()` extension, not from strings stored on every issue.
- **Warnings** do not change `Outcome` (a space or control byte in a callsign, an overlong information field).
  **Errors** make it `Malformed` (no header, an empty path entry, later a position with an invalid latitude).
- The metrics `reason` tag is the code of the first error.

This also replaces `ParseResult` and `UnknownPacket` from the plan: every result is an `AprsPacket`, and "unknown"
is `Type == Unknown`.

## Payloads

`Payload` is an abstract `AprsPayload` with one sealed subclass per type (`PositionReport`, `MicEReport`,
`ObjectReport`, `MessagePayload`, …), designed in M1 and M3. Callers switch on the type:

```csharp
switch (packet.Payload)
{
    case PositionReport p: …
    case MessagePayload m: …
}
```

A third-party packet (`}`) holds another `AprsPacket` parsed from the bytes after the `}`. It is parsed once, with no
deeper nesting, so a crafted packet cannot recurse.

### Position reports

`PositionReport` (`!`, `=`, `/`, `@`) is the first payload. Its building blocks are shared with objects, items and
Mic-E when those land: `AprsPosition` (latitude, longitude, ambiguity), `AprsTimestamp` (with `ToDateTimeOffset`
relative to the receive time), `AprsSymbol`, and the data-extension records (`PowerHeightGain`,
`DirectionFindingStrength`, `DirectionFindingReport`, `CompressionType`).

Decisions:

- **Ambiguity** keeps the blanked digits as zeros (the corner of the ambiguity box), as the APRS101 vectors expect;
  the latitude's ambiguity also blanks the longitude's digits.
- **One data extension** after an uncompressed position (course/speed with an optional DF bearing, or PHG, RNG,
  DFS). Anything else stays in the comment. `/A=` altitude is read from anywhere in the comment and left there.
- **`!DAO!`** (APRS 1.2) adds precision to unambiguous uncompressed positions and is removed from `Comment`;
  `CommentBytes` keeps every byte.
- **Comments** decode as UTF-8 when the bytes are valid UTF-8, otherwise as Latin-1.
- **X1J** (`!` after a beacon text) is not supported: APRS 1.1 erratum A11-ERR-01 deprecates it, so `!` is a data
  type only at offset 0. `!!` is raw Ultimeter weather, not a position.
- **Leniency seen in real traffic:** lower-case hemisphere letters decode with a `LowercaseHemisphere` warning, and a
  broken timestamp in front of a good position only loses the time (`InvalidTimestamp` as a warning). A missing or
  broken position is an error.
- **Weather on the weather-station symbol** (`_`): course and speed are the wind until the weather decoder (M3)
  reads the rest of the comment.

### Status reports

`StatusReport` (`>`, APRS101 ch. 16) never fails: a status is text. It reads a zulu `DDHHMMz` timestamp (the only
form the spec allows; other forms stay text, and an impossible zulu timestamp stays text with an `InvalidTimestamp`
warning), or a 4- or 6-character Maidenhead locator and symbol at the start (upper-cased whatever case it arrived in),
and a trailing `^HP` meteor-scatter beam heading and ERP. `Text` keeps everything after the timestamp or locator,
including the leading space after a locator and any `^HP` code, so it round-trips.

### Messages

The message format (`:ADDRESSEE:`, APRS101 ch. 13–15) carries two payloads:

- `MessagePayload`, with a `Kind`: text message, acknowledgement, rejection, bulletin (`BLN0`–`BLN9`, group
  bulletins such as `BLN4WX`), announcement (`BLNA`–`BLNZ`), NWS bulletin (`NWS-…`) or directed query (`?APRSD`,
  `?PING?`). The message ID is split off the text; an APRS 1.1 reply-ack `{MM}AA` gives `MessageId` `MM` and
  `ReplyAck` `AA` (empty when the sender only advertises support). `ack`/`rej` texts carry the answered ID in
  `MessageId`. A `{` not followed by a valid ID (1–5 characters, optional `}` and up to 5 more) stays in the text.
- `TelemetryDefinition` for `PARM.`, `UNIT.`, `EQNS.` and `BITS.`, which are sent as messages to the telemetry
  station's own callsign. `EQNS.` with fewer than five channels is accepted in whole triples, as real stations send
  it; unreadable equations or bits keep the definition kind with an `InvalidTelemetryDefinition` warning.

A missing addressee is the only error (`InvalidAddressee`). An addressee that is not padded to 9 characters
(SvxLink sends `:EL-N0CAL:`) or is padded past 9 (`:BLNRAIN    :`) is read with an `UnpaddedAddressee` warning, as
long as it is a 1–9 character callsign-like name followed by spaces and a colon within the first 12 bytes; anything
else after `:` (some stations put a position or a comment there) is still an error. Real traffic uses longer announcement
names than the spec (`BLNALUX`, `BLNA-WEB`); these read as announcement `A` with the rest as the group. NTS
radiogram routing inside the text (`N1\…`) is not decoded yet.

### Objects and items

`ObjectReport` (`;`) and `ItemReport` (`)`) carry the same position body as a position report, so the three share
an abstract base, `PositionedPayload`, with every position field; a map can `switch` on `PositionedPayload` once.
`PositionReport` adds only `IsMessagingCapable`. Objects add a 9-character name (padding removed), live or killed,
and a required timestamp (broken ones are a warning when the position is good, as for position reports); items add
a 3–9 character name and live or killed, and have no timestamp.

Two extensions apply to any positioned payload: an area symbol (`\l`) reads its `Tyy/Cxx` extension as an
`AreaShape` (shape, fill, colour, and size codes turned into degrees as code² / 1500, the APRS 1.1 correction), with
a line's corridor width from `{nnn}` in the comment; a signpost symbol (`\m`) reads its 1–3 character text from
`{…}`. Both stay in the comment. Weather in an object's comment and storm data on `\@` wait for the weather
decoder.

### Mic-E

`MicEReport` (`` ` ``, `'`, and the rev. 0 bytes 0x1C/0x1D) is also a `PositionedPayload`. The destination's six
characters give the latitude, ambiguity, message bits, N/S, the +100° longitude offset and E/W (its SSID is
ignored); the first nine information bytes give longitude, speed, course and symbol. Then, per APRS 1.1 and 1.2:

- an optional **type byte** (`␠`, `>`, `]`, `` ` ``, `'`) sets `IsMessagingCapable` and starts device detection;
- an optional `xxx}` **altitude** comes right after it (A11-ERR-02), otherwise `/A=` in the text is used;
- a trailing **device code** (Kenwood `=`/`^`, Yaesu `_x`, Byonics `|x` and the rest of the 2021 APRS 1.2 table) sets
  `Device` and is removed from `Comment`; an unrecognised code stays in the text (A12-MICE-02). Newer Yaesu codes
  (`_4`–`_7`) are not in that table and are left in the text until the maintained `aprs-deviceid` list is adopted;
- `!DAO!` precision applies, and a Maidenhead locator at the start of the text is reported too.

`IsCurrentFix` follows the data type (`` ` `` and 0x1C current), except that Kenwood TM-D700 radios send `'` for
current data (`']`). Speeds over 670 knots use the APRS 1.2 rule S × 112 − 74370. The original Mic-E telemetry is
superseded by the type codes and not decoded.

### Telemetry reports

`TelemetryReport` (`T#`) holds the `Sequence` as sent (`005` or `MIC`, with or without the comma after `MIC`) and
as a `SequenceNumber`, the raw `Analog` values, the eight `Digital` channels and the `Comment` after them. Real
traffic bends APRS101's three-digit 0–255 values, so, as aprs.fi does, values may be decimals, negative or larger,
sequences may have any number of digits, fewer than five analog values may be sent, an empty value is `null`, and the
digital channels may be left out. A value that is not a number, or digital channels that are not eight `0`/`1`
characters, make the report malformed (`InvalidTelemetry`). Values stay raw: applying a station's `EQNS.` needs its
earlier definition messages, which is a job for the caller.

### Weather

`WeatherData` holds the readings in named units (`TemperatureF`, `RainLastHourInches`, `PressureMillibars`…), each
`null` when not sent or sent as dots or spaces. It appears in two places:

- `PositionedPayload.Weather`, for a position, object or item with the weather station symbol (`_`, any table). The
  wind comes from `DIR/SPD` (or a compressed course and speed), which also stay in `Course` and `SpeedKnots` as
  before; the other readings come from the start of the comment, and `Comment` is the text after them.
  `CommentBytes` still holds every byte. A weather symbol whose comment has no readings has no `Weather`.
- `WeatherReport` (`_`), the positionless form, with an `MMDDHHMM` timestamp
  (`AprsTimestampFormat.MonthDayHourMinute`, always zulu). A bad timestamp makes it malformed.

Real stations bend APRS101, so the reader is lenient. Fields come in any order (the corpus has `P` before `p`, and
`b` before `h`). Any run of dots means no data (`b...` as well as `b.....`). Humidity may have three digits (`h100`,
`h092`). Some LoRa iGates send a compressed position without course and speed, then `DIR/SPD` in the comment, which
is read as the wind. Reading stops at the first byte that does not start a field, or at a field that repeats.
APRS101's software and unit type codes (`wRSW`) are not split out, because most stations put free text there; they
stay in the comment. APRS 1.1's luminosity (`L`/`l`), snowfall (`s`) and raw rain counter (`#`) are read; other
letters (`X`, `V`) are not in a published spec and stay in the comment. APRS101 says knots for `DIR/SPD` and mph for
the positionless form. Stations send mph in both, so `WindSpeedMph` is the value as sent.

Raw weather station data (`!!`, `#`, `*`, `$ULTW`) has no layout in APRS101, so it is recognised but not decoded:
`RawWeatherReport` names the format (Ultimeter 2000 data logging or packet mode, Peet Bros U-II) and keeps the data as
sent. `#` and `*` count only when hex digits follow, because stations also start beacon text with them. Storm data in
`\@` objects is not decoded yet.

### Station capabilities and user-defined data

`CapabilitiesReport` (`<`, APRS101 ch. 15) keeps every `TOKEN` or `TOKEN=VALUE` entry in order, trimmed, with
helpers for the IGate fields (`IsIGate`, `MessageCount`, `LocalStationCount`) and `GetValue` for the counters newer
IGates add (`PKT_CNT`, `RF_CNT`…). A list of tokens cannot be malformed, so it never fails.

`UserDefinedPayload` (`{`, APRS101 ch. 18) splits off the user ID and packet type and keeps the data as sent; `{{`
marks an experimental packet. The data belongs to the sending program and is not interpreted. A packet shorter than
its user ID and packet type is malformed (`InvalidUserDefined`).

## AX.25 and KISS (phase 2)

A TNC in KISS mode sends AX.25 frames, not TNC2 text: callsigns are shifted 7-byte fields with the SSID and
has-been-repeated flags in bit fields, and there is no `>`, `,` or `:`. Only the information field is the same bytes.

`AprsNet.Ax25` will decode frames into its own `Ax25Frame` type, which keeps the frame bytes. It converts to an
`AprsPacket` by rendering the TNC2 line and parsing it, so `AprsPacket.Raw` always means "the TNC2 line" whatever
the source. The rendering step is cheap next to radio data rates, and it keeps one parser for every source.

## Limits

All input is hostile. `Parse` rejects nothing for being long, but:

- decoding stops at 512 bytes of information (`InformationTooLong` warning); `Raw` still holds the whole line;
- path entries are capped (`TooManyPathEntries` error past 10; AX.25 allows 8 digipeaters, and APRS-IS adds q-construct
  entries);
- nothing allocates in proportion to an attacker-controlled count other than the single copy of the input.

## Testing

- Every APRS101 vector in `tests/AprsNet.Packets.SpecTests/Vectors` gets expected `Type`, `Outcome` and payload fields.
- Every corpus line must parse without throwing, and `ToString()` and `GetBytes()` must return it unchanged.
- The deliberately odd corpus lines (leading space, control bytes, bare `Q`, lower-case calls) get explicit expected
  issues, so a change in leniency shows up as a test failure.
- Fuzzing and property tests: `Parse` never throws on random bytes, and `Raw` always equals the input minus its line
  ending.

## Open questions

- [ ] Should `Callsign` also be usable on its own (`Callsign.Parse("N0CALL-9")`) for building packets, or only come
  from the parser until the encoder lands?
- [ ] Is 512 the right decoding cap? APRS-IS servers accept 512-byte lines; AX.25 allows a 256-byte information field.
- [ ] Should `PacketType` follow APRS101 chapter names (useful in docs) or the C# payload type names (useful in code)?
  The metrics plan asks the same question for the `type` tag.
