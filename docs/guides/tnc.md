# Getting started with a TNC

This guide connects an app to a TNC with `AprsNet.Kiss`: first [Dire Wolf](https://github.com/wb2osz/direwolf), the
software TNC, over KISS TCP; then a hardware TNC on a serial port. You need a radio and a sound card interface for Dire
Wolf, or a TNC, and a licence to transmit.

```sh
dotnet add package AprsNet.Kiss --prerelease
```

## Set up Dire Wolf

Dire Wolf serves KISS over TCP on port 8001 by default. A minimal `direwolf.conf`:

```text
ADEVICE plughw:1,0
CHANNEL 0
MYCALL N0CALL-10
MODEM 1200
KISSPORT 8001
```

Start it with `direwolf -c direwolf.conf`. To check it hears packets before writing any code, run
`aprscli tnc listen --tnc localhost --format decoded`.

## Read what the TNC hears

`KissTcpConnection` connects when you start reading and reconnects by itself, waiting 5 seconds and doubling to a
minute, so a restarted Dire Wolf only pauses the stream. `StateChanged` tells you when it is lost and back:

<!-- snippet: tnc-read -->
```csharp
// Dire Wolf on this computer, on its KISS TCP port (8001).
await using var tnc = new KissTcpConnection("localhost");
tnc.StateChanged += (_, e) =>
    Console.Error.WriteLine(
        e.State == KissConnectionState.Reconnecting
            ? $"TNC lost ({e.Error?.Message ?? "closed"}); trying again in {e.RetryIn}"
            : $"TNC {e.State}"
    );

await foreach (var packet in tnc.ReadPacketsAsync(cancellationToken))
{
    Console.WriteLine($"{packet.Source}: {packet.Type} {packet}");
}
```

`ReadPacketsAsync` gives the APRS packets, parsed; `ReadFramesAsync` gives every AX.25 frame, APRS or not.

## Send packets

Check what you send before you send it: `aprscli tnc send status "Testing" -c N0CALL-9 --dry-run` prints the packet
without transmitting. In code, build the packet from your address and send it while the connection is up:

<!-- snippet: tnc-send -->
```csharp
await using var tnc = new KissTcpConnection("localhost");

// Sending needs a live connection, and reading is what connects: read in the background.
var connected = new TaskCompletionSource();
tnc.StateChanged += (_, e) =>
{
    if (e.State == KissConnectionState.Connected)
    {
        connected.TrySetResult();
    }
};
var reading = Task.Run(
    async () =>
    {
        await foreach (var heard in tnc.ReadPacketsAsync(cancellationToken))
        {
            Console.WriteLine(heard);
        }
    },
    cancellationToken
);
await connected.Task.WaitAsync(cancellationToken);

// Your callsign, APRS.NET's TOCALL and the usual two-hop path.
var me = PacketAddress.Parse("N0CALL-9", "APZNET", ["WIDE1-1", "WIDE2-1"]);
await tnc.SendAsync(me.Create(new StatusReport { Text = "On the air with APRS.NET" }), cancellationToken);
```

Sends are rate limited so a bug cannot flood the channel: 3 frames at once, then one every 10 seconds. A send waits for
its turn. `KissTcpOptions` holds the limit and every other setting:

<!-- snippet: tnc-limit -->
```csharp
await using var tnc = new KissTcpConnection(
    new KissTcpOptions
    {
        Host = "192.168.1.20",
        Port = 8001,
        TncPort = 1, // the second radio on a two-port TNC
        TransmitLimit = new KissTransmitLimit(burst: 1, interval: TimeSpan.FromSeconds(30)),
    }
);
```

## A hardware TNC on a serial port

Put the TNC in KISS mode and use `KissSerialConnection` from `AprsNet.Kiss.Serial`
(`dotnet add package AprsNet.Kiss.Serial --prerelease`). It is a `KissConnection`, like `KissTcpConnection`: it opens
the port when you start reading and opens it again, waiting 2 seconds and doubling to 30, if the port fails or the TNC
is unplugged and plugged back in. `KissSerialConnection.GetPortNames()` lists the ports present. Most USB TNCs run at
9600 baud; the NinoTNC uses 57600.

On Linux, a USB TNC may come back as `/dev/ttyUSB1` after a replug; use its stable name under `/dev/serial/by-id/`.
To manage the port yourself, or for a Bluetooth link, hand its stream to `KissStreamClient`, which reads and sends the
same way over any stream but doesn't reconnect.

<!-- snippet: tnc-serial -->
```csharp
// A hardware TNC in KISS mode on a USB serial port (AprsNet.Kiss.Serial).
await using var tnc = new KissSerialConnection("/dev/ttyUSB0", 9600);

await foreach (var packet in tnc.ReadPacketsAsync(cancellationToken))
{
    Console.WriteLine(packet);
}
```

From the command line, give the port as `serial:PORT[@BAUD]` wherever a TNC goes:

```sh
aprscli tnc ports                                        # the serial ports present
aprscli tnc listen -t serial:/dev/ttyUSB0 --reconnect    # 9600 baud; serial:/dev/ttyACM0@57600 for a NinoTNC
aprscli serve -t serial:/dev/ttyUSB0 --http 127.0.0.1:14590
```

When the TNC is unplugged, `tnc listen --reconnect` and `serve` say so ("lost the TNC at /dev/ttyUSB0: … probably
unplugged; reconnecting in 2s"), say "is not present; is the TNC plugged in?" while it is away, and "reconnected" once
it is back. Without `--reconnect`, `tnc listen` ends with exit code 2. A program has the port to itself while it reads;
another one opening it gets "Device or resource busy".

### A radio's built-in TNC

Some handhelds and mobiles, such as the Kenwood TH-D72A, have a TNC behind a USB serial port. A few things set them
apart from a stand-alone TNC:

- **KISS has to be switched on.** In its APRS mode the radio prints each packet as a line of text, which a KISS reader
  can't use; `aprscli` warns "the TNC is sending text, not KISS frames; is it in KISS mode?". Switch the radio's TNC to
  its packet mode (PACKET12 on the TH-D72A); the connection can then switch it to KISS itself (below).
- **KISS doesn't survive a power cut.** Switched off and on again, the radio comes back in packet mode with KISS off,
  sending text. With a setup, the connection notices and switches it back.
- **Switching the radio off may not close the port.** The USB chip can be powered from the computer, so the port stays
  open and nothing fails; only silence shows it. `--quiet-warning 10m` (or `QuietWarning`) reports a TNC that has sent
  nothing for that long. Pick a time longer than your channel is ever quiet.
- **DTR and RTS.** Some radios send nothing while RTS is low; the serial connection raises DTR and RTS when it opens the
  port, as a terminal program does. A board that resets on those lines, such as an ESP32 TNC, needs them left low:
  set `DtrEnable` and `RtsEnable` to `false`, or end the address in `,dtr=off,rts=off`
  (`serial:/dev/ttyUSB0@115200,dtr=off,rts=off`).
- **Other output on the same port.** With GPS output switched on, NMEA sentences arrive between the packets, and the
  radio may cut a line short where the two meet.

Set `KissSerialOptions.Setup` to the radio's `TncSetup` and the connection switches it into KISS each time it opens the
port, including after a reconnect; opens the port again when the radio starts sending text, as after a power cut; and
switches it back out of KISS when it closes. The scripts only talk to the TNC; nothing goes on the air.

<!-- snippet: tnc-setup -->
```csharp
// A Kenwood TH-D72A with its TNC in packet mode (PACKET12): switched into KISS whenever the port opens.
await using var tnc = new KissSerialConnection(
    new KissSerialOptions { PortName = "/dev/ttyUSB0", Setup = TncSetup.KenwoodThD72 }
);
```

`TncSetup.Known` lists the built-in setups for a settings picker. For another TNC, write the steps as a `TncScript`, in
code or in its text form:

```text
# Leave KISS if it was on, throw away what waited in the port, then turn KISS on at the cmd: prompt.
bytes C0 FF C0
drain 500ms
send
expect 3s cmd:
send KISS ON
expect 3s cmd:
send RESTART
pause 1s
```

`drain` matters at a command line: a TNC at its prompt prints every packet it hears, and the port keeps that, old
prompts included, until someone reads it. Without it, an `expect` can match a prompt left over from an earlier
session. `new TncSetup("my-tnc", "My TNC", TncScript.Parse(enter), TncScript.Parse(leave))` makes it a setup. To work at a TNC's
command line by hand, `KissSerialConnection.OpenTerminal` opens the port as a `TncTerminal`: send it commands with
`SendLineAsync` and read what it says with `ReadAsync`.

`tools/d72.sh` in this repository switches a TH-D72A into and out of KISS mode and shows what it is sending.

## Share one TNC between apps

Dire Wolf already lets several apps connect. For a TNC that takes one connection, or to give every app one transmit
limit, put a `KissTcpServer` in front of it; apps connect to port 8101 as if it were the TNC:

<!-- snippet: tnc-share -->
```csharp
await using var tnc = new KissTcpConnection("localhost");
await using var server = new KissTcpServer(new IPEndPoint(IPAddress.Loopback, 8101))
{
    Transmit = tnc.SendFrameAsync, // every app's frames share the TNC's transmit limit
};
server.Start();

await foreach (var frame in tnc.ReadFramesAsync(cancellationToken))
{
    server.Publish(frame);
}
```

## Next

- [Run a gateway](gateway.md) that serves what the TNC hears to APRS-IS clients.
- The [AprsNet.Kiss README](../../src/AprsNet.Kiss/README.md) covers the metrics and the KISS framing types.
