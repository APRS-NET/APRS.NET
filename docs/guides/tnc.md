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

- **Two ways to read it.** In its APRS mode the radio prints each packet as a line of text. Set
  `KissSerialOptions.TextMode` (`serial:/dev/ttyUSB0,text` on the command line) to read those lines: receive only,
  with no setup, and the radio keeps working as an APRS station of its own (display, beacons, message acks). To send,
  use KISS mode instead.
- **KISS has to be switched on.** A KISS reader can't use the text lines of APRS mode; `aprscli` warns "the TNC is sending text, not KISS frames; is it in KISS mode?". Switch the radio's TNC to
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
- **Its GPS on the same port, in APRS mode only.** With GPS PC output switched on, NMEA sentences arrive between the
  packets, and the radio may cut a line short where the two meet. Text mode passes them on (below). In packet mode
  with KISS on, a TH-D72A sends no GPS at all (firmware up to 1.05), so its GPS can't be read alongside KISS: use
  another GPS, such as gpsd.

In text mode, each whole GPS sentence whose checksum matches comes through `NmeaSentenceReceived`; one cut short is
dropped and shows in `Traffic` as `DamagedNmea`. `GpsPresenceChanged` says when sentences start, and when none has come
for `GpsTimeout` (10 seconds), so an app can show the radio's GPS while there is one. A sentence means a GPS is there,
not that it has a fix: before it has one, a TH-D72A sends sentences that say so, some with a placeholder position.
Feed them to an `NmeaFixTracker` and use its fix.

<!-- snippet: tnc-text-gps -->
```csharp
// A TH-D72A in APRS mode with GPS PC output on: its packets, and its GPS from the same port.
await using var radio = new KissSerialConnection(
    new KissSerialOptions { PortName = "/dev/ttyUSB0", TextMode = true }
);
var gps = new NmeaFixTracker();
radio.GpsPresenceChanged += (_, e) => Console.Error.WriteLine(e.IsPresent ? "GPS found" : "GPS gone");
radio.NmeaSentenceReceived += (_, e) =>
{
    // A GPS without a fix still sends sentences: only the tracker's fix says where the radio is.
    if (gps.Update(e.Sentence) && gps.Fix.Position is { } position)
    {
        Console.WriteLine($"The radio is at {position}");
    }
};

await foreach (var packet in radio.ReadPacketsAsync(cancellationToken))
{
    Console.WriteLine(packet);
}
```

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

