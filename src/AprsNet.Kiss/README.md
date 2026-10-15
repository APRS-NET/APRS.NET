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

### Transmit limit

Every client limits how fast it sends frames for the TNC to transmit, so a bug or a loop cannot flood a shared radio
channel: 3 frames at once, then one every 10 seconds. A send waits for its turn rather than fails; cancel it with its
token. `KissTcpConnection` keeps one limit across reconnects. To change it, or share one limit between clients on the
same TNC, pass a `KissTransmitLimit`:

```csharp
var limit = new KissTransmitLimit(burst: 1, interval: TimeSpan.FromSeconds(30));
await using var tnc = new KissTcpConnection("localhost", transmitLimit: limit);
```

`KissTransmitLimit.Off` removes the limit, for a channel the app has to itself or a test.

### Sharing a TNC: KissTcpServer

`KissTcpServer` is a KISS TCP service, like Dire Wolf's port 8001: several KISS apps connect at once and each receives
every frame you publish; the frames they send go to `Transmit`. Put it in front of one TNC so several apps can share
it, with one transmit limit for all of them:

```csharp
await using var tnc = new KissTcpConnection("localhost");
await using var server = new KissTcpServer(new IPEndPoint(IPAddress.Loopback, 8101)) { Transmit = tnc.SendFrameAsync };
server.Start();
await foreach (var frame in tnc.ReadFramesAsync())
    server.Publish(frame);
```

`PublishFromAprsIs(packet, gateway)` sends a packet heard on APRS-IS to the apps as third-party traffic from your
callsign (`N0CALL-10>APZNET:}N1ABC>APRS,TCPIP,N0CALL-10*:…`), so apps that only speak KISS see internet traffic too.
An app that falls 1,000 frames behind is disconnected.
