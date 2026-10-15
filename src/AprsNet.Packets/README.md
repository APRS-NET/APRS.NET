# AprsNet.Packets

APRS packet models, parser and encoder. No I/O.

> **Alpha pre-release.** APRS.NET packages are versioned `0.x.y-alpha.N` until at least 1.0.0, and the API can change
> between any two releases. Install with `--prerelease`. Not for production use.

## Parsing

`AprsPacket.Parse` never throws for malformed data. It always returns a packet holding the exact input, and reports
what it could not decode in `Outcome` and `Issues`.

```csharp
var packet = AprsPacket.Parse(line); // a TNC2 line: bytes, or a string read as Latin-1
if (!packet.HasHeader)
{
    Console.WriteLine($"Not a packet: {packet} ({string.Join(", ", packet.Issues)})");
    return;
}

Console.WriteLine($"{packet.Source} via {string.Join(",", packet.Path)}: {packet.Type}");
byte[] original = packet.GetBytes(); // exactly what was parsed, minus the line ending
```

Strings must hold one character per byte (Latin-1), as `AprsIsClient` returns them: APRS packets are bytes, and
Mic-E data is not valid UTF-8. `Parse(string)` throws `ArgumentException` for a character above U+00FF, because such
a string has already lost bytes.

Position reports are decoded: plain and compressed positions, ambiguity, timestamps, symbols, course and speed,
altitude, PHG, RNG, DFS, DF bearings and `!DAO!` precision. Status reports are decoded too: text, zulu timestamp,
Maidenhead locator and symbol, and meteor-scatter beam heading and power. Messages are decoded as `MessagePayload`
(text messages with IDs and APRS 1.1 reply-acks, acks, rejects, bulletins, announcements, NWS bulletins and directed
queries) or `TelemetryDefinition` (`PARM.`, `UNIT.`, `EQNS.`, `BITS.`). Objects and items are decoded as
`ObjectReport` and `ItemReport`, including area shapes and signposts, and Mic-E reports as `MicEReport` (position,
speed and course, message, altitude, device and status text). Telemetry reports are decoded as `TelemetryReport`
(sequence, raw analog values, digital bits and comment). Weather is decoded as `WeatherData`: on positions, objects
and items with the weather station symbol (`PositionedPayload.Weather`), and in positionless reports
(`WeatherReport`); raw weather station data is recognised as `RawWeatherReport` (the station format and its data,
which APRS101 does not lay out). Station capabilities such as an IGate's `<IGATE,MSG_CNT=0,LOC_CNT=1` are decoded as
`CapabilitiesReport`, and user-defined packets (`{`) as `UserDefinedPayload` (user ID, packet type and the data as
sent). Other packet types (queries, raw NMEA, third-party…) come back with `Outcome == ParseOutcome.Unsupported`
until their decoders land.

```csharp
if (packet.Payload is PositionedPayload report) // a position report, object, item or Mic-E report
{
    Console.WriteLine($"{report.Position.Latitude:0.00000} {report.Position.Longitude:0.00000} {report.Symbol}");
    if (report.Course is { } course && report.SpeedKnots is { } speed) Console.WriteLine($"{course}° at {speed} kn");
}
```

