# AprsNet.Kiss.Serial

A lasting connection to a KISS TNC on a serial port, such as a USB TNC (the MFJ TNC-X or the NinoTNC) or a Bluetooth
serial link. It opens the port when a read starts and opens it again when it fails or the TNC is unplugged and plugged
back in, so the reads only pause. It adds `System.IO.Ports` to [`AprsNet.Kiss`](https://www.nuget.org/packages/AprsNet.Kiss),
which stays free of it.

```csharp
using AprsNet.Kiss;

foreach (var name in KissSerialConnection.GetPortNames())    // for a settings picker
    Console.WriteLine(name);                                  // /dev/ttyUSB0, COM3, ...

await using var tnc = new KissSerialConnection("/dev/ttyUSB0");  // 9600 baud; pass 57600 for a NinoTNC
tnc.StateChanged += (_, e) =>
{
    if (e.State == KissConnectionState.Reconnecting)
        Console.WriteLine($"TNC lost ({e.Error?.Message ?? "closed"}); retrying in {e.RetryIn}");
};

await foreach (var packet in tnc.ReadPacketsAsync(cancellationToken))
    Console.WriteLine(packet);
```

`KissSerialConnection` is a `KissConnection`, like `KissTcpConnection`: the same `State`, `StateChanged`, reconnects,
transmit limit, metrics (tagged with the port name) and `SendAsync`. `KissSerialOptions` holds its settings: port, baud
rate, radio port, reconnect waits (2 seconds, doubling to 30), clock and transmit limit. The port is opened at 8 data
bits, no parity, one stop bit and no handshaking.

DTR and RTS are raised while the port is open, as a terminal program leaves them; some radios' built-in TNCs, such as
the Kenwood TH-D72A, send nothing while RTS is low. A board that uses those lines to reset, such as an ESP32 TNC, needs
them left low: set `DtrEnable` and `RtsEnable` to `false`.

A USB TNC can come back under another name (`/dev/ttyUSB1` instead of `/dev/ttyUSB0`) when it is plugged back in. On
Linux, name it by its stable link under `/dev/serial/by-id/` instead. Linux reports a port that is missing as access
denied; both are retried.

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).
