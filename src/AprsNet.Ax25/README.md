# AprsNet.Ax25

AX.25 UI frames for APRS: decode and encode the frames a KISS TNC sends, and convert them to and from APRS.NET
packets.

```csharp
if (Ax25Frame.TryDecode(kissPayload, out var frame, out var error) && frame.IsAprs)
{
    AprsPacket packet = frame.ToAprsPacket(); // parsed from its TNC2 line, like an APRS-IS packet
}

var outgoing = Ax25Frame.FromAprsPacket(address.Create(new StatusReport { Text = "On the air" }));
byte[] bytes = outgoing.GetBytes(); // for a KISS data frame
```

A packet heard on APRS-IS carries a q-construct in its path and is refused: it goes to RF as third-party traffic.

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).
