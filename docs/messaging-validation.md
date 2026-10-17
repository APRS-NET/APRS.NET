# Messaging validation

How `MessagingStation` behaves with a real APRS radio, checked through `aprscli send message` before AprsMap builds its
messaging on it (F2.4). The unit tests cover acks, reply-acks, retries and duplicates against the spec; this records
the checks against a real station on the air, on 17 October 2026.

- **Sender:** `aprscli send message` as NZ0P-10 on APRS-IS, with the default retry profile.
- **Radio:** a Kenwood TH-D72A as NZ0P-9 on 144.390 MHz, a few metres from the maintainer's Dire Wolf. It reaches
  the IGates in the area only through digipeaters.
- **Watching:** the maintainer's Dire Wolf over KISS TCP (`aprscli tnc listen`, receive only), to see what went out
  on RF; for the last run, a second APRS-IS login with the filter `b/NZ0P-9/NZ0P-10`, to see what reached APRS-IS.

Nothing in these runs transmitted on RF from the development machine: aprscli only sent to APRS-IS, and local IGates
chose what to gate to RF.

## Runs

| Run | Command | Seen | Result |
| --- | --- | --- | --- |
| 1 | `send -c NZ0P-10 message NZ0P-9 "APRS.NET ack test, please ack"` | Gated to RF by two IGates, but the radio's APRS TNC was off, so no ack; four sends on the default schedule before it was stopped | Not a test of the software |
| 2 | `send -c NZ0P-10 message NZ0P-9 "APRS.NET ack test 2"` | Sent as `{01}`, gated to RF, and shown on the radio. The radio answered `ack01}`, an IGate gated it to APRS-IS, and aprscli printed "Acknowledged by NZ0P-9 after 1 attempt." | Passed |
| 2, reply | Sent from the radio: `hi-hi{0` | aprscli printed "Reply from NZ0P-9: hi-hi" and sent `ack0` | Passed |
| 3 | `send -c NZ0P-10 message --wait-replies 10m NZ0P-9 "APRS.NET ack test 3, please reply"` | The radio acked the first send within seconds on RF, but it reached APRS-IS about 5 s after the 30-second retry, so: "Acknowledged by NZ0P-9 after 2 attempts." | Passed |
| 3, reply | A retry of `hi-hi{0` from the radio, gated to APRS-IS | aprscli printed the reply and sent `ack0`; the second APRS-IS login saw `NZ0P-10>APZNET,TCPIP*,qAC,T2PERTH::NZ0P-9   :ack0` less than a second later | Passed |

The radio sends reply-ack acks (`ack01}`) for a reply-ack message (`{01}`), and its own messages carry a plain
one-character ID (`{0`) with no reply-ack; the station handled both, and its ack echoed the radio's ID as sent.

## What didn't reach the radio

Neither `ack0` was gated from APRS-IS to RF: Dire Wolf never heard one in about 30 minutes, and the radio kept
resending `hi-hi` until it gave up. aprscli's ack was on APRS-IS (run 3 shows it there), so the gap is in the IGates:
many gate a message to RF only for a station they have heard directly or within a hop or two, and the radio reached
them only through digipeaters. Our own messages to the radio were gated, so the IGates' rules differ by station or
by timing; which rule applied wasn't found.

The two-way IGate (#212, #214) closes this gap for a station that runs one.

## Also seen

- An IGate in the area gated a copy of the radio's ack with ` <<UI>>:` inserted after the path, its TNC's monitor
  format. The parser leaves it as an unknown packet, as the corpus test
  `AnIGateThatInsertsUiMarkersLeavesThePacketTypeUnknown` already checks for this IGate's other traffic, so the
  station ignored it and took the clean copy.
- `aprscli send message --dry-run` printed the message without the `{01}` it is sent with; it now prints it as sent.

## Result

The messaging engine sends, retries, takes reply-acks, receives replies with plain IDs and acks them, against a real
Kenwood radio over APRS-IS and RF. The only step that failed, the ack gated back to RF, is outside the station.
