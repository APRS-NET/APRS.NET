# AprsNet.Filters

APRS-IS filter expressions evaluated locally, so a filter written for an APRS-IS server (`r/38.98/-94.67/50 t/m
-b/N0CALL*`) works the same on packets from a TNC, a file or a gateway.

```csharp
var filter = AprsFilter.Parse("r/38.98/-94.67/50 t/wm");
if (filter.Matches(packet)) { … }
```

`AprsFilterTerm` builds filters from typed parts and writes the server syntax, independent of the current culture.
A built filter is the same as the parsed one, so it can be sent with `SetFilterAsync` or evaluated here:

```csharp
var built = AprsFilter.Create(
    AprsFilterTerm.Area(mapBounds),
    AprsFilterTerm.Buddies("N0CALL*", "N1ABC-7"),
    AprsFilterTerm.Types(AprsFilterTypes.Weather).Exclude());
built.ToString();             // a/50.05/-73.03/49.05/-72.03 b/N0CALL*/N1ABC-7 -t/w
built.CheckServerLimits();    // null, or why a server would refuse it (more than 9 r/ or f/ terms)
```

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).
