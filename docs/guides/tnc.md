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
