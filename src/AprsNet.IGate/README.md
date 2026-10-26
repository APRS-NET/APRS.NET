# AprsNet.IGate

The rules of an APRS IGate (internet gateway). `RfToInternetGate` decides which packets heard on RF go to APRS-IS,
following the aprs-is.net [IGate rules](https://www.aprs-is.net/IGateDetails.aspx), and builds the line to send. It
does no I/O: pass it what a TNC hears, and send what it gates with a verified APRS-IS login.

```csharp
using AprsNet.IGate;
using AprsNet.Packets;

var gate = new RfToInternetGate(Callsign.Parse("N0CALL-10")); // receive-only: adds qAO

await foreach (var frame in tnc.ReadFramesAsync(cancellationToken))
{
    var decision = gate.Gate(frame);
    if (decision.IsGated)
    {
        await aprsIs.SendAsync(decision.Packet, cancellationToken); // N0CALL-9>APRS,WIDE1-1,qAO,N0CALL-10:...
    }
    else
    {
        logger.LogDebug("Not gated ({Reason}): {Packet}", decision.Refusal, decision.Heard);
    }
}
```

Every packet is gated except:

| `GateRefusal` | What |
| --- | --- |
| `NotAprs` | Not a UI frame with no layer 3 protocol |
| `FromInternet` | `TCPIP` or `TCPXX` in the path, or in a third-party packet's inner header: it came from APRS-IS |
| `NoGate` | `NOGATE` or `RFONLY` in the path: the sender asked IGates not to gate it |
| `GeneralQuery` | `?APRS?`, `?IGATE?`, `?WX?` and other `?` queries, meant for stations in radio range |
| `AlreadyGated` | A q-construct already in the path |
| `Unreadable` | A header that can't be read, or a third-party packet whose inner packet can't |
| `Empty` | No information field, or nothing before its first CR, LF or NUL |
| `TooLong` | Longer than the 510 bytes of an APRS-IS line once the q-construct is added |
| `Duplicate` | A copy of a packet gated within the last 30 seconds (`DuplicateWindow`), as when digipeaters repeat it |

The path is sent as heard, with `,qAO,` and the IGate's callsign after it. Third-party traffic from another RF
network goes up as the packet it carries, without the RF header. The information field is cut at its first CR, LF or
NUL, which an APRS-IS line can't hold; many TNCs end packets with a CR. A packet whose payload has errors is still
gated: APRS-IS carries what stations send.

A receive-only IGate adds `qAO`, which tells APRS-IS it can't pass messages back to RF. Receive-only IGates are
discouraged on the shared APRS frequency: nearby stations take a message gated from them as a sign they can be reached
through the IGate, and replies never come back on RF. Set `ReceiveOnly = false` (adding `qAR`) only for an IGate that
also gates messages to RF.

Every decision is counted on the `AprsNet.IGate` meter: `aprsnet.igate.packets.gated` and
`aprsnet.igate.packets.refused` by `reason` (see [docs/metrics.md](../../docs/metrics.md#igate)).

## From APRS-IS to RF

A two-way IGate also carries messages from APRS-IS to stations heard on RF, so it doesn't break messaging the way a
receive-only one does. `InternetToRfGate` decides which, using the `HeardStations` below, and builds the third-party
packet to transmit:

```csharp
var heard = new HeardStations();                       // record every frame the TNC hears in it
var igate = PacketAddress.Parse("N0CALL-10", "APZNET", ["WIDE1-1", "WIDE2-1"]);
var toRf = new InternetToRfGate(igate, heard);

await foreach (var packet in aprsIs.ReadPacketsAsync(cancellationToken))
{
    var decision = toRf.Gate(packet);
    if (decision.IsGated)
    {
        await tnc.SendAsync(decision.Packet, cancellationToken); // N0CALL-10>APZNET,WIDE1-1,WIDE2-1:}W1AW>APRS,TCPIP,N0CALL-10*::N0CALL-9 :Hi
    }
}
```

Only messages, acknowledgements and rejections go out, and only when the addressee is local (heard on RF within 180
minutes over at most 2 hops) and the sender isn't. After a message, the sender's next position on APRS-IS within 30
minutes goes out once, so the addressee can see who is writing. Everything else is refused with an `RfGateRefusal`:

| `RfGateRefusal` | What |
| --- | --- |
| `NotForAStation` | Not a message for a station (bulletins, announcements, queries, positions and the rest of the feed) |
| `NotLocal` | The addressee hasn't been heard on RF recently over few enough hops |
| `SenderOnRf` | The sender is local on RF itself, so the addressee hears it directly |
| `NoGate` | `TCPXX`, `NOGATE` or `RFONLY` in the path |
| `FromRadio` | Gated from RF to APRS-IS (`qAR`, `qAO`): it is on RF already |
| `Duplicate` | A copy of one gated within 30 seconds, such as from a second APRS-IS server |
| `RateLimited` | Over 6 packets a minute or 10 in five minutes (`MaxPerMinute`, `MaxPerFiveMinutes`), as Dire Wolf's `IGTXLIMIT` |
| `Unreadable` | A header that can't be read |

The IGate transmits on its own: run one only with a licence, and match `HeardStationsOptions.MaxLocalHops` to the hops
of its path.

## Stations heard on RF

`HeardStations` keeps the stations a TNC has heard: when each was last heard, over how many digipeater hops and on
which radio port. An IGate that gates messages to RF uses it to send them only to stations that are local, heard within
`LocalWindow` (180 minutes) over at most `MaxLocalHops` (2), as Dire Wolf decides. Set `MaxLocalHops` to the hops of
the path the IGate transmits with.

```csharp
var heard = new HeardStations();

await foreach (var frame in tnc.ReadFramesAsync(cancellationToken))
{
    heard.Record(frame);
    // ...
}

if (heard.IsLocal(Callsign.Parse("N0CALL-9")))
{
    // heard on RF within 180 minutes, over 2 hops at most
}
```

The hops are `PathAnalysis.HopCount`: under the New-N paradigm, `DIGI1,WIDE1*,WIDE2-1` is one hop. A transmission heard
direct and then through digipeaters within 30 seconds keeps the shorter path. Packets with a q-construct, or `TCPIP` or
`TCPXX` in the path, came from APRS-IS and are ignored. Third-party traffic records the station that relayed it on RF.
At most `Capacity` stations (10,000) are kept; when full, the one heard longest ago makes room.
