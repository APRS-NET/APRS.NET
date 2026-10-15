# AprsNet.Filters

APRS-IS filter expressions evaluated locally, so a filter written for an APRS-IS server (`r/38.98/-94.67/50 t/m
-b/N0CALL*`) works the same on packets from a TNC, a file or a gateway.

```csharp
var filter = AprsFilter.Parse("r/38.98/-94.67/50 t/wm");
if (filter.Matches(packet)) { … }
```

Not published yet: this is groundwork for the phase 3 gateway.
