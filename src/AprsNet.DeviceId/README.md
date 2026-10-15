# AprsNet.DeviceId

Names the device or program that sent an APRS packet, from the
[aprs-deviceid](https://github.com/aprsorg/aprs-deviceid) list: by its destination (TOCALL), or for Mic-E by the
device codes around its comment. The list is built into the package, so lookups need no file or network access.

```csharp
AprsDevices.Identify(packet);              // the vendor, model, class, operating system and features
AprsDevices.FromTocall("APDW17");          // WB2OSZ DireWolf
Console.WriteLine($"{AprsDevices.ListCommit} {AprsDevices.ListDate}");
```

**Licence:** the code is MIT; the built-in device table is adapted from aprs-deviceid and is CC BY-SA 2.0. See
[LICENSE.md](https://github.com/APRS-NET/APRS.NET/blob/main/src/AprsNet.DeviceId/LICENSE.md).

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).

<sub>Developed with AI assistance; see the [AI declaration](https://github.com/APRS-NET/APRS.NET/blob/main/AI-DECLARATION.md).</sub>
