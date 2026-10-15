# Device identification list

`tocalls.json` is the [aprs-deviceid](https://github.com/aprsorg/aprs-deviceid) list as the APRS Foundation publishes
it (`https://aprs-deviceid.aprsfoundation.org/tocalls.pretty.json`), normalized: keys sorted, so diffs show only real
changes, and contact addresses left out. It is CC BY-SA 2.0, like the table generated from it (see
[the package licence](../../src/AprsNet.DeviceId/LICENSE.md)). `tocalls.version` holds the list's commit (the last one
to change `tocalls.yaml`) and the published file's generation time.

`Generate.cs` turns it into `src/AprsNet.DeviceId/AprsDevices.Generated.cs`, and CI fails if the generated file is out
of date.

## Updates

The `deviceid` workflow runs nightly. It fetches the published list, checks that it matches the commit that produced
it, and, when anything changed, opens or updates a pull request from `automation/deviceid` listing each TOCALL or
Mic-E code added, removed or changed. To do the same by hand:

```sh
dotnet run tools/deviceid/Generate.cs -- --update summary.md
```

A new device class in the list stops the generator: add it to `AprsDeviceClass` and to the tool's class table.