From the command line, add the setup to the port, `--tnc serial:/dev/ttyUSB0,setup=th-d72`, and every `tnc` command and
`serve` use it. `aprscli tnc kiss on` and `off` switch the radio once, and `aprscli tnc terminal` is a console on its
command line; see the [CLI README](../../apps/AprsNet.AprsIsCli/README.md#tnc-kiss-and-tnc-terminal).

## A TNC behind AGWPE

Some sound-card modems speak AGWPE rather than KISS. UZ7HO SoundModem and AGWPE itself are common on Windows, and Dire
Wolf offers AGWPE on port 8000 beside KISS on 8001. Prefer KISS when the program offers it. Choose AGWPE when it's the
only way in, or when the program lists several radio ports by name and you want to pick one from that list.

`AgwpeConnection` works like `KissTcpConnection`: the same reconnects, states, radio ports and transmit limit. It also
asks the server which radio ports it has:

<!-- snippet: tnc-agwpe -->
```csharp
// SoundModem, or Dire Wolf's AGW port (8000), on this computer.
await using var tnc = new AgwpeConnection("localhost");
tnc.PortsReceived += (_, e) =>
{
    foreach (var port in e.Ports)
    {
        Console.Error.WriteLine($"radio port {port.Number}: {port.Description}");
    }
};

await foreach (var packet in tnc.ReadPacketsAsync(cancellationToken))
{
    Console.WriteLine(packet);
}
```

Ports are numbered from 0, so SoundModem's or Dire Wolf's `Port1` is radio port 0. In `aprscli`, give the TNC as
`agw:host[:port]`: `aprscli tnc listen --tnc agw:localhost`.

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

### Share a hardware TNC over the network

`aprscli tnc share` does the same from the command line, for a TNC on a serial port, so apps on other computers can use
it as they would Dire Wolf. It sends each app's frames to the TNC whole and one at a time, paced to the air rate. A TNC
with little buffering, such as the TNC-X, does better with that than with a byte-for-byte bridge like ser2net. See the
[CLI README](../../apps/AprsNet.AprsIsCli/README.md#tnc-share) for the options.

On a Raspberry Pi or a small Linux PC with the TNC plugged in, install .NET 10 and the tool, then check it by hand:

```sh
dotnet tool install --global AprsNet.AprsIsCli --prerelease
ls /dev/serial/by-id/                         # the TNC's stable name
aprscli tnc share --tnc serial:/dev/serial/by-id/usb-FTDI_TNC-X-if00-port0@1200 --listen 0.0.0.0:8001
```

From another computer, `aprscli tnc listen --tnc pi.local:8001` should print what the TNC hears.

To keep it running, install it as a service with [apps/services](../../apps/services/README.md). That installs
`aprscli` for a service user, the `aprsnet-tnc-share@` service, which reads its settings from a TOML file, and scripts
to check it and read its logs:

```sh
sudo apps/services/install.sh
sudo cp /etc/aprsnet/examples/tncx.toml /etc/aprsnet/tnc-share/tncx.toml   # set the TNC's path and baud rate
sudo aprsnet-check --config-only
sudo systemctl enable --now aprsnet-tnc-share@tncx
aprsnet-logs -f tncx                     # apps connecting, frames heard and sent
```

KISS over TCP has no login: anyone who can reach port 8001 can transmit through the TNC. Allow only your own network
in the firewall, or listen on `127.0.0.1` and reach it through an SSH tunnel.

## Digipeat

A `Digipeater` decides which frames to repeat and rewrites their path under the New-N paradigm, as Dire Wolf does. By
default it is a fill-in digipeater: it repeats `WIDE1-1`, its own call and its aliases, and leaves `WIDEn-N` to the
wide-area digipeaters. Each frame it doesn't repeat has a reason (`DigipeatRefusal`), and a copy of a frame it repeated
in the last 30 seconds isn't repeated again:

<!-- snippet: tnc-digi -->
```csharp
await using var tnc = new KissTcpConnection("localhost");
var digi = new Digipeater(new Ax25Address("N0CALL", 1, flag: false)); // fill-in: WIDE1-1 only

await foreach (var frame in tnc.ReadFramesAsync(cancellationToken))
{
    if (digi.Digipeat(frame).Frame is { } repeat)
    {
        await tnc.SendFrameAsync(repeat, cancellationToken); // N0CALL-9>APRS,N0CALL-1*,WIDE2-1:...
    }
}
```

`aprscli tnc digi` runs one from the command line, printing each frame repeated or refused and why:

```sh
aprscli tnc digi --tnc localhost --call N0CALL-1
```

A digipeater transmits on its own, so:

- **Licence:** you need one, and the digipeater's call must be yours.
- **Fill-in first:** run a fill-in digipeater where your area's wide-area digipeaters don't reach, such as a valley or a
  home station at the edge of coverage. Use `--wide` (`FillInOnly = false`) only by agreement with the people who run
  the local APRS network: a wide-area digipeater in the wrong place doubles traffic on a shared channel.
- **Hops:** a wide-area digipeater traps paths asking for more than 2 hops (`--max-hops`, `MaxHops`), as APRS 1.1
  recommends for most areas; more hops multiply every packet across the network.
- **Transmit limit:** `aprscli tnc digi` sends at most 3 repeats at once, then one each 10 seconds, and drops repeats
  that would wait longer: a repeat sent late only adds to a busy channel.
- **Testing:** test against a Dire Wolf with `ADEVICE null null`, which reaches no radio, before putting a digipeater on
  the air.

## Next

- [Run a gateway](gateway.md) that serves what the TNC hears to APRS-IS clients, and gates it to APRS-IS as an IGate.
- For a two-port TNC, or Dire Wolf with two channels, set `AllPorts` to read and send on every port on one connection;
  see "A multi-port TNC" in the [AprsNet.Kiss README](../../src/AprsNet.Kiss/README.md#a-multi-port-tnc).
- For SoundModem or another AGWPE server, see [A TNC behind AGWPE](#a-tnc-behind-agwpe) above.
- The [AprsNet.Kiss README](../../src/AprsNet.Kiss/README.md) covers the metrics and the KISS framing types.
