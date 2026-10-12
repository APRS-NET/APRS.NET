# Packet API design

Status: **agreed; the header parser is implemented, payloads are not** (M1, `AprsNet.Packets`). This is the shape of `AprsPacket` and how text
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
    // later: Capabilities, Query, Nmea, UserDefined, …
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
