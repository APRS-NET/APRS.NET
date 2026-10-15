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

## Maidenhead locators and geodesy

`MaidenheadLocator` converts between Maidenhead (IARU) locators and positions, following the IARU Region 1 VHF
Handbook 10.03 (February 2026), section 20.2, "Definition of the IARU locator". It reads 4, 6, 8 and 10 characters
(field, square, subsquare, extended square, extended subsquare), ignoring case and surrounding whitespace, and gives
the squaroid's centre (the IARU reference location) and its south-west and north-east corners. `FromPosition` goes
the other way at a chosen length.

Decisions:

- **Home.** Geodesy lives in `AprsNet.Packets`, not a separate package: `AprsPosition` is defined here, the rest of
  AprsMap's gap G08 (distance and bearing, destination point, the ambiguity box of an `AprsPosition`, unit
  conversions, the PHG range formula) builds on it, and the decoders already read locators in status and Mic-E
  reports. A separate `AprsNet.Geo` would either depend on `AprsNet.Packets` or be depended on by it, so it could not
  hold all of G08. The package stays I/O-free and BCL-only.
- **Edges follow the handbook's test cases** (section 20.2.7): longitude 180° is the same meridian as 180° W and falls
  in field `A` (`AJ00AA` on the equator), latitude 90° falls in the last row, and at the poles, where longitude is
  undefined, the longitude characters are the lowest (`AA00AA`, `AR09AX`). The handbook prints the ten-character South
  Pole locator as `A00AA00AA`, one character short; `AA00AA00AA` is what its rules give.
- **Exact cells.** Positions are converted to the finest squaroids (1/2880° of longitude, 1/5760° of latitude) as
  integers once, and shorter locators are taken from those, so a locator is always a prefix of the longer ones and a
  value within floating-point error of an edge lands on the edge (2° W, 50° N is `IO90`).
- **Written form.** The handbook writes locators in upper case and recommends accepting any case. `Value` uses the
  common written form with lower-case subsquare letters (`FN31pr`, `JN18du55ix`), which AprsMap shows to users.
- **User input.** `TryParse` never throws and reports why text is not a locator: empty, a character out of range for
  its position (with its index in the text as given and the pair it belongs to), or the wrong length. Characters are
  checked before the length, so a typo is reported where it is. `Parse` throws `FormatException` with the same message.
- **Decoders.** The status and Mic-E decoders check locators with the same rules; their `MaidenheadLocator` string
  properties can be passed to `MaidenheadLocator.Parse`.

## Building packets

`AprsPacket.Create(source, destination, path, payload)` is the reverse of `Parse`: it writes the header and encodes
the payload as the information field. Callers build payloads with object initializers; the properties a caller sets
are `public init`, while the ones the parser derives (a status report's beam heading, a message's bulletin ID or query
type) and the exact-byte views (`TextBytes`, `CommentBytes`) stay read-only.

```csharp
var packet = AprsPacket.Create(
    Callsign.Parse("N0CALL-9"),
    Callsign.Parse("APZ123"),
    [PathEntry.Parse("WIDE1-1"), PathEntry.Parse("WIDE2-1")],
    new MessagePayload { Addressee = "WU2Z", Text = "Testing", MessageId = "003" });
packet.ToString(); // N0CALL-9>APZ123,WIDE1-1,WIDE2-1::WU2Z     :Testing{003
```

Decisions:

- **One model.** The payload types are the parser's own, not separate builder types, so a parsed payload can be
  rebuilt as it is and round-trip tests compare like with like.
- **The result is parsed back.** `Create` returns the parser's view of the bytes it wrote, so the packet is exactly
  what a receiver decodes. If that differs from the payload given (status text that a receiver would read as a
  timestamp, a message whose kind contradicts its addressee), `Create` throws `ArgumentException` instead of returning
  a packet that would be misread.
- **Text.** Text set by a caller is written as UTF-8 (APRS 1.2). Text that came from the parser keeps its original
  bytes, so a Latin-1 comment is not re-encoded. Control characters are rejected.
- **Spec limits apply to new text only.** Message text from a caller follows APRS101 (at most 67 characters, no `|`,
  `~` or `{`); parsed text is written back as received.
