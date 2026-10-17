# KISS over TCP validation

How `KissTcpConnection` behaves with a real Dire Wolf, checked before AprsMap uses it for its KISS sources (F3.1). The
unit tests cover framing, reconnects and the transmit limit against fakes; this records the checks against a real TNC,
in October 2026.

Two Dire Wolf instances were used:

- **Live:** the maintainer's station, KISS TCP on port 8001, with a radio on its sound card.
- **Local:** Dire Wolf on the development machine with `ADEVICE null null`, so nothing it sends goes on the air. All
  the tests that stop, block or cut the TNC used this one.

The checks used a small program that logs each `StateChanged` event, every packet, any parse issues and the
`AprsNet.Kiss` metrics. It set `ReconnectDelay` to 2 s and `MaxReconnectDelay` to 16 s, to keep the runs short.

## Receiving

| Check | TNC | Seen | Result |
| --- | --- | --- | --- |
| 20-minute soak | Live | 69 packets, no parse issues, no reconnects, no frames dropped | Passed |
| Two clients at once | Live | Both received every packet; Dire Wolf serves each KISS client separately | Passed |
| Dire Wolf stopped and restarted | Local | `Reconnecting` with waits of 2, 4, 8 and 16 s, then `Connected` once Dire Wolf was back, reading on | Passed |
| Connect attempts that get no answer (SYN dropped) | Local | Each attempt timed out after 10 s, then the wait doubled as above | Passed |
| `aprscli serve --tnc 10.10.20.144 --http` | Live | `/health` listed both sources; a packet heard on RF and APRS-IS was served once; the `-f` range filter left out a station about 90 km away | Passed |

## A TNC that disappears

The first run found a bug: when the link died without either end closing it, for example when the TNC lost power or the
network between them went down, the connection kept reporting `Connected` forever. TCP sends nothing on an idle link,
so nothing failed.

The check simulated it on the local TNC by dropping all packets on an established connection, in a user network
namespace (`unshare -rn` and `nft`), so no root was needed.

| Run | Seen |
| --- | --- |
| Before the fix | `Connected` for the whole run after the packets were dropped |
| After the fix (#184) | The dead link was noticed about 51 s after the drop (61 s after the last data), and the connection went to `Reconnecting` with an `IOException` |
| Quiet but healthy link, after the fix | `Connected` throughout a 150 s run with no packets |

#184 added `KissTcpOptions.DeadLinkTimeout` (60 s by default), which turns on TCP keepalives: probes start after half
the timeout with no data, and three unanswered probes close the link. It should also catch a laptop that slept and
woke on a different network, though that wasn't tested.

## Transmitting

| Check | TNC | Seen | Result |
| --- | --- | --- | --- |
| `aprscli tnc send` (status, no path) | Local | Dire Wolf logged `[0L] NZ0P-9>APZNET:>null audio test`, its transmit log, even though the client closed straight after sending | Passed |
| `aprscli tnc send` (status, `WIDE1-1`) | Live | The maintainer ran it as NZ0P-9; Dire Wolf logged the frame and keyed the radio | Passed |

The live command:

```sh
aprscli tnc send -c NZ0P-9 -t 10.10.20.144 --path WIDE1-1 status "APRS.NET KISS transmit test"
```

Two earlier sends without a path weren't confirmed either way: the Dire Wolf console had been scrolled up. On RF the
callsign needs a numeric SSID from 0 to 15; `NZ0P-AM`, for example, can only be used on APRS-IS.

## Result

KISS over TCP receives, reconnects, notices a dead link and transmits through a real Dire Wolf. The one bug found, the
dead link reported as connected, was fixed in #184 and shipped in 0.3.0-alpha.3.

Not covered here: KISS TNCs on USB serial (#126, #160), which still need their hands-on check.
