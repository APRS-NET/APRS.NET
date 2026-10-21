# IGate validation

How `aprscli serve --igate` behaves as a real receive-only IGate, checked before AprsMap builds its IGate on
`AprsNet.IGate` (F3.4). The unit and corpus tests cover the gating rules against aprs-is.net's; this records the
checks against a real radio and the live APRS-IS network, on 20 and 21 October 2026 (UTC).

- **IGate:** `aprscli serve -c NZ0P-6 --tnc serial:/dev/ttyUSB0,setup=th-d72 --igate --lat … --lon …`, logged in
  with NZ0P's passcode. The position, the maintainer's, is left out here.
- **Radio:** a Kenwood TH-D72A on 144.390 MHz, its built-in TNC switched into KISS by the `th-d72` setup.
- **Watching:** a second, read-only APRS-IS login with the filter `b/NZ0P-6 e/NZ0P-6`: the IGate's own beacons, and
  packets that entered APRS-IS through it.

The IGate is receive-only: nothing was transmitted on RF.

## Runs

| Run | Settings | IGate's summary | Seen on APRS-IS |
| --- | --- | --- | --- |
| 1 | 30 minutes, beacon every 30 minutes (23:56–00:26 UTC) | Login verified on T2PANAMA; gated 91 packets, 91 sent, 0 dropped, refused none; 1 beacon | Three gated packets with `qAO,NZ0P-6` (below). The listener started after the beacon |
| 2 | 210 seconds, beacon every minute (00:29–00:32 UTC) | Login verified on T2PANAMA; gated 9 packets, 9 sent, 0 dropped, refused none; 4 beacons | All four beacons, a minute apart |

Gated packets, as other stations received them from APRS-IS (comments shortened):

```
K0HQ-1>APN383,qAO,NZ0P-6:!3859.73N/09441.45WhPHG7330 W2, MOn-N …
CBCKC>APN383,K0HQ-1*,WIDE2-1,qAO,NZ0P-6:!3903.22N/09435.19WhPHG7330 W2, MOn-N …
K0HQ-1>APN383,WIDE2-2,qAO,NZ0P-6:!3859.73N/09441.45WhPHG7330 W2, MOn-N …
```

Each keeps the path as heard on RF, including the digipeater marked used (`K0HQ-1*`), with `qAO` and the IGate's
call after it, as aprs-is.net asks of a receive-only IGate. The two K0HQ-1 packets came more than 30 seconds apart
by different paths, so both were gated.

The IGate's beacon, as APRS-IS carried it (position left out):

```
NZ0P-6>APZNET,TCPIP*,qAC,T2PANAMA:!DDMM.mmNR0DDMM.mmW&RX-only IGate
```

The `R` overlay on the alternate-table `&` marks a receive-only IGate, and the server added `TCPIP*,qAC` for a
packet from a verified login.

## Why so few gated packets showed

Only 3 of the 100 packets gated across both runs entered APRS-IS through NZ0P-6. APRS-IS keeps the first copy of a
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

A verified login, gated packets on APRS-IS with `qAO` and the RF path unchanged, and the R& beacon at its interval:
`aprscli serve --igate` works as a receive-only IGate on the live network.
