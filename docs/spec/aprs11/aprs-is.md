---
id: aprs11.aprs-is
spec: APRS 1.1 Addendum
status: approved (the addendum notes "an APRS-IS spec still does not exist"; see aprs-is.net)
topic: IGate / APRS-IS rules in the addendum
affects_aprs101_pages: []
sources:
  - https://www.aprs.org/aprs11.html
  - http://www.aprs-is.net/q.htm   # external, not mirrored
---

# APRS 1.1 — APRS-IS (Internet System)

- **A11-IS-01 NOGATE / RFONLY.** A packet whose RF digipeater path contains `NOGATE` or `RFONLY`
  must **not** be gated RF→IS by IGates.
- **A11-IS-02 `!x!` = no archive.** Any packet (position, status, message) containing the string
  `!x!` must not be archived by APRS-IS databases. (Clients may still display it live.)
- **A11-IS-03 q-construct.** All packets entering APRS-IS are marked with a `qXX` construct
  identifying the point/method of entry (`qAR`, `qAS`, `qAC`, `qAX`, `qAU`, `qAo`, `qAO`, `qAI`, …).
  Spec: aprs-is.net/q.htm (external). Path parser must recognise `TCPIP*`, `TCPXX*` and `qA?`.
- **A11-IS-04 IGate status report.** Status packet beginning with `<` (left bracket in the source
  text is ambiguous; the long-standing convention is the **capabilities** DTI `<`):
  ```
  <IGATE,MSG_CNT=n,LOC_CNT=n
  ```
  (aprs11.html literally says: *Left_bracket then "IGate, MSG_CNT=N, LOC_CNT=N"*.)
  `MSG_CNT` = messages gated IS→RF, `LOC_CNT` = local stations heard. Clients may draw a circle or
  marker around any station sending it (1.2 idea).
- **A11-IS-05 Timestamps.** APRS-IS adds latency and individual IGate clocks can't be trusted;
  the addendum offers no solution. Receivers should still stamp on receipt (see
  `display-and-timing.md`).
- **A11-IS-06 3rd-party filtering.** IGates should loop-protect by checking for `TCPIP*`, not by
  dropping all 3rd-party (`}`) packets (from 1.2 `info/object-perm.txt`).
- Servers: Core (`core.aprs.net`), Tier-2 (`aprs2.net`), how it works: `aprs-is.net`.
- WXSVR protocols (Dale Huguley): `wxsvr.net/protocol.html` (external).

## Implementation checklist

- [ ] RF→IS gate: drop if path contains `NOGATE`/`RFONLY`, if 3rd-party with `TCPIP`, or if generic dupe.
- [ ] Archive/log layer: honour `!x!`.
- [ ] Recognise `<IGATE,...` capability packets.
