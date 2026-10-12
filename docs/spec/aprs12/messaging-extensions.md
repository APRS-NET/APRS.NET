---
id: aprs12.messaging
spec: APRS 1.2 Addendum (draft/proposals)
status: proposals (ITEM-in-MSG and UTF-8 messages are commonly supported; others mostly unimplemented)
topic: ITEM-in-MSG, MESSAGE OBJECT/KILL/QRZ servers, FREQ-in-MSG, UTF-8, IGate path report, universal messaging
affects_aprs101_pages: [71, 72, 73]
sources:
  - https://www.aprs.org/aprs12/item-in-msg.txt     # 21 Feb 2012
  - https://www.aprs.org/aprs12/msg-obj.txt         # 24 Feb 2012
  - https://www.aprs.org/aprs12/utf-8.txt           # 17 Dec 2009
  - https://www.aprs.org/aprs12/Igate-path-report.txt  # 30 Oct 2008
  - https://www.aprs.org/aprs-messaging.html
  - https://www.aprs.org/txt/messages.txt
  - source/aprs.org/ (same paths)
see_also:
  - ../aprs11/messaging.md
  - frequency-afrs.md   # FREQ-in-MSG / QSY
---

# APRS 1.2 — Messaging extensions

## A12-MSG-01 — ITEM-in-MSG

An **item** embedded as the text of a normal message, so a position/object can be delivered to a
specific station across the APRS-IS → RF path.

```
:USER     :)NAME!DDMM.mmN/DDDMM.mmW$comment...{nn
```

- Text begins with `)` + item name (≤ 9 chars) + `!` + uncompressed position + symbol + comment.
  (The aprs12.html summary shows `)OBJNAME!DDMM.mmN/DDDMM.mmW$...`.)
- Comment may contain any position extension: `CCC/SSS/`, `PHGphgd`, `FFF.FFFMHz`, DF `CSE/SPD/BRG/NRQ`, …
- Sent/acked like any message (line number). For a wider audience, send to a generic addressee
  so it is not acked but retried ~5 times.
- **Receive:** copy the item to the station/object list and plot it; **retain sender callsign** as owner.
- **Read-the-mail:** even if not addressed to us, any ITEM-in-MSG heard should be plotted.
- **Retention:** if the client cannot originate objects, keep the message so the user can edit/resend.
- Optional UI: "send selected station/object as ITEM-in-MSG to …".
- Used by AVRS to send a frequency object prompting a QSY.

## A12-MSG-02 — MESSAGE OBJECT (object server)

Lets any radio originate an object at **its own last-known position** via a server.

```
:OBJECT   :OBJNAME [/$] text...{nn          (addressee OBJECT or OBJ)
```
- `OBJNAME` 3–9 chars; optional 2-char symbol in brackets `[/$]`; text may contain CSE/SPD, PHG,
  frequency, DF, etc.
- Server: ack; if name unique → send 3rd-party object for the sender **and** an ITEM-in-MSG back to
  the sender; if name in use (< 24 h old) → reply with owner/location and suggest renaming.
- **KILL server:** send the same message to addressee `KILL`; kills only if sender is the current owner.
- **QRZ server:** addressee `QRZ`; returns ITEM-in-MSG for the station/object matching the first word
  (`?` and `*` wildcards → list; lower-case request ⇒ case-insensitive match).

## A12-MSG-03 — Frequency/QSY in messages

See `frequency-afrs.md` §6 (`FFF.FFFMHz Tnnn QSY?` request / `QSY!` command). Unstable.

## A12-MSG-04 — UTF-8

- Messages, bulletins, announcements: UTF-8 OK (no UTF-8 continuation byte equals `{`, so line
  numbers are safe).
- Status: OK after `>` and optional `DDHHMMz` timestamp.
- Positions/objects: UTF-8 may begin **after** all fixed-format leading extensions:
  `CSE/SPD`, `PHGxxxx`, `RNGxxxx`, `DFSxxxx`, `DIR/SPD` (WX), `Tyy/Cxx` (area), plus
  anywhere-tokens `/A=xxxxxx`, `!DAO!`, `{XX}` signpost.
- Mic-E: OK provided the `xxx}` altitude prefix and the trailing `Mv` bytes are preserved.
  (Source says altitude bytes are "between $21 and $92"; the base-91 alphabet is actually
  `$21`–`$7B`.)
- Global/international traffic should still prefer ASCII.
- **Decoder rule:** decode text fields as UTF-8 with fallback (e.g. Latin-1/ replacement) for invalid
  sequences; never let decoding failure drop a packet. *(implementation guidance)*

## A12-MSG-05 — IGate path report (`{IP-`)

After gating a message IS→RF, an IGate reports what it did on APRS-IS:

```
IGATECALL>APXXXX,RULE:{IP-<packet exactly as transmitted on RF>
```
`RULE` codes (examples): `-Hd` heard direct, `-H1` one hop, `-H2` two hops, `-CL` callsign list,
`-LA` local area. Uses the experimental `{` DTI with header `IP-`. Proposal only.

## A12-MSG-06 — Universal text messaging (initiative)

Tie together APRS, Winlink, DTMF paging, SMS/email gateways, EchoLink/IRLP text etc. so any ham can
text any other by callsign. Informational; no wire format beyond normal APRS messages.

## Message-entry guidance

See `../aprs11/messaging.md` (67-char lines, multi-line warnings).
