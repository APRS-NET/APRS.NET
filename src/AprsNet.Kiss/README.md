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

For a USB or serial TNC, `KissSerialConnection` in
[`AprsNet.Kiss.Serial`](https://www.nuget.org/packages/AprsNet.Kiss.Serial) opens the port and opens it again after an
unplug. To manage a port or a Bluetooth link yourself, hand its stream to `KissStreamClient`, which does the same over
any readable and writable stream (no serial-port dependency in this package):

```csharp
using var serial = new SerialPort("/dev/ttyUSB0", 9600);
serial.Open();
await using var tnc = new KissStreamClient(serial.BaseStream);
```

### Staying connected

`KissTcpConnection` (and `KissSerialConnection`; both are a `KissConnection`) reconnects by itself, and its read
streams carry on across reconnects. `State` says where it is
(`Idle`, `Connecting`, `Connected`, `Reconnecting`, `Closed`), and `StateChanged` reports each change, as
`AprsIsConnection` does; a `Reconnecting` change carries the error, if any, and the wait before the next attempt:

```csharp
tnc.StateChanged += (_, e) =>
{
    if (e.State == KissConnectionState.Reconnecting)
        Console.WriteLine($"TNC lost ({e.Error?.Message ?? "closed"}); retrying in {e.RetryIn}");
};
```

A TNC that goes away without closing the connection, because its host lost power or this computer left the network,
is noticed within `KissTcpOptions.DeadLinkTimeout` (60 seconds) through TCP keepalives. The TNC's host answers those
even while the radio channel is quiet, so a quiet channel never looks like a dead link.

A serial TNC has no such check. One behind a USB chip powered from the computer, such as a radio's built-in TNC, leaves
its port open when switched off, and only silence shows it. Set `QuietWarning` (off by default) to hear about that:
after that long with no bytes at all, `Warning` reports `KissWarning.Quiet` ("The TNC has sent nothing for 10 minutes;
is it switched on?", with the time in `QuietFor`) and `IsQuiet` is true, until bytes arrive again. Pick it longer than
the channel is ever quiet.

A TNC that isn't in KISS mode sends text lines instead of frames: a radio's built-in TNC in its APRS mode, or one that
came back from a power cut with KISS off. Rather than drop them without a word, the clients raise `Warning`
(`KissWarning.TextNotKiss`) when the text starts, and again with `IsActive` false once frames arrive. On a
`KissConnection`, `IsSendingText` says whether it is on now:

```csharp
tnc.Warning += (_, e) => Console.WriteLine(e.Message); // "The TNC is sending text, not KISS frames; is it in KISS mode?"
```

A radio's built-in TNC, or one with its own command line, has to be told to speak KISS, and forgets after a power
cut. A `TncSetup` says how: `TncScript`s to enter and leave KISS (send a command, expect the prompt, pause), such as
the built-in `TncSetup.KenwoodThD72`. `KissSerialOptions.Setup` (in `AprsNet.Kiss.Serial`) runs it each time the port
opens and again when the TNC starts sending text. `TncTerminal` is a console on a TNC's command line, for setting one up
by hand.

For a troubleshooting console, `Traffic` reports everything that crosses the link: each frame received (APRS, another
protocol, another port, a KISS command, or dropped as damaged), bytes outside any frame a line at a time, each frame
sent, and each step of a `TncSetup`. Each entry has its bytes as they crossed the link, and for a frame its port,
command and parsed packet. Nothing extra is done while no one listens, and nothing is kept: keep your own history.

```csharp
tnc.Traffic += (_, e) => console.Add($"{e.At:HH:mm:ss} {e.Kind}: {e.Packet?.ToString() ?? e.Text}");
```

A console can also type back, over the same open connection. `BeginManual()` starts manual mode: `SendLineAsync` sends
a command with a carriage return, `SendRawAsync` sends bytes as they are, and `RunScriptAsync` runs a `TncScript` whose
expect steps see the TNC's answers. All of them are written between frames. That makes switching a radio's TNC into
KISS one action, without reopening the port. While manual mode is on, a connection with a `TncSetup` doesn't reopen the
port to undo what you typed.

```csharp
using (tnc.BeginManual())
{
    await tnc.RunScriptAsync(TncSetup.KenwoodThD72.EnterKiss); // KISS ON and RESTART, on the open port
}
```

Manual mode is for setting up and troubleshooting, not for sending packets. Raw sends skip the transmit limit, raw
bytes forming a data frame go on the air, and at a TNC's command line some commands transmit, such as `ID`, `CONV` and
`K` on TNC-2-style TNCs.

The `AprsNet.Kiss` meter counts frames received, sent and dropped, bytes discarded outside frames, open connections and
reconnects by cause, each tagged with the TNC (`host:port`, or the serial port's name). It also counts a
`KissTcpServer`'s apps connected, leaving and turned away and the frames it passes each way, tagged with the address it
listens on. Add it to OpenTelemetry with `AddMeter("AprsNet.Kiss")`; [docs/metrics.md](../../docs/metrics.md#kiss) lists
the instruments.

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

`KissTcpOptions` holds every setting for `KissTcpClient` and `KissTcpConnection`: host, port, radio ports, timeouts,
reconnect waits, clock and transmit limit. `new KissTcpConnection("localhost")` uses the defaults.

`KissTransmitLimit.Off` removes the limit, for a channel the app has to itself or a test.

### A multi-port TNC

A connection reads one radio port, `TncPort` (0 by default). For a two-port serial TNC, or Dire Wolf with two channels
behind one KISS port, set `AllPorts` to read them all on one connection: `ReadPortFramesAsync` says which port each
frame was heard on, and `SendToPortAsync` sends on any port. `SendAsync` still sends on `TncPort`.

```csharp
await using var tnc = new KissTcpConnection(new KissTcpOptions { Host = "localhost", AllPorts = true });

await foreach (var (port, frame) in tnc.ReadPortFramesAsync(cancellationToken))
{
    Console.WriteLine($"port {port}: {frame}");           // port 1: N0CALL-9>APRS,WIDE1-1:...
}

await tnc.SendToPortAsync(1, packet, cancellationToken); // on the second channel
```

Each port has a transmit limit of its own with the same settings, so a busy port doesn't hold up the other. The frame
counts carry a `port` tag.

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

`ClientConnected`, `ClientDisconnected` and `ClientRefused` report apps arriving, leaving (with `FellBehind` when the
server dropped them) and being turned away over `maxClients`, with their address; `ClientsRefused` counts the last.
`aprscli tnc share` is built on it.

Start with the [TNC guide](https://github.com/APRS-NET/APRS.NET/blob/main/docs/guides/tnc.md).

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).
