# AprsNet.Kiss

Talk to a KISS TNC, such as Dire Wolf or a hardware TNC: read the APRS packets it hears and send packets for it to
transmit. Below the clients, the KISS framing itself: escape and frame data, and read frames back from a byte stream
that splits them anywhere.

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

### Staying connected

`KissTcpConnection` reconnects by itself, and its read streams carry on across reconnects. `State` says where it is
(`Idle`, `Connecting`, `Connected`, `Reconnecting`, `Closed`), and `StateChanged` reports each change, as
`AprsIsConnection` does; a `Reconnecting` change carries the error, if any, and the wait before the next attempt:

```csharp
tnc.StateChanged += (_, e) =>
{
    if (e.State == KissConnectionState.Reconnecting)
        Console.WriteLine($"TNC lost ({e.Error?.Message ?? "closed"}); retrying in {e.RetryIn}");
};
```

The `AprsNet.Kiss` meter counts frames received, sent and dropped, open connections and reconnects by cause, each tagged
with the TNC (`host:port`); add it to OpenTelemetry with `AddMeter("AprsNet.Kiss")`.

### Transmit limit

Every client limits how fast it sends frames for the TNC to transmit, so a bug or a loop cannot flood a shared radio
channel: 3 frames at once, then one every 10 seconds. A send waits for its turn rather than fails; cancel it with its
token. `KissTcpConnection` keeps one limit across reconnects. To change it, or share one limit between clients on the
same TNC, set a `KissTransmitLimit` in the options:

```csharp
await using var tnc = new KissTcpConnection(new KissTcpOptions
{
    Host = "localhost",
    TransmitLimit = new KissTransmitLimit(burst: 1, interval: TimeSpan.FromSeconds(30)),
});
```

`KissTcpOptions` holds every setting for `KissTcpClient` and `KissTcpConnection`: host, port, radio port, timeouts,
reconnect waits, clock and transmit limit. `new KissTcpConnection("localhost")` uses the defaults.

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

Start with the [TNC guide](https://github.com/APRS-NET/APRS.NET/blob/main/docs/guides/tnc.md).

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).
