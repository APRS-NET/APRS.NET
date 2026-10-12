---
id: aprs12.other
spec: APRS 1.2 Addendum (draft/proposals) — "new ideas" section
status: ideas / informational; low priority for implementation
topic: Barcodes, APRStt, MileMarks, Japanese NAVITRA gateway, vicinity plots, Omni-DF, eyeball RNG zoom, IGate circles, misc
sources:
  - https://www.aprs.org/aprs12.html
  - https://www.aprs.org/aprs12/barcode.txt
  - https://www.aprs.org/aprstt.html
  - https://www.aprs.org/aprs12/navitraDX.txt
  - https://www.aprs.org/APRS-docs/VICINITY.TXT
  - https://www.aprs.org/avrs.html
  - source/aprs.org/ (same paths)
---

# APRS 1.2 — Other proposals and ideas

| ID | Item | Summary | Wire format |
|---|---|---|---|
| A12-IDEA-01 | **Barcodes** (Feb 2007) | Carry scanned barcodes as: a message to an application callsign (preferred, captured by all clients/findu), a bulletin `:BLN1SCAN :`, status, or position comment. "Barcode posit" (barcode encodes the position) vs "wand posit" (scanner's GPS + barcode in comment). | No new DTI |
| A12-IDEA-02 | **APRStt** (TouchTone) | DTMF-only users report callsign/position/frequency via an APRStt gateway, which emits APRS packets (e.g. `WB4APR>APRStt:!DDMM.mmN/DDDMM.mmWA146.940MHz listening APRStt`). Symbol family `#A` (BOX) with overlays `9A` mobile, `7A` HT, `HA` house, `EA`/`IA`/`AA`/`DA`; SSID `-12`; object names may use `-tt`. DTMF position order changed to **lat then lon** (Aug 2013); new callsign encoding in draft APRStt 3.01 (2015). | Gateway emits normal APRS |
| A12-IDEA-03 | **MileMarks** | Interstate mile-marker based position reporting (Tampa example). | n/a |
| A12-IDEA-04 | **Japanese NAVITRA** | JGates linking Japan's NAVITRA system to APRS. | see `navitraDX.txt` |
| A12-IDEA-05 | **EchoLink/IRLP node status** (AVRS) | VoIP node objects and Automatic Voice Relay System (call by callsign; auto-QSY via ITEM-in-MSG). | `frequency-afrs.md` §4 |
| A12-IDEA-06 | **Vicinity plots** | Plot positionless/new stations near the first digi heard, `?` symbol. | `../aprs11/display-and-timing.md` |
| A12-IDEA-07 | **Omni-DF** | DF by signal-strength circles inversely scaled from PHG; no DF gear needed. Uses `DFSshgd`. | APRS101 DF |
| A12-IDEA-08 | **Eyeball RNG zoom** | An eyeball (`/E`) object with an `RNGrrrr` descriptor causes receiving maps (if enabled) to **center and zoom** to that range on first receipt. | `RNGrrrr` on `/E` |
| A12-IDEA-09 | **IGate circle** | Draw a circle/attribute around any station sending an IGate status packet (`<IGATE,...`), independent of symbol. | `../aprs11/aprs-is.md` |
| A12-IDEA-10 | **TH-D7 manual table positions** | Pick positions from a table without GPS. | n/a |
| A12-IDEA-11 | **Microwave antenna pointing** | Use APRS to coordinate microwave experiment antenna pointing. | n/a |
| A12-IDEA-12 | **SkyCommand** | Remote HF control additions (USNA). | n/a |
| A12-IDEA-13 | **Vicinity indoor tracking (BOXn digis)** | Nearly-deaf indoor digis named `BOXn`; users plotted near the digi that heard them. | via paths |
| A12-IDEA-14 | **APRS 1.3 proposals** | `specfuture.html` ("New APRS areas") — page not archived; not available. | — |