[docs/packet-types.md](https://github.com/APRS-NET/APRS.NET/blob/main/docs/packet-types.md) lists every APRS data type, whether it
decodes and builds, and how much of the test corpus it covers.

`AprsPacket.Create` builds packets from payloads, the reverse of `Parse`, for every payload type it decodes except raw
weather station data. Mic-E reports replace the destination with their latitude:

```csharp
var packet = AprsPacket.Create(
    Callsign.Parse("N0CALL-9"),
    Callsign.Parse("APZ123"),
    [PathEntry.Parse("WIDE1-1")],
    new MessagePayload { Addressee = "WU2Z", Text = "Testing", MessageId = "003" });
// N0CALL-9>APZ123,WIDE1-1::WU2Z     :Testing{003

var beacon = new PositionReport
{
    Position = new AprsPosition(49.05833, -72.02917),
    Symbol = new AprsSymbol('/', '>'),
    Course = 88,
    SpeedKnots = 36,
    Comment = "On the road",
};
// payload: !4903.50N/07201.75W>088/036On the road

var weather = new PositionReport
{
    Position = new AprsPosition(49.05833, -72.02917),
    Symbol = new AprsSymbol('/', '_'),
    Weather = new WeatherData { WindDirection = 220, WindSpeedMph = 4, TemperatureF = 77, HumidityPercent = 50 },
};
// payload: !4903.50N/07201.75W_220/004g...t077h50
```

An app sends as one station, so `PacketAddress` holds its source, TOCALL and default path, checked once:

```csharp
var rf = PacketAddress.Parse("N0CALL-9", "APZ123", ["WIDE1-1", "WIDE2-1"]);
var aprsIs = rf with { Path = [] };

rf.Create(beacon);                                   // N0CALL-9>APZ123,WIDE1-1,WIDE2-1:!4903.50N/...
aprsIs.Create(new StatusReport { Text = "QRV" });     // N0CALL-9>APZ123:>QRV
rf.Create(beacon, [PathEntry.Parse("WIDE2-2")]);     // this packet only
```

`MaidenheadLocator` converts between Maidenhead locators (4, 6, 8 or 10 characters) and positions, following the
IARU Region 1 VHF Handbook:

```csharp
var locator = MaidenheadLocator.Parse("fn31pr");                    // FN31pr
Console.WriteLine($"{locator.Center} {locator.SouthWest} {locator.NorthEast}");
MaidenheadLocator.FromPosition(48.858222, 2.2945, 10);               // JN18du55ix

if (!MaidenheadLocator.TryParse(input, out var parsed, out var error))
    Console.WriteLine(error.Message);                               // Character 3 ('Z') must be a digit 0-9 (square).
```

`Geodesy` measures on a sphere of 6371 km, as APRS-IS servers do for range filters, and `AprsUnits` converts the
units APRS sends:

```csharp
var km = Geodesy.DistanceKm(home, station.Position);
var bearing = Geodesy.InitialBearing(home, station.Position);
Console.WriteLine($"{km:F1} km {Geodesy.CompassPoint(bearing)}");    // 12.3 km NNE
var corner = Geodesy.Destination(home, 45, 50);                      // 50 km north-east
var line = Geodesy.PointsAlong(home, station.Position, 32);          // a great-circle line to draw

var area = position.GetAmbiguityBounds();                            // where an ambiguous position may be
AprsUnits.Convert(weather.TemperatureF!.Value, TemperatureUnit.Fahrenheit, TemperatureUnit.Celsius);
AprsUnits.Convert(weather.PressureMillibars!.Value, PressureUnit.Millibars, PressureUnit.InchesOfMercury);
```

`PathAnalysis` reads how a packet travelled from its path:

```csharp
var path = PathAnalysis.FromPacket(packet);  // N0CALL>APRS,N1ABC-1*,WIDE2-1,qAR,N2ABC-10:...
path.QConstruct;                              // qAR
path.EntryStation;                            // N2ABC-10, the IGate
path.Digipeaters;                             // [N1ABC-1]
path.IsGatedFromRadio;                        // true
path.IsHeardDirectly;                         // false: a digipeater repeated it
path.RemainingHopCount;                       // 1
```

`PacketMerger` reads several sources as one stream, naming each packet's source and marking a packet heard again on
any source within 30 seconds as a duplicate of the first copy:

```csharp
await using var merger = new PacketMerger();
var aprsIs = merger.Add("aprs-is", connection.ReadPacketsAsync(token));
var tnc = merger.Add("tnc", kiss.ReadPacketsAsync(token));   // completes, or faults, when that source ends
await foreach (var merged in merger.ReadAllAsync(token))
{
    Console.WriteLine(merged.IsDuplicate
        ? $"{merged.Source}: also heard on {merged.DuplicateOf!.Source}"
        : $"{merged.Source}: {merged.Packet}");
}
```

Input a person types (`Callsign.Parse`, `PathEntry.Parse`, `PacketAddress.Parse`) is refused with
`AprsValidationException`, an `ArgumentException` with a stable `Code` (`AprsValidationErrorCode`) for an app to
translate, and `Index`/`Character` for the offending character. Codes are never renamed; the English message may be.

See [the design notes](https://github.com/APRS-NET/APRS.NET/blob/main/docs/design/packet-api.md).

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).

<sub>Developed with AI assistance; see the [AI declaration](https://github.com/APRS-NET/APRS.NET/blob/main/AI-DECLARATION.md).</sub>
