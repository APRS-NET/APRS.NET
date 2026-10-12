---
id: aprs12.mic-e
spec: APRS 1.2 Addendum (draft/proposals)
status: de-facto standard (widely implemented by Kenwood, Yaesu, Anytone, Byonics, clients)
topic: Mic-E type byte, manufacturer/version suffix, status-text field ordering, speed extension, PHG in Mic-E
affects_aprs101_pages: [42-56]
sources:
  - https://www.aprs.org/aprs12/mic-e-types.txt       # 16 Sep 2021
  - https://www.aprs.org/aprs12/mic-e-examples.txt    # 10 Dec 2008
  - https://www.aprs.org/aprs12.html
  - source/aprs.org/aprs12/mic-e-types.txt
  - source/aprs.org/aprs12/mic-e-examples.txt
---

# APRS 1.2 — Mic-E type codes and text field

## Mic-E status-text layout

After the 9 fixed info bytes (DTI, 3 lon bytes, 3 speed/course bytes, symbol, table) comes the
"text" field, written here as **`T......Mv`**:

| Part | Meaning |
|---|---|
| `T` | **Type byte**: one of `␠` (space), `>`, `]`, `` ` ``, `'` |
| `aaa}` | Optional altitude — **must be first** after `T` (A11-ERR-02). `aaa` = base-91, metres, offset −10000. |
| `FFF.FFFMHz` | Optional frequency — first "text" after the optional altitude (see `frequency-afrs.md`). Early FTM-350s need a following space. |
| `...` | Free text (status/comment). May contain any normal position-comment extension, e.g. `PHGphgd` (**A12-MICE-05**), `/A=nnnnnn` (should be near the end), base-91 telemetry `\|...\|`, and `!DAO!` (**last**, before `Mv`). |
| `Mv` | Optional **manufacturer** byte + **version** byte at the very end. Some legacy codes are a single byte. A space before ` Mv` is recommended for readability. |

Canonical ordering (from `mic-e-examples.txt` + base91 telemetry spec):
```
'abc123R/T[aaa}][FFF.FFFMHz][ ]text...[/A=nnnnnn][|tlm|][!DAO!][ ]Mv
```

> Note on the DTI: `` ` `` was intended for a *current* GPS fix and `'` for an *old* fix, but Kenwood
> implemented them backwards and it became frozen in the spec. Do not infer fix age from the DTI
> for Kenwood radios.

## Type byte (`T`)

| `T` | Meaning | Message capable |
|---|---|---|
| `␠` | Original Mic-E | no |
| `>` | Kenwood TH-D7 family (HT) | yes |
| `]` | Kenwood TM-D700 family (mobile) | yes |
| `` ` `` | **OTHER Mic-E, message capable** — "McE-Msg" | yes |
| `'` | **OTHER Mic-E, tracker only** — "McE-Trk" | no |

The `` ` `` and `'` were originally Mic-E telemetry flags (deprecated, see below) and are the only
extra bytes all legacy radios/UI-View pass through, hence their reuse (2008).

## Device identification table

