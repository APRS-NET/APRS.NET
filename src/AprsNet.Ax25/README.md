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

## Digipeating

`Digipeater` decides which frames heard on RF an APRS digipeater repeats, and rewrites their path under the New-N
paradigm, as Dire Wolf does. It does no I/O: pass it each frame the TNC hears, and send the frames it returns.

```csharp
var digi = new Digipeater(new Ax25Address("N0CALL", 1, flag: false)); // fill-in: WIDE1-1 only

await foreach (var frame in tnc.ReadFramesAsync(cancellationToken))
{
    var decision = digi.Digipeat(frame);
    if (decision.IsRepeated)
    {
        await tnc.SendFrameAsync(decision.Frame, cancellationToken); // N0CALL-9>APRS,N0CALL-1*,WIDE2-1:...
    }
}
```

| Next hop in the path | Fill-in (default) | Wide-area (`FillInOnly = false`) |
| --- | --- | --- |
| Its call, such as `N0CALL-1` | Replaced by it, marked used | The same |
| An alias from `Aliases` | Replaced by its call, marked used | The same |
| `WIDE1-1` | Replaced by its call, marked used | The same |
| `WIDE2-2` | Not repeated | `N0CALL-1*,WIDE2-1`: its call inserted, N counted down |
| `WIDE2-1` | Not repeated | Replaced by its call, marked used |
| `WIDE3-3` and wider (over `MaxHops`, 2) | Not repeated | Trapped: replaced by its call, so it goes no further |

Every frame not repeated has a `DigipeatRefusal`: `NotAprs`, `NoHopsLeft`, `NotForUs`, `OwnFrame` (its own call as the
source) or `Duplicate` (a copy of one it repeated in the last 30 seconds, by any path). `Preemptive = true` also takes
its call or an alias later in the path, dropping the unused entries before it, but never a generic `WIDEn-N`.

A digipeater transmits on its own: run one only with a licence, and a wide-area one only by agreement with the local
APRS network.

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).
