# AprsNet.DeviceId

Names the device or program that sent an APRS packet, from the
[aprs-deviceid](https://github.com/aprsorg/aprs-deviceid) list: by its destination (TOCALL), or for Mic-E by the
device codes around its comment. The list is built into the package, so lookups need no file or network access.

```csharp
AprsDevices.Identify(packet);              // the vendor, model, class, operating system and features
AprsDevices.FromTocall("APDW17");          // WB2OSZ DireWolf
Console.WriteLine($"{AprsDevices.ListCommit} {AprsDevices.ListDate}");
```

## A newer list

The built-in list is as new as the package. To identify devices added since, an app can load the list the APRS
Foundation publishes as an `AprsDeviceList`, which has the same `Identify` and `FromTocall`:

```csharp
var source = new AprsDeviceListSource(httpClient); // or a mirror: new AprsDeviceListSource(httpClient, url)
var fetch = await source.FetchAsync(savedETag, cancellationToken); // asks only for a copy newer than savedETag's
if (fetch.List is { } list && list.IsNewerThan(current))
{
    await File.WriteAllBytesAsync(path, fetch.Content.ToArray(), cancellationToken); // keep the file as published
    (current, savedETag) = (list, fetch.ETag);
}

// Next time, start from the saved copy:
var saved = AprsDeviceList.TryParse(File.OpenRead(path), out var loaded, out _) ? loaded : AprsDeviceList.BuiltIn;
```

- `FetchAsync` never throws for a failure: `Outcome` is `Fetched`, `Unchanged` (a 304), `Unreachable`,
  `UnexpectedAnswer` or `Unreadable`, with the reason in `Error`.
- A file is read under the same rules as the built-in table. One this version can't read — a new device class, a
  TOCALL pattern it can't match, no TOCALLs — is refused with the reason, so the app keeps the list it has.
- `IsNewerThan` compares `GeneratedAt`, the file's generation time. `AprsDeviceList.BuiltIn` is the built-in list,
  with its `Commit`.
- How often to fetch, where to keep the file and which list to use stay with the app.

**Licence:** the code is MIT; the built-in device table is adapted from aprs-deviceid and is CC BY-SA 2.0. See
[LICENSE.md](https://github.com/APRS-NET/APRS.NET/blob/main/src/AprsNet.DeviceId/LICENSE.md).

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).

<sub>Developed with AI assistance; see the [AI declaration](https://github.com/APRS-NET/APRS.NET/blob/main/AI-DECLARATION.md).</sub>
