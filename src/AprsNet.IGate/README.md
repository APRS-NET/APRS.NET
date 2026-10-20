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
