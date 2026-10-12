---
id: aprs12.index
spec: APRS 1.2 Addendum — "APRS SPEC Addendum 1.2 Proposals"
status: never formally approved; living draft maintained by Bob Bruninga, WB4APR (SK 2022). Many items are de-facto standards.
canonical_url: https://www.aprs.org/aprs12.html
pdf_available: false
note: No separate "1.2c" document exists on aprs.org; aprs12.html is the single living 1.2 draft. The weather file calls its content "1.2.1".
---

# APRS Specification Addendum 1.2 — Index

All spec updates proposed since the 1.1 freeze (July 2004). Implementation status varies; each file
has a `status:` line. Rule IDs are `A12-<AREA>-nn`.

### Recommended implementation priority for a parser/decoder

| Priority | File | Why |
|---|---|---|
| **High** | [mic-e-type-codes.md](mic-e-type-codes.md) | Every modern radio; device ID, msg-capability, field order, speed > 670 kt, PHG in Mic-E |
| **High** | [dao-precision-datum.md](dao-precision-datum.md) | `!DAO!` widely transmitted |
| **High** | [frequency-afrs.md](frequency-afrs.md) | Radios insert `FFF.FFFMHz Tnnn ±ooo`; freq objects |
| **High** | [telemetry.md](telemetry.md) | Base-91 comment telemetry `\|ss11\|`; 000–999 telemetry |
| **High** | [comment-field-extensions.md](comment-field-extensions.md) | Consolidated comment parse order, delimiters, PHGR |
| **High** | [symbol-overlays.md](symbol-overlays.md) | Overlays on all alternate symbols + meanings |
| Medium | [messaging-extensions.md](messaging-extensions.md) | ITEM-in-MSG, UTF-8, message object/QRZ servers, `{IP-` |
| Medium | [weather-sensors.md](weather-sensors.md) | `X` radiation, `F` water level, `V` volts, `*_`/`*W`/`*H` rule |
| Medium | [objects.md](objects.md) | `111111z` permanent objects, `AP0Cxy` caching |
| Medium | [operator-precedence-rr-bits.md](operator-precedence-rr-bits.md) | `O` overlay, lower-case overlay precedence, RR bits, preemptive digi |
| Medium | [emergency-codes.md](emergency-codes.md) | `!EMERGENCY!` etc. for non-Mic-E |
| Low | [digipeating-and-network.md](digipeating-and-network.md) | Decay, proportional pathing, stewardship, RGATING, ALOHA, Voice Alert |
| Low | [other-proposals.md](other-proposals.md) | Barcodes, APRStt, MileMarks, NAVITRA, etc. |
| — | [open-issues.md](open-issues.md) | Unresolved spec questions + source discrepancies |

### Already in 1.1 (reminder list from aprs12.html)

Baseline usage through July 2004; symbol tables & attributes; TOCALLs & experimental formats; digi
overlay chars, default paths, NOID/HID, SSIDs; datum, range scale, PHG display, timestamps; reply-ACK;
polygon/line objects; object punctuation/ownership/killing; deprecated compressed objects, items,
raw WX; WXSVR & APRS-IS notes. → see `../aprs11/`.
