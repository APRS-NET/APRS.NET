# IGate validation

How `aprscli serve --igate` behaves as a real receive-only IGate, checked before AprsMap builds its IGate on
`AprsNet.IGate` (F3.4). The unit and corpus tests cover the gating rules against aprs-is.net's; this records the
checks against real radios and the live APRS-IS network, on 20 and 21 October 2026 (UTC).

- **IGate:** `aprscli serve -c NZ0P-6 --tnc serial:/dev/ttyUSB0,setup=th-d72 --igate --lat … --lon …`, logged in
  with NZ0P's passcode. The position, the maintainer's, is left out here.
- **Radio:** runs 1 and 2, a Kenwood TH-D72A on 144.390 MHz, its built-in TNC switched into KISS by the `th-d72`
  setup. Run 3, Dire Wolf on another computer, read over KISS-over-TCP (`--tnc <host>:8001`).
- **Watching:** a second, read-only APRS-IS login with the filter `b/NZ0P-6 e/NZ0P-6`: the IGate's own beacons, and
  packets that entered APRS-IS through it. Run 3 added `b/NZ0P-4`, a test station.

The IGate is receive-only: it transmitted nothing on RF. In run 3 the maintainer sent test packets as NZ0P-4 from
a TNC-X on a separate radio.

## Runs

| Run | Settings | IGate's summary | Seen on APRS-IS |
| --- | --- | --- | --- |
| 1 | 30 minutes, beacon every 30 minutes (23:56–00:26 UTC) | Login verified on T2PANAMA; gated 91 packets, 91 sent, 0 dropped, refused none; 1 beacon | Three gated packets with `qAO,NZ0P-6` (below). The listener started after the beacon |
| 2 | 210 seconds, beacon every minute (00:29–00:32 UTC) | Login verified on T2PANAMA; gated 9 packets, 9 sent, 0 dropped, refused none; 4 beacons | All four beacons, a minute apart |
| 3 | 30 minutes, Dire Wolf over TCP (03:03–03:33 UTC) | Login verified on T2FINLAND; gated 19 packets, 19 sent, 0 dropped; refused 1 from the internet, 1 duplicate; 1 beacon | The beacon, and one of NZ0P-4's six test packets with `qAO,NZ0P-6` (below) |

Gated packets, as other stations received them from APRS-IS (comments shortened):

```
K0HQ-1>APN383,qAO,NZ0P-6:!3859.73N/09441.45WhPHG7330 W2, MOn-N …
CBCKC>APN383,K0HQ-1*,WIDE2-1,qAO,NZ0P-6:!3903.22N/09435.19WhPHG7330 W2, MOn-N …
K0HQ-1>APN383,WIDE2-2,qAO,NZ0P-6:!3859.73N/09441.45WhPHG7330 W2, MOn-N …
```

Each keeps the path as heard on RF, including the digipeater marked used (`K0HQ-1*`), with `qAO` and the IGate's
call after it, as aprs-is.net asks of a receive-only IGate. The two K0HQ-1 packets came more than 30 seconds apart
by different paths, so both were gated.

In run 3, NZ0P-4's position report, heard direct by Dire Wolf (no digipeater marked used) and gated over KISS-over-TCP
(position left out):

```
NZ0P-4>APZ001,WIDE1-1,WIDE2-1,qAO,NZ0P-6:!DDMM.mmN/DDDMM.mmW-TNC-X message test
```

Its other five packets reached APRS-IS first through other IGates (`qAR` via SEKANS and BASHOR, `qAS` via KA0GFC-3),
the same duplicate check as below.

The IGate's beacon, as APRS-IS carried it (position left out):

```
NZ0P-6>APZNET,TCPIP*,qAC,T2PANAMA:!DDMM.mmNR0DDMM.mmW&RX-only IGate
```

The `R` overlay on the alternate-table `&` marks a receive-only IGate, and the server added `TCPIP*,qAC` for a
packet from a verified login.

## Why so few gated packets showed

Only 3 of the 100 packets gated in runs 1 and 2 entered APRS-IS through NZ0P-6. APRS-IS keeps the first copy of a
packet and drops the rest as duplicates, and the Kansas City area has several IGates with better antennas than a
handheld; they were first with the rest. The IGate sent every packet it gated (none dropped, none refused by the
APRS-IS client), so this is the network's duplicate check, not a loss.

## Also seen

- With no filter, the IGate's APRS-IS connection still received 99 packets in run 1 and 3 in run 2. What they were
  wasn't recorded; servers are known to send an IGate traffic for stations it has gated, for a two-way IGate to pass
  to RF (#212). The receive-only IGate only passes them to local clients.
- The local feed dropped 23 of the radio's packets as duplicates in run 1, while the gate refused none. The feed
  checks both sources together and counts a duplicate against the copy that came second; since the gate, checking
  the radio's packets alone over the same 30 seconds, found no duplicates, these were packets APRS-IS had already
  sent the IGate. They were still gated: the gate decides on what the radio heard, before the feed.
- `-d 3m30s` is refused: a duration takes one unit (`210s`).

## Result

A verified login, gated packets on APRS-IS with `qAO` and the RF path unchanged, and the R& beacon at its interval,
from a radio's TNC over serial and from Dire Wolf over TCP: `aprscli serve --igate` works as a receive-only IGate on
the live network.