- **Normalisation.** `Create` writes the canonical form: addressees padded to exactly 9 characters and Maidenhead
  locators in upper case, as the parser already reports them. Every other status report and message in the corpus
  rebuilds byte for byte.
- **Headers.** `Callsign.Parse` accepts 1 to 9 printable ASCII characters without spaces or the header separators
  (`>`, `:`, `,`, `*`); it does not require AX.25 (check `IsAx25` for RF). `PathEntry.Parse` takes the wire form,
  with a trailing `*` for an entry that has been repeated.
- **Error codes for typed input.** Where a person types the input (callsigns, path entries, a `PacketAddress`, and in
  the APRS-IS client the login settings, filter, server address and passcode callsign), the error is an
  `AprsValidationException`: still an `ArgumentException`, so existing handlers catch it, with a `Code` from
  `AprsValidationErrorCode`, the refused `Value`, and the `Index` and `Character` at fault. An app shows the error in
  its reader's language from the code, which works when the error is raised on a server far from the reader. The
  codes are a stable vocabulary like `ParseIssueCode`: added to, never renamed or reused. `MaidenheadLocator.TryParse`
  keeps its own `MaidenheadParseError`, which already has a kind, part, index and character. Errors in values a
  program sets (timeouts, payload fields) stay plain `ArgumentException`s.
- **Timestamps.** `AprsTimestamp.DayHourMinute`, `HourMinuteSecond`, `MonthDayHourMinute` and `FromDateTimeOffset`
  create them; `ToString()` is the wire form.
- **Positions are rounded to the format.** Uncompressed positions are sent in hundredths of a minute, speeds in
  whole knots, PHG as its codes (power 0, 1, 4 … 81 W, height 10 × 2ⁿ ft), so `Create` checks that the position
  lands within the format's precision and that the symbol, timestamp, extension and comment survive; the returned
  packet shows the exact values sent. Ambiguity blanks the same digits the parser reads; give the box corner.
- **One data extension.** Course and speed, PHG, range or DF signal strength, as APRS101 allows after an uncompressed
  position; more than one throws. A compressed position carries course and speed, else range, else altitude in its
  `cs` bytes, and has no PHG, DFS or ambiguity.
- **Altitude** goes in front of the comment as `/A=` (the parser reads it from anywhere and leaves it in `Comment`),
  unless the comment already has one or a compressed position carries it.
- **Parsed spellings are kept.** The format spells some values several ways (unknown course and speed as `.../...`
  or spaces, the ignored `csT` bytes of a compressed position without course or speed), so a parsed payload keeps the
  bytes its data extension arrived with. Every position report in the corpus rebuilds byte for byte, apart from what
  the parser normalises (lower-case hemispheres, a broken timestamp it dropped, ambiguity applied to both
  coordinates). A second corpus test copies only the settable values into new reports, so extensions are also encoded
  from their values.
- **Not settable yet:** weather readings, the `!DAO!` datum, area shapes, DF bearing reports and signposts are
  derived from the comment or not encodable on their own yet; parsed payloads carry them through unchanged.

### Sending as one station

An app sends every packet with the same source, the same destination (its TOCALL) and usually the same path.
`PacketAddress` holds those three, checked when it is made, and `Create(payload)` passes them to `AprsPacket.Create`:

```csharp
var rf = PacketAddress.Parse("N0CALL-9", "APZ123", ["WIDE1-1", "WIDE2-1"]);
var aprsIs = rf with { Path = [] };
rf.Create(beacon);
rf.Create(beacon, [PathEntry.Parse("WIDE2-2")]); // this packet only; rf.Path is unchanged
```

- **A record, not a builder.** Only the header is shared between packets; the payload stays the parser's own record,
  built with an object initializer. A fluent builder (`.Speed(36).Altitude(1200)`) would be a second model of the same
  fields, so `Parse(Create(x))` round-trips would compare builder state with parsed payloads, `with` edits on a parsed
  payload would no longer feed back into sending, and values with units would lose their names (`SpeedKnots`,
  `AltitudeFeet`).
- **Immutable.** The parts are checked once, in the constructor or `Parse`, and again by `with`, so an address that
  exists is sendable. A beacon scheduler, a messaging queue and an object timer can share one instance across threads
  without locking; a changed path or callsign is a new address.
