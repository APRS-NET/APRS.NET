---
id: aprs11.messaging
spec: APRS 1.1 Addendum
status: approved
topic: Reply-ACK algorithm, auto-answer rules, message entry limits
affects_aprs101_pages: [71, 72, 73]
sources:
  - https://www.aprs.org/aprs11/replyacks.txt
  - https://www.aprs.org/aprs11.html   # auto-answer rules (updated 3 Nov 2011)
  - https://www.aprs.org/txt/messages.txt
  - source/aprs.org/aprs11/replyacks.txt
  - source/aprs.org/txt/messages.txt
see_also:
  - ../aprs12/messaging-extensions.md
---

# APRS 1.1 — Messaging

## Reply-ACK (`{MM}AA`)

Fully backward compatible with APRS 1.0 (the extension lives inside the free-form message-ID).

```
Original message ID:   {#####          (up to 5 chars, no restrictions)
Reply-ACK message ID:  {MM}AA          MM = this message's ID, AA = free ACK (may be empty)
Reply-ACK capable, no ack pending:  {MM}
```

`}` was chosen because it is not a base-91 digit, so it can't collide with existing IDs.
A trailing `}` with empty `AA` advertises "I am reply-ACK capable".

### Rules

- **A11-RACK-01 (send)** Always send IDs as `{MM}AA`. `AA` = the **latest** message ID received
  from that station that we owe an ACK for, or empty. Append `AA` only **at the instant of
  transmission** (a queued line holds just `{MM}`); `AA` may differ between retries of the same `MM`.
- **A11-RACK-02 (receive, implicit ack)** On receiving `...{MM}AA`, if `AA` matches the `MM` of one
  of *our* outstanding messages to that station, mark it ACKed.
- **A11-RACK-03 (dedupe)** Strip `AA` (everything after `}`) before storing/duplicate-checking, since
  copies of the same `MM` may carry different `AA`.
- **A11-RACK-04 (explicit ack unchanged)** Still send the normal explicit ack, echoing the
  **entire** received ID: received `{MM}AA` → send `ackMM}AA`.
- **A11-RACK-05** After receiving `MM}AA`, remember `MM` as the latest-ACK-owed for that station
  (becomes our next outgoing `AA`).
- **A11-RACK-06 (receive explicit ack)** On receiving `ackMM}AA`, use the `MM` part (before `}`)
  to match our outstanding `{MM}`.
- Rationale: over 2 hops at 70 % channel success, a message gets through ~50 % and its ack only
  ~25 %; free acks riding on replies dramatically improve dialog reliability. Pair with the decaying
  retry schedule (first retry ~15 s).

### Test vectors

| Received message ID | Store/dedupe key | Explicit ack we send | Implicit ack of our msg |
|---|---|---|---|
| `{12` | `12` | `ack12` | — |
| `{12}` | `12` | `ack12}` | — (sender is reply-ack capable) |
| `{12}AB` | `12` | `ack12}AB` | our `{AB}` → ACKed |
| ack text `ack12}AB` | — | — | our `{12}` → ACKed |

## Auto-answer messages (added 3 Nov 2011)

Auto-answer ("I'm away") messages are network spam; original clients followed these rules:

- **A11-AA-01** Text must begin with `AA:`.
- **A11-AA-02** **No message number** (so it does not trigger acks).
- **A11-AA-03** Sent **once** per incoming message packet (no retries).
- **A11-AA-04** Default **OFF** on power-up.
- **A11-AA-05** Cancel automatically when the client detects the operator has returned.
- **A11-AA-06** May end with a `}yy` reply-ACK if the software supports it.

## Message entry (txt/messages.txt, 2003)

APRS messages are one-liners; line 2+ stall until an ACK returns. Recommendations for UIs:

- **A11-MSG-01** Max line length **67** chars (TH-D7 shows 45, TM-D700 shows 64).
- **A11-MSG-02** Send nothing until a multi-line entry is complete and confirmed.
- **A11-MSG-03** Warn & require confirmation if > 3 lines.
- **A11-MSG-04** Warn if a 2nd line is < 20 % of the first (suggest rewording).
- **A11-MSG-05** Optionally preview how it renders on a D7 (12×12×12×9) / D700 (22×22×20) display.
