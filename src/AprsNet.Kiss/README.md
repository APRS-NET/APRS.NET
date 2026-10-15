# AprsNet.Kiss

KISS framing for TNCs: escape and frame data for a KISS TNC, and read frames back from a byte stream that splits them
anywhere.

> **Not published yet.** Phase 2 groundwork.

```csharp
var decoder = new KissDecoder();
foreach (var kiss in decoder.Decode(bytesJustRead))
    if (kiss.TryGetAx25(out var frame) && frame!.IsAprs)
        Handle(frame.ToAprsPacket());

byte[] wire = KissFrame.ForAx25(Ax25Frame.FromAprsPacket(packet)).Encode();
```

`KissTcpClient` talks to a TNC that serves KISS over TCP, such as Dire Wolf (port 8001):

```csharp
await using var tnc = new KissTcpClient("localhost");
await tnc.ConnectAsync();
_ = Task.Run(async () =>
{
    await foreach (var packet in tnc.ReadPacketsAsync())
        Console.WriteLine(packet);
});
await tnc.SendAsync(address.Create(new StatusReport { Text = "On the air" }));
```

For a serial, USB or Bluetooth TNC, open the port yourself and hand its stream to `KissStreamClient`, which does the
same over any readable and writable stream (no serial-port dependency in this package):

```csharp
using var serial = new SerialPort("/dev/ttyUSB0", 9600);
serial.Open();
await using var tnc = new KissStreamClient(serial.BaseStream);
```