- **What is checked.** The source is a callsign of 1 to 6 letters or digits with an optional SSID of 1 or 2 letters or
  digits, without the `*` repeated marker; APRS-IS accepts SSIDs such as `-L` that AX.25 cannot carry, so check
  `Source.IsAx25` before sending on RF. The destination must fit AX.25, as every TOCALL does. Path entries follow
  `PathEntry.Parse`, at most 10. Each error names the part (`The source 'N0CALL*' …`) and the parameter.
- **Mic-E.** A Mic-E packet carries its latitude in the destination in place of the TOCALL, so for a `MicEReport`
  `Create` sends that instead of `Destination`, keeping only the TOCALL's SSID (see Mic-E reports below).

### Mic-E reports

`AprsPacket.Create` writes a `MicEReport`'s latitude, message and the north, longitude-offset and west flags into the
destination, replacing the one given; that destination's SSID is kept, because Mic-E reads it as a digipeater path
code (APRS101 ch. 10). `PacketAddress.Create` does the same with its TOCALL.

```csharp
var report = new MicEReport
{
    Position = new AprsPosition(33.42733, -112.129),
    Symbol = new AprsSymbol('/', 'j'),
    Message = MicEMessage.Returning,
    IsCurrentFix = true,
    IsMessagingCapable = true,
    Course = 251,
    SpeedKnots = 20,
    Comment = "Mobile",
};
AprsPacket.Create(source, Callsign.Parse("APZ123"), [], report).ToString();
// N0CALL-9>S32UVT:`(_fn"Oj/`Mobile
```

- **Settable:** `Message` (any but `Unknown`, which mixes standard and custom bits), `IsCurrentFix` (`` ` `` or `'`
  data type), `IsMessagingCapable`, and the shared position fields: position and ambiguity, symbol, course, speed,
  altitude and comment. Mic-E has no timestamp, compression, PHG, range or DFS; setting one throws.
- **Course and speed are always sent.** The format has no "unknown" form, so a missing course or speed is sent as 0
  (APRS reads course 0 as unknown). Speeds are whole knots up to 670: higher values decode by the APRS 1.2 rule for
  fast objects such as the ISS.
- **Printable where the format allows.** Speeds under 40 knots use the `l`–`o` form of the speed byte, and a speed
  ending in 0 uses the +400° form of the next byte, so neither is a control character. The course's tens and units byte has no such form, so
  courses ending in 00–03 or 99 still send a control character or DEL.
- **Type byte.** A new report sends `` ` `` when it can receive messages and a space (original Mic-E) when not, then
  the altitude as base-91 metres (`xxx}`), then the comment, with no device code. After `` ` `` a receiver still reads
  the last two bytes as a device code, so a comment ending in one (` X` is the AP510) is refused.
- **Longitude.** 0–9° and 100–109° each have two spellings; new reports use APRS101's (the offset flag with
  bytes 118–127 and 108–117), and parsed reports keep theirs.
- **Parsed reports rebuild byte for byte.** The type, longitude, speed and course bytes and the text after the symbol
  are kept as they arrived. All 3,138 Mic-E packets in the corpus rebuild exactly, destination included, apart from
  those whose message mixes standard and custom bits. A second corpus test rebuilds each from its values alone.

### Objects and items

`ObjectReport` and `ItemReport` share the position body with position reports (`PositionEncoder.WriteBody`), so the
rules above for positions, extensions, altitude and comments apply to them unchanged.

```csharp
var leader = new ObjectReport
{
    Name = "LEADER",
    Timestamp = AprsTimestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
    Position = new AprsPosition(49.05833, -72.02917),
    Symbol = new AprsSymbol('/', '>'),
};
address.Create(leader); // ;LEADER   *092345z4903.50N/07201.75W>

// Later, to take it off the map: the same object, killed.
address.Create(new ObjectReport
{
    Name = leader.Name,
    IsLive = false,
    Timestamp = AprsTimestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
    Position = leader.Position,
    Symbol = leader.Symbol,
}); // ;LEADER   _100012z4903.50N/07201.75W>
```

