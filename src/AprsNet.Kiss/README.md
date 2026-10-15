# AprsNet.Kiss

KISS framing for TNCs: escape and frame data for a KISS TNC, and read frames back from a byte stream that splits them
anywhere.

> **Not published yet.** Phase 2 groundwork; the TCP and serial transports come next.

```csharp
var decoder = new KissDecoder();
foreach (var kiss in decoder.Decode(bytesJustRead))
    if (kiss.TryGetAx25(out var frame) && frame!.IsAprs)
        Handle(frame.ToAprsPacket());

byte[] wire = KissFrame.ForAx25(Ax25Frame.FromAprsPacket(packet)).Encode();
```