| Pattern | Device | Notes |
|---|---|---|
| `␠.......` | Original Mic-E | not msg capable |
| `>.......` | TH-D7A (TOCALL `APK001`) | msg |
| `>......v` | *future* TH-D7 version byte | |
| `>......=` | **TH-D72** (`APK003`) | msg |
| `>......^` | **TH-D74** (`APK004`) | msg |
| `].......` | TM-D700 (`APK101`) | msg |
| `]......=` | **TM-D710** (`APK201` in this doc) | msg |
| `]......v` | *future* D7xx version | |
| `` `......_␠ `` | Yaesu **VX-8** (`_` + SPACE) | msg |
| `` `......_" `` | Yaesu **FTM-350** | msg |
| `` `......_# `` | Yaesu **VX-8G** | msg |
| `` `......_$ `` | Yaesu **FT1D** | msg |
| `` `......_% `` | Yaesu **FTM-400DR** | msg |
| `` `......_) `` | Yaesu **FTM-100D** | msg |
| `` `......_( `` | Yaesu **FT2D** | msg |
| `` `......_0 `` | Yaesu **FT3D** | msg |
| `` `......_3 `` | Yaesu **FT5D** | msg |
| `` `......_1 `` | Yaesu **FTM-300D** | msg |
| `` `......␠X `` | AP510 (KD7LXL) | **not** msg capable (spec violation: uses `` ` ``) |
| `` `......(5 `` | Anytone **D578UV** | msg |
| `'......(8` | Anytone **D878UV** | not msg |
| `'......\|3` | Byonics **TinyTrack3** | |
| `'......\|4` | Byonics **TinyTrack4** | |
| `T......\v` | HamHUD (unconfirmed) | |
| `T....../v` | Argent (unconfirmed) | |
| `T......^v` | HinzTec anyfrog | |
| `T......*v` | KissOZ tracker (`APOZxx`) | |
| `'......:4` | SCS P4dragon DR-7400 | not msg |
| `'......:8` | SCS P4dragon DR-7800 | not msg |
| `T......~v` | OTHER (reserved for when other `M` bytes are exhausted) | |

Reserved/available `M` bytes (coordinate with list maintainer): `` ` `` `'` `/` `\` `-` `:` `;` `.`

> The maintained list today is the community `aprs-deviceid` YAML (`mice:` and `micelegacy:`
> sections). Prefer it for data; this table is the 2021 aprs.org snapshot.

## Display rules

- **A12-MICE-01** Recognise the leading `T` byte (` `, `>`, `]`, `` ` ``, `'`) and **do not display
  it** as part of the status text; use it for the radio-type/message-capable flag instead.
- **A12-MICE-02** If the trailing `Mv` is recognised, it may be suppressed from the displayed text and
  shown as the device name. If **not** recognised, leave it in the text.
- **A12-MICE-03** Unknown device: show "McE-Msg"/"MicE-Msg" for `` ` `` and "McE-Trk"/"McTrackr"
  for `'`.
- **A12-MICE-04** Original Mic-E **telemetry** formats (`` ` `` / `'` + hex channels) are
  **deprecated** — superseded by type codes; use base-91 comment telemetry
  (`telemetry.md`). Also deprecated: alpha/beta Mic-encoder models' special symbols/telemetry.
- **A12-MICE-05** **PHG in Mic-E**: the Mic-E text may contain any normal position-comment field
  such as `PHGphgd` (important for Mic-E hardware digipeaters).
- **A12-MICE-06** UTF-8 is allowed in the Mic-E text provided the altitude prefix and the trailing
  `Mv` bytes are preserved (see `comment-field-extensions.md`).

## Speed above Mach 1 (A12-MICE-07)

For a decoded Mic-E speed **S > 670 knots**, display speed as **`S × 112 − 74370` knots**.
Example: ISS encoded as 799 → 799×112 − 74370 = **15 118 kt**.
(Corrects an earlier formula `S × 135 − 9000`.)

## Test vectors (from mic-e-examples.txt)

Destination `ABCDEF` → lat **12°34.56' N**, message code *Custom 0*, longitude West with +100° offset.
Info bytes `abc123R/` → lon **169°10.71' W**, course **223°**, speed **14** (knots), symbol `/R` (RV).

```
'abc123R/text...
'abc123R/>text...
'abc123R/>text...v               (future D7)
'abc123R/]text...
'abc123R/]text...=               (D710)
'abc123R/'text...... Mv          (other manufacturers)
'abc123R/'FFF.FFFMHztext... Mv
'abc123R/'123}text... Mv
'abc123R/'123}FFF.FFFMHztext... Mv
'abc123R/'123}FFF.FFFMHztext.../A=123456 Mv
'abc123R/'123}FFF.FFFMHztext.../A=123456...!DAO! Mv
```

Real-world example with telemetry + DAO + type code (from base-91 telemetry spec):
```
N0CALL>APRS,qAR,IGATE:`pZ3l-B]/'"6{}|!9'X$u|!wr8!|3
                      ^position----^alt-^tlm----^DAO-^Mv (`|3` = TinyTrack3)
```
