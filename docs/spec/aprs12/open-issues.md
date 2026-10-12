---
id: aprs12.open-issues
spec: APRS 1.2 Addendum — "Work in progress" section + discrepancies found while summarizing
status: unresolved
topic: Known ambiguities to decide explicitly in APRS.NET
sources:
  - https://www.aprs.org/aprs12.html
---

# Open issues and ambiguities

## From aprs12.html "WORK IN PROGRESS" (reviewer notes, never resolved)

| ID | Issue | Suggested APRS.NET decision |
|---|---|---|
| OI-01 | Formula for (area/DF) sizing not specified in spec. | Area objects: use A11-AREA-01 (`offset²/1500`). |
| OI-02 | Colors of circles not specified. | UI choice. |
| OI-03 | Does `DFSshgd` have a `/` after it? Unspecified. | Accept optional `/` or space delimiter (A12-CMT-02). |
| OI-04 | Can `DFSshgd/` be followed by speed/course? Not in spec. | Don't expect it; tolerate. |
| OI-05 | Object/item format text (APRS101 p.97) doesn't allow `/BRG/NRQ`, but other places do. | Accept `/BRG/NRQ` after `CSE/SPD` in objects/items too. |
| OI-06 | Spec shows `DFS` and `/BRG/NRQ` in the same packet — probably wrong. | Parse whichever is present; don't require exclusivity. |
| OI-07 | Spec says `DFS` **or** course/speed, but DOS examples show both (DFS first). | Accept both, either order. |
| OI-08 | Zero signal level → divide by zero; use 0.8 in place of 0. | Use 0.8 internally for omni-DF circle sizing. |

## Discrepancies noticed while writing these summaries

| ID | Where | Discrepancy |
|---|---|---|
| OI-09 | Mobile symbol set | aprs11.html shows alternate `>KOS^ksuv`; symbolsX.txt (Nov 2008) shows `>KOSY[^ksuv\`. Prefer symbolsX. |
| OI-10 | RR bits | Two files describe default `1` as "precedence" vs "priority"; both mean 0 = droppable. |
| OI-11 | DAO example | `!w:\!` text says longitude digits "59" (unscaled) — formula gives 64.9. Trust formula. |
| OI-12 | Weather examples (`weather-new.txt`) | Use `/Gggg/Tttt/` with slashes & capital T, inconsistent with APRS101 `gggtttt`. |
| OI-13 | Emergency codes | `!ALARM!` etc. described as "without the surrounding !" but listed with them. |
| OI-14 | Mic-E altitude bytes | utf-8.txt says `$21`–`$92`; base-91 range is `$21`–`$7B`. |
| OI-15 | QSY-in-message | Two different syntaxes (aprs12.html vs freqspec.txt). |
| OI-16 | Mic-E D710 TOCALL | mic-e-types.txt says `APK201`; tocalls.txt lists D710 as `APK102`. |
| OI-17 | Water level | 1.1 `spec-wx.txt` header: `Fxxxx` feet / `fxxxx` meters; 1.2: `Fxxxx` signed tenths of a foot vs flood stage, meters not used. Prefer 1.2. |
| OI-18 | IGate status DTI | aprs11 says "Left_bracket then IGate,…"; the conventional form is `<IGATE,MSG_CNT=n,LOC_CNT=n`. |
| OI-19 | Missing sources | `specfuture.html` (1.3 ideas) and a few external links (WXSVR multiline protocol, q-construct page) were not archived/mirrored. |
