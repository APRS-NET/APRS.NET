# Device identification list

`tocalls.yaml` is an unmodified copy of the [aprs-deviceid](https://github.com/aprsorg/aprs-deviceid) list (CC BY-SA
2.0, see [THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md)). `tocalls.version` holds the commit it was taken from
and that commit's date. `Generate.cs` turns it into `src/AprsNet.Packets/AprsDevices.Generated.cs`, and CI fails if the
generated file is out of date.

To update the list:

```sh
curl -fsSLo tools/deviceid/tocalls.yaml https://raw.githubusercontent.com/aprsorg/aprs-deviceid/main/tocalls.yaml
gh api repos/aprsorg/aprs-deviceid/commits/main --jq '.sha + " " + .commit.committer.date[:10]' > tools/deviceid/tocalls.version
dotnet run tools/deviceid/Generate.cs
```

A new device class in the list stops the generator: add it to `AprsDeviceClass` and to the tool's class table.
