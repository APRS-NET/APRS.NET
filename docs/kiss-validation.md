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

## A TNC on USB serial

On 17 October 2026, a Kenwood TH-D72A on `/dev/ttyUSB0`: its built-in TNC behind a CP2102 USB serial chip, at the
radio's default 9600 baud. `tools/d72.sh` switches it into and out of KISS mode.

| Check | Seen | Result |
| --- | --- | --- |
| The radio in APRS mode | It sends TNC-2 text lines (`SRC>DEST,PATH:info`), not KISS; a KISS reader drops them all without a word (#232) | As expected |
| PACKET mode, then `KISS ON` and `RESTART` | KISS frames (`C0 00 …`) | Passed |
| `aprscli tnc listen -t serial:/dev/ttyUSB0`, with and without `--reconnect` | Nothing at first, though `cat` showed the frames | Failed, then fixed |
| The same, after the fix | Packets heard through the local digipeaters, on both paths | Passed |
| `aprscli tnc send -c NZ0P-3 -t serial:/dev/ttyUSB0 --path WIDE1-1 status …` | The maintainer's Dire Wolf heard it directly, then a digipeated copy a second later | Passed |

The failure: `System.IO.Ports` opens a port with DTR and RTS low, and the radio sends nothing while RTS is low. `cat`
leaves both raised, which is why it saw the frames. `KissSerialConnection` and the CLI's serial link now raise both.

### Unplugging, and switching the radio off

| Check | Seen | Result |
| --- | --- | --- |
| Unplug and replug under `tnc listen --reconnect` | At first the listener crashed: on Linux .NET closes the port itself, and the next read throws `ObjectDisposedException` ("The port is closed"), which wasn't taken for a lost link. After the fix: "the TNC was probably unplugged", retries after 2, 4, 8 and 16 s, then packets again once it was back | Failed, then fixed |
| Unplug without `--reconnect` | At first an unhandled exception; after the fix one line, "lost the TNC … probably unplugged", and exit code 2 | Failed, then fixed |
| Retries while unplugged | .NET reports a port that isn't there as "access denied"; the message now says the port "is not present; is the TNC plugged in?" | Fixed |
| Unplug and replug under `aprscli serve` | The gateway kept running; `/health` went to `unhealthy` (503) within 3 s, back to `healthy` once the port reopened, and packets resumed. Retries waited 2, 4, 8 and 16 s, then 30 s | Passed |
| `tnc ports` and `tnc send` with no port | "no serial ports found"; the send fails with exit code 2 | Passed |
| Radio switched off, cable still in | The radio's USB chip runs on USB power, so `/dev/ttyUSB0` stayed and the port stayed open: `/health` said `healthy` with only `lastPacket` standing still (#234) | Gap |
| Radio switched back on | It came back in PACKET mode but with KISS off, sending text the KISS reader dropped without a word (#232). `tools/d72.sh kiss-on` brought the packets back | Gap |

While `serve` held the port, `stty` on it failed with "Device or resource busy": .NET opens a serial port for one
program at a time.

## Result

KISS over TCP receives, reconnects, notices a dead link and transmits through a real Dire Wolf. The one bug found, the
dead link reported as connected, was fixed in #184 and shipped in 0.3.0-alpha.3.

KISS over USB serial receives and transmits through a TH-D72A, and survives the radio being unplugged and plugged back
in, both under `tnc listen --reconnect` and in the gateway. Three bugs were found and fixed: DTR and RTS left low, an
unplug taken for a crash, and a missing port reported as access denied. Two gaps remain, both about a TNC that is
connected but not working: one switched off behind a powered USB chip (#234), and one sending text in KISS mode (#232).
