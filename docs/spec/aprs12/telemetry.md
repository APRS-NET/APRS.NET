---
id: aprs12.telemetry
spec: APRS 1.2 Addendum (draft/proposals) + Base91 Comment Telemetry (OH7LZB, 2012-01-11)
status: base91 comment telemetry = implemented (aprs.fi/FAP, APRSIS32, TinyTrak3/4); 000-999 = proposal (28 Apr 2020)
topic: Telemetry value range extension, base-91 comment telemetry, Mic-E telemetry deprecation
affects_aprs101_pages: [68, 69, 70, "Ch.13 Telemetry"]
sources:
  - https://www.aprs.org/aprs12.html
  - http://he.fi/doc/aprs-base91-comment-telemetry.txt
  - source/he.fi/aprs-base91-comment-telemetry.txt
---

# APRS 1.2 — Telemetry

## A12-TLM-01 — Analog values 000–999 (proposal, 28 Apr 2020)

In the classic `T#sss,111,222,333,444,555,bbbbbbbb` report, the five analog channels may carry
**000–999** in addition to the original 000–255. Decoders should accept 0–999 (APRS101 says 0–255).
(In practice many decoders also accept decimals/negatives; keep the parser lenient.)

## A12-TLM-02 — Mic-E telemetry deprecated

The original Mic-E telemetry (`` ` ``/`'` + hex channels after the symbol) is obsolete: those bytes
are now Mic-E **type codes** (`mic-e-type-codes.md`). Use base-91 comment telemetry instead.

## A12-TLM-03 — Base91 comment telemetry

Machine-readable telemetry appended to the comment of **any** position format (uncompressed,
compressed, Mic-E).

### Syntax

```
|SS[A1A1[A2A2[A3A3[A4A4[A5A5[BB]]]]]]|
```

- Delimited by `|` at both ends.
- `SS` = sequence counter, each channel = **2 base-91 chars**:
  `value = (c1 − 33) × 91 + (c2 − 33)`, range **0–8280**.
- Must contain the sequence and **at least 1** analog channel; up to **5** analog channels.
- Optional final `BB` = the 8 digital bits as one base-91 integer: **LSB = B1 … bit 7 = B8**;
  bits 9–13 reserved (ignore).
- Total lengths therefore: 4, 6, 8, 10, 12 (analog only) or 14 (5 analog + bits) chars between pipes.
  Bits are only present after all 5 analog channels.
- Sequence may wrap at 255 (1-byte counter) or `(seq + 1) & 0x1FFF`; senders must never exceed 8280.
- Values 0–255 are fine (no need to upscale); use the standard `PARM`/`UNIT`/`EQNS`/`BITS`
  messages for scaling/labels — they still apply.

### Placement / ordering (A12-TLM-04)

- After the user's free-form comment text.
- **Before** `!DAO!` and before a Mic-E `Mv` type code: `…comment|tlm|!DAO!Mv`.
- Parse telemetry **before** searching for `!DAO!` (a DAO-looking sequence can occur inside the
  base-91 bytes).
- Strip the telemetry from the displayed comment.

### Compatibility notes

`|` is historically the TNC stream-switch character and APRS101 forbids it, but in practice it
traverses RF and APRS-IS fine (TinyTrak uses it in Mic-E type codes). Decoders must accept it.

### Test vectors

| Extension | Decodes to |
|---|---|
| `\|ss11\|` | seq 7544, A1 1472 |
| `\|ss112233\|` | seq 7544, A1 1472, A2 1564, A3 1656 |
| `\|ss1122334455!"\|` | seq 7544, A1..A5 = 1472, 1564, 1656, 1748, 1840; bits value 1 ⇒ B1=1, B2..B8=0 |
| `\|!!!!\|` | seq 0, A1 0 |

Full packets:
```
N0CALL>APRS,qAR,IGATE:`pZ3l-B]/'"6{}|!9'X$u|!wr8!|3                 Mic-E, 2 ch, DAO, type code
N0CALL>APRS,qAR,IGATE:!/0%3RTh<6>dS_http://aprs.fi/|"p%T'.ag|          compressed, comment, 3 ch
N0CALL>APRS,qAR,IGATE:!6304.03NN02739.63E#PHG26303/Siilinjarvi|"p%T'.agff|   uncompressed, PHG, comment, 4 ch
```

Regex: `\|(?<b91>(?:[!-{]{2}){2,7})\|` (each char in `!`..`{` i.e. 33..123; must also check the
max value constraint and even length). Take the **last** such match before any `!DAO!`/`Mv` suffix.