- **New objects and items are live.** `IsLive` defaults to `true`, so forgetting it cannot send a kill; to remove one
  from the map, send it again with `IsLive = false`.
- **Names.** An object name is 1 to 9 printable ASCII characters without trailing spaces, padded to 9 when sent. An
  item name is 3 to 9 printable ASCII characters without `!` or `_`, which end it on the wire. Parsed names are written
  back as received.
- **Timestamps.** An object needs one (`DDHHMMz`, `DDHHMM/` or `HHMMSSh`); an item has none, and setting one throws.
  A parsed object whose broken timestamp the parser dropped cannot be rebuilt.
- **Round trips.** All 6,540 objects and items in the corpus with a usable timestamp rebuild byte for byte, and a
  second corpus test rebuilds them from their values alone.

### Sending weather

`WeatherData` is a record of readings, each `null` when not sent, so a station can keep one and send
`readings with { TemperatureF = 71 }`. It goes on a position, object or item with the weather station symbol (the
complete format APRS 1.1 recommends), or in a positionless `WeatherReport`.

```csharp
var readings = new WeatherData
{
    WindDirection = 220, WindSpeedMph = 4, WindGustMph = 5, TemperatureF = 77,
    RainLastHourInches = 0, HumidityPercent = 50, PressureMillibars = 990,
};

address.Create(new PositionReport
{
    Position = new AprsPosition(49.05833, -72.02917),
    Symbol = new AprsSymbol('/', '_'),
    Weather = readings,
}); // !4903.50N/07201.75W_220/004g005t077r000h50b09900

address.Create(new WeatherReport
{
    Timestamp = AprsTimestamp.FromDateTimeOffset(DateTimeOffset.UtcNow, AprsTimestampFormat.MonthDayHourMinute),
    Weather = readings,
}); // _10090556c220s004g005t077r000h50b09900
```

- **Wind is the course and speed.** In the complete format the wind is the `DIR/SPD` extension (or the compressed
  course and speed), taken from `WindDirection` and `WindSpeedMph`. Setting `Course` or `SpeedKnots` as well is
  allowed when they agree; when they differ, `Create` throws.
- **Required fields as dots.** APRS101 puts wind, gust and temperature in every report, so they are written as dots
  when unknown (`.../...g...t...`). The other fields are written only when set, in APRS101 order:
  `r p P h b L/l s #`.
- **Units.** Values are rounded to whole degrees, mph and °F, hundredths of an inch of rain, tenths of a millibar and
  whole W/m². Snowfall keeps one decimal below 10 inches (`s1.5`). Humidity is 1 to 100, as `h00` means 100%.
- **Readings lead the comment.** A receiver reads fields from the first byte after the extension, so the readings
  come first, then a `/A=` altitude, then the comment. A comment that would itself read as a field (`h45 in the
  shade`) is refused.
- **Positionless reports** need a `MonthDayHourMinute` timestamp and cannot carry snowfall, since their `s` is the wind
  speed. APRS 1.1 discourages them; prefer the complete format.
- **The weather symbol is required.** Readings on any other symbol, or on a Mic-E report, throw.
- **Round trips.** All 5,527 weather packets in the corpus (with and without a position) rebuild byte for byte: parsed
  readings keep their spelling and order. Second corpus tests rebuild them from their values alone, in canonical order.

### Telemetry

A station sends raw channel values in `T#` reports, and tells receivers how to read them with four definitions, each a
message addressed to the station's own callsign.

```csharp
var address = PacketAddress.Parse("N0QBF-11", "APZ123", []);
address.Create(new TelemetryReport
{
    SequenceNumber = 5,
    Analog = [199, 0, 255, 73, 123],
    Digital = [false, true, true, false, true, false, false, true],
}); // T#005,199,000,255,073,123,01101001

address.Create(new TelemetryDefinition
{
    Addressee = "N0QBF-11",
    Kind = TelemetryDefinitionKind.Equations,
    Equations = [new(0, 5.2, 0), new(0, 0.53, -32), new(3, 4.39, 49), new(-32, 3, 18), new(1, 2, 3)],
}); // :N0QBF-11 :EQNS.0,5.2,0,0,0.53,-32,3,4.39,49,-32,3,18,1,2,3
```

