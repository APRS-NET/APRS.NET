---
id: aprs12.bits
spec: APRS 1.2 Addendum (draft/proposals)
status: proposals — "O" overlay operator-present is in light use; precedence & RR-bit schemes essentially unimplemented
topic: Operator-present bit, precedence (ex-priority) bit, AX.25 RR bits, preemptive digipeating
affects_aprs101_pages: []
sources:
  - https://www.aprs.org/aprs12/operator-bit.txt         # 16 Jan 2013
  - https://www.aprs.org/aprs12/precedence-bit.txt       # 16 Jan 2013
  - https://www.aprs.org/aprs12/priority-bit.txt         # obsolete, historical
  - https://www.aprs.org/aprs12/RR-bits.txt              # 05 Dec 2012
  - https://www.aprs.org/aprs12/preemptive-digipeating.txt  # 04 Dec 2012
  - source/aprs.org/aprs12/ (same files)
---

# APRS 1.2 — Operator-present, precedence, RR bits, preemptive digipeating

## History of "spare bits"

Pre-2012 APRS had only three spare bits in an uncompressed position: the **case** of the `N/S`
byte, of the `E/W` byte, and of the **overlay/table** byte. Lower-case `N/S` was abandoned because
UI-View drops such packets entirely. Hence the overlay-byte schemes below, and later the RR-bit idea.

## A12-OP-01 — Operator present (overlay `O`)

- When operator activity is detected (keyboard/mouse), save the current overlay, set overlay to
  **`O`**, send a position **immediately**, and reset the decay schedule to 10 min.
- After **30 min** without activity, restore the saved overlay and let the rate decay back to 30 min.
- Display: operator-present stations highlighted (APRSdos: bright white; others gray). Offer a
  "show only active" filter: operator-present (`O`) stations, mobiles in motion, message-capable Mic-E.
- `O-` (operator on a house) is also listed in the symbol overlay table.
- Decoder: `overlay == 'O'` on an alternate symbol ⇒ `OperatorPresent = true`.

## A12-PREC-01 — Precedence bit (lower-case overlay)

(Was "priority bit" until Jan 2013; renamed to avoid confusion with Mic-E *Priority* status.)

- If the overlay byte is **lower case** (`a`–`z`), the packet is **PRECEDENCE**; primary table or
  upper-case overlay ⇒ **ROUTINE**.
- Only possible on **alternate symbols with an alpha overlay**; **cannot** be used in compressed
  positions (lower-case `a`–`j` there encode overlay digits 0–9).
- Intended use: digis in "MINIMIZE" mode — *minimum*: routine packets digipeated once only; *maximum*:
  routine packets dropped; precedence always passed normally.
- Decoder: `table in 'a'..'z'` (uncompressed only) ⇒ `Precedence = true`, overlay = upper-case of it.

## A12-RR-01 — AX.25 RR bits (future, Dec 2012)

SSID octet: `H R R S S S S E` (see `../aprs11/ax25-ssid-digipeating.md`). RR default `11`.

| Address field | Proposed RR use |
|---|---|
| Source | bit 1 = **precedence** (1 = precedence/default, 0 = lower, decimatable); bit 2 = **operator present** |
| Destination | TBD |
| Each digi field | LSB RR: 1 = not pre-empted (default), 0 = this field was **pre-empted** (with H set); MSB TBD (default 1) |

- Receivers must **ignore** RR bits and **pass them unchanged** when digipeating. KISS and soundcard
  TNCs can access them; hardware TNCs mostly can't.
- Known other uses: RR bit 5 by DAMA (connected-mode, Europe); AX.25 modulo-128 uses bit 6 of source SSID.
- Note the conflict: `RR-bits.txt` says default 1 = precedence; `preemptive-digipeating.txt` says
  default 1 = PRIORITY and 0 = droppable. Both agree on wire semantics (0 = less important).

## A12-PRE-01 — Preemptive digipeating (rev b, Dec 2012)

Digi option `PREEMPT OFF | DROP | MARK`.

- When ON, the digi searches from the **first unused** digi field **to the end** of the path for any
  of its aliases (MYCALL, MYALIAS, UIDIGI). On a match:
  - replace that alias with **MYCALL**, set its H bit; digipeat **once**; fields after it stay active.
  - **DROP**: remove all earlier unused fields (preempted alias becomes first digi field).
  - **MARK**: keep earlier fields but set H and clear the RR-LSB ("pre-empted") on each skipped field
    up to and including the preempted alias.
- **Not** applied to generic `XXXXn-N` (UIFLOOD/UITRACE) aliases.
- Use cases: `WIDE2-2,HOMEX` (always reach HOMEX in 1–3 hops); `WIDE1-1,CITYA,WIDE2-1,CITYB`;
  event gateway `FREQB7-7,GATE,WIDE2-1` so exactly one copy crosses the gateway.

## Display

Operator-present and precedence should be treated as fundamental symbol attributes alongside the
legacy 8 color attributes; above all, always distinguish **stations vs objects** and **my objects
vs others'** (ownership can change packet-by-packet).
