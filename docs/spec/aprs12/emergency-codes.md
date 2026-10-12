---
id: aprs12.emergency
spec: APRS 1.2 Addendum (draft/proposals)
status: proposal (13 Feb 2007); the !ALARM!-style triggers were implemented in APRS+SA and Xastir
topic: Mic-E-equivalent status/emergency codes for non-Mic-E packets
affects_aprs101_pages: [45]
sources:
  - https://www.aprs.org/aprs12/EmergencyCode.txt
  - source/aprs.org/aprs12/EmergencyCode.txt
---

# APRS 1.2 — Emergency / status codes for non-Mic-E packets

Mic-E encodes 8 "position comment" states in 3 bits (A11-ERR-06). This proposal lets any
position or object carry the same states as text.

## A12-EMG-01 — Bracketed codes

Placed as the **first bytes of the comment** (after `PHGphgd` if present):

| Code | Mic-E equivalent | Receive action (original APRS) | Color |
|---|---|---|---|
| `!EMERGENCY!` | Emergency | center + zoom + **alarm beeps until cleared** | red |
| `!TESTALARM!` | *(new)* | same as EMERGENCY but clearly a **test** | |
| `!PRIORITY!` | Priority | center + zoom to 8-mile range | orange |
| `!SPECIAL!` | Special | center map on symbol | yellow |
| `!COMMITTED!` | Committed | — | |
| `!RETURNING!` | Returning | — | |
| `!INSERVICE!` | In Service | — | |
| `!ENROUTE!` | En Route | — | |
| `!OFF-DUTY!` | Off Duty | — | |

## A12-EMG-02 — Additional center/zoom triggers (APRS+SA, Xastir)

`!ALARM!`, `!ALERT!`, `!WARNING!`, `!WXALARM!`, `!EM!` — proposed new on-receipt level **FLASH**
(silent, unmistakable visual flash; one step below emergency beeping). *(The source says these are
"without the surrounding !" yet lists them with `!`; treat the `!…!` form as canonical and match
case-sensitively.)*

## Examples

```
!DDMM.hhN/DDDMM.hhW$!PRIORITY! Special Event in progress
!DDMM.hhN/DDDMM.hhW$PHG3530!PRIORITY! Special Event in progress
;OBJCTNAME*DDHHMMzDDMM.hhN/DDDMM.hhW$!PRIORITY! Special Event...
;OBJCTNAME*DDHHMMzDDMM.hhN/DDDMM.hhW$PHG3530!PRIORITY! Special...
```

## Implementation notes

- Fully backward compatible (plain comment text) but **no guaranteed response** in older software.
- Decoder: after stripping known leading data extensions, match
  `^!(EMERGENCY|TESTALARM|PRIORITY|SPECIAL|COMMITTED|RETURNING|INSERVICE|ENROUTE|OFF-DUTY|ALARM|ALERT|WARNING|WXALARM|EM)!`
  and map to the same enum as the Mic-E message code.
- Mic-E itself also has `\!` emergency symbol and the Mic-E Emergency code — unify into one `Emergency` flag.