- **Raw values, scaled by EQNS.** New reports carry whole numbers 0 to 999 (APRS 1.2 widened APRS101's 0 to 255), as
  three digits. A reading such as 13.8 V is sent as a raw value with an equation that scales it; decimals and
  negatives are refused. Parsed reports keep whatever their station sent.
- **Shape.** One to five analog values; a missing one is an empty field. The eight digital channels need all five
  analog values in front of them, and a comment needs the digital channels, because a receiver reads anything after
  a short list of values as another value.
- **Sequence.** `SequenceNumber` 0 to 999, written as three digits; wrap it with `% 1000`. `Sequence = "MIC"` writes
  the Mic-E form.
- **Definitions.** `PARM.` and `UNIT.` take 1 to 13 labels without commas; `EQNS.` one to five equations, written in
  the shortest form that reads back exactly; `BITS.` eight `0`/`1` characters and an optional project name. The text
  must fit a 67-character message. A new definition is addressed to a callsign; a parsed one keeps its addressee.
- **Comment telemetry.** Trackers also append base-91 telemetry to a position comment (APRS 1.2): `|SS11223344|`,
  two characters each for the sequence and one to five values 0 to 8280, then the eight digital channels as one value
  after all five. It decodes into `PositionedPayload.Telemetry`, a `TelemetryReport`, so the station's definitions
  apply to it, and is left out of `Comment`. The last valid block wins, and it is read before the `!DAO!` search
  because its bytes can look like a DAO token. Setting `Telemetry` on a position, object, item or Mic-E report writes
  the block after the comment; a comment that would itself read as a block is refused.
- **Round trips.** All 7,708 telemetry reports and definitions in the corpus rebuild byte for byte. A second corpus
  test rebuilds them from their values alone, leaving out what the encoder refuses in new payloads: decimal or
  negative values, and SvxLink's 69-character `UNIT.` definitions.

### Capabilities and user-defined data

`CapabilitiesReport.ForIGate(messageCount, localStationCount)` builds the `<IGATE,MSG_CNT=n,LOC_CNT=n` an IGate
announces; any other list of tokens can be set on `Capabilities`. Tokens and values cannot contain commas (tokens no
`=`, neither surrounding spaces), since the receiver splits and trims on them. `UserDefinedPayload` writes `{`, a
printable user ID (`{` for experimental), a printable packet type and the data. Capabilities text now decodes like
every other text field, as UTF-8 when valid and Latin-1 otherwise, so a WinAPRS `DX=…(12.5mi 241°)` reads the same
after a round trip. All 1,433 of both in the corpus rebuild byte for byte and from their values alone.

### Third-party traffic

`}` carries a whole packet that a gateway passes on from another network: `}W1AW>APRS,TCPIP,N0CALL*:>Status`.
`ThirdPartyPayload.Packet` is that packet, parsed like any other line, so it has its own header, payload and issues; a
broken carried packet does not make the wrapper malformed. The wrapper is malformed (`InvalidThirdParty`) only when
the text after `}` has no `SOURCE>DESTINATION:` header. Nesting is unwrapped to three levels (`ThirdPartyTooDeep`
beyond that), and the carried packet is never cut at the 512-byte information limit, since its own header counts
toward it. `new ThirdPartyPayload(packet)` wraps a packet for `Create`, which writes `}` and the packet's raw bytes.
IGates unwrap third-party traffic before it reaches APRS-IS, so the corpus has none; it is tested from APRS101's
examples.

Supported: every payload the parser decodes, apart from raw weather station data (`!!`, `$ULTW`, `#`, `*`), which
APRS 1.1 discourages and whose layouts belong to the manufacturers; it throws `NotSupportedException`.

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

- [x] Should `Callsign` also be usable on its own for building packets? **Decided: yes,** `Callsign.Parse` and
  `TryParse` (see Building packets).
- [ ] Is 512 the right decoding cap? APRS-IS servers accept 512-byte lines; AX.25 allows a 256-byte information field.
- [ ] Should `PacketType` follow APRS101 chapter names (useful in docs) or the C# payload type names (useful in code)?
  The metrics plan asks the same question for the `type` tag.
