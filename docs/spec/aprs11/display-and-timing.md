---
id: aprs11.display
spec: APRS 1.1 Addendum
status: approved
topic: Receive timestamps, station timeout, range scale, PHG circle sizing, vicinity plotting
affects_aprs101_pages: [10, 11, 23, 24, 29, 92]
sources:
  - https://www.aprs.org/aprs11/timestamps.txt
  - https://www.aprs.org/aprs11/RangeScale.txt
  - https://www.aprs.org/symbols/PHGmobile.txt
  - https://www.aprs.org/phg.html
  - https://www.aprs.org/APRS-docs/VICINITY.TXT
  - source/aprs.org/ (same paths)
---

# APRS 1.1 — Timestamps, display and range conventions

## Timestamps

- **A11-TS-01** Since 1995 default position reports carry **no** transmit timestamp; receivers
  **stamp on receipt**. RF data is assumed live.
- **A11-TS-02** Once a packet leaves RF, a timestamp must stay associated with it forever.
  **All logs must store a received timestamp**; raw logs without times are "NOT GOOD" (replayed data
  becomes indistinguishable from live).
- **A11-TS-03** If a packet *does* carry a transmit timestamp, honour it as the time the data was valid,
  regardless of receive time.
- **A11-TS-04** Resolution better than 1 minute was never intended on 1200 baud (hence no seconds in
  most formats).

**Implementation:** every decoded packet model has `ReceivedAt` (required, set by the receiver)
and optional `Timestamp` (from the packet). Log format must include `ReceivedAt`.

## Station age

- **A11-AGE-01** Fade-to-gray / timeout old stations at **80 minutes** (was 2 h).

## Range scale

- **A11-RS-01** Every map view is described by **center + range scale**: the radius of the largest
  circle that fits in the view, ideally powers of 2 (… ¼, ½, 1, 2, 4 … 8192 mi or km).
- **A11-RS-02** Show it explicitly: "RANGE = N mi" in a corner, a scale bar N long, and range rings
  of radius N when measuring from own station. Metric equivalents allowed.
- **A11-RS-03** Arbitrary zoom factors (1×, 2×…) should be converted to a range scale for display.

## PHG range circles

- **A11-PHG-01** Display PHG circles at **half** the APRS101 formula by default (realistic **mobile**
  range: 10–30 dB multipath flutter; digital needs ~10 dB more margin). Offer the full-size
  ("fixed path") circle as an option.
- **A11-PHG-02** Path range between two stations is the **sum** of their PHG ranges.
- **A11-PHG-03** Defaults for stations not reporting PHG:
  fixed **`PHG5130`** (25 W, 20 ft, 3 dB) ≈ 4 mi half-size; mobile **`PHG3020`** (10 W, 10 ft,
  2 dB) ≈ 2 mi half-size.
- **A11-PHG-04** Antenna gain in PHG is **dBi** (A11-ERR-04).
- Omni-DF: signal-strength reports draw circles inversely proportional to PHG range; a 0
  (not heard) report draws a circle ~10 % larger than the PHG circle. For signal level 0 use 0.8
  internally to avoid divide-by-zero (open review note, see `../aprs12/open-issues.md`).

## Vicinity plotting (APRSdos; not formally in spec)

When a station is heard with **no** valid position (first packet, positionless WX, `0000/0000`):
place it at a random point within 1 mi of the **first digi in its (traceable) path**, use a `?`
symbol (`\.` "ambiguous"), 1-mile ambiguity, hide its label above the 8-mile range scale.

## Position ambiguity & attributes

Symbol color attributes (APRSdos legacy) convey state: moving, fixed, dead-reckoned, decayed, old,
object, own-object vs other-object, message capable, operator present (white) — see
`../aprs12/operator-precedence-rr-bits.md`. Implementations should at minimum distinguish
**stations vs objects** and **my objects vs others' objects**.
