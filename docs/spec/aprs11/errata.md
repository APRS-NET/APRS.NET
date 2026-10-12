---
id: aprs11.errata
spec: APRS 1.1 Addendum
status: approved (APRS-WG vote called 30 Jul 2004; page maintained through 2012+)
topic: Corrections to the APRS 1.0.1 specification text
affects_aprs101_pages: [11, 18, 21, 23, 25, 28, 34, 45, 48, 55, 91, 95, "AX.25 UI-frame table"]
sources:
  - https://www.aprs.org/aprs11.html
  - source/aprs.org/aprs11.html.md
---

# APRS 1.1 — Corrections to APRS 1.0.1

The 1.1 addendum does **not** republish the spec. Each item below amends a
specific page of `../APRS101.PDF`. Items are keyed `A11-ERR-nn` for traceability.

| ID | APRS101 page | Correction | Parser / encoder impact |
|---|---|---|---|
| A11-ERR-01 | p.18 | The **X1J exception** (data type identifier not at byte 0) was abandoned/deprecated in 2012. The `!` DTI is only valid at the **beginning** of the information field. | Do **not** scan up to 40 chars for `!` as APRS101 describes. Treat `!` as DTI only at offset 0. (Consider a lenient opt-in mode for legacy captures.) |
| A11-ERR-02 | p.55 | Mic-E optional altitude (`xxx}`) comes **first**, immediately after the Mic-E **type** byte. (The type byte is extended in 1.2; see `../aprs12/mic-e-type-codes.md`.) | Mic-E status text parse order: `type byte` → optional `aaa}` → rest. |
| A11-ERR-03 | p.25 | References an NMEA `WPT` sentence; almost certainly means **`$GPWPL`** (per WE7U). | Accept `WPL` (and tolerate `WPT`). |
| A11-ERR-04 | p.28 | PHG antenna **gain is in dBi**. | Range calc uses dBi. |
| A11-ERR-05 | p.34 | DF `NRQ` is not fully defined in APRS101; see original `APRS-docs/DF.TXT` and `PROTOCOL.TXT`. | See `source/aprs.org/APRS-docs/DF.TXT`. |
| A11-ERR-06 | p.45 | Terminology: say **"Mic-E Position Comment"**, not "Mic-E Messages", for the 3 message bits. | Naming only (use `MicEPositionComment` / message-code enum, not "message"). |
| A11-ERR-07 | p.48 | Mic-E speed/course table: 100–109 maps to `u` (not DEL); 110–179 maps to 38–107 i.e. up to `k` (not DEL). | Fix lookup tables for the Mic-E DC+28 byte encoding. |
| A11-ERR-08 | AX.25 UI-frame table | Trailing flag is **1** byte, not 2, and may be shared with the next frame (VK2TDS). | Relevant only for raw HDLC framing. |
| A11-ERR-09 | p.11 | Default path is **`WIDE2-2`**, not `RELAY` or `RELAY,WIDE` (updated Jun 2006). | Default outgoing path for clients = `WIDE2-2` (mobiles: `WIDE1-1,WIDE2-1`; see New-N in `ax25-ssid-digipeating.md`). |
| A11-ERR-10 | p.91 | TNC **HID should be OFF** in all APRS TNCs. | Any TNC init strings we emit must include `HID OFF`. |
| A11-ERR-11 | p.95 | SSID conventions replaced — see `ax25-ssid-digipeating.md`. | Informational. |
| A11-ERR-12 | p.21 | **Datum**: standard is WGS-84; continental agreements (e.g. OSGB36 in UK) allowed. See `!DAO!` in 1.2. | Default datum WGS-84. |
| A11-ERR-13 | p.11 | Map **range scale** standard view-size descriptor. See `display-and-timing.md`. | UI only. |
| A11-ERR-14 | p.23 | **Time stamps on receipt** — see `display-and-timing.md`. | Every received packet gets a receive timestamp. |
| A11-ERR-15 | p.10 | Fade-to-gray / timeout old stations at **80 minutes** (was 2 hours) to allow for satellite-path stations. | Default station-stale timeout = 80 min. |
| A11-ERR-16 | p.29 | Display PHG range circles at **half** the APRS101 formula value (mobile range). See `display-and-timing.md`. | UI default. |
| A11-ERR-17 | p.60 | Area-object reference point & size formula are wrong in APRS101. See `objects-items-names.md`. | Fix area-object decode. |
| A11-ERR-18 | p.64 | Weather field clarifications. See `weather.md`. | Weather parser. |
| A11-ERR-19 | p.73 | Reply-ACK algorithm. See `messaging.md`. | Messaging. |
| A11-ERR-20 | p.58 | Compressed **Objects** not recommended on RF (incompatibilities). For ~1 ft precision use `$GPGGA`, 3rd-party compressed posits, or `!DAO!`. | Encoder: prefer uncompressed objects on RF. Decoder: still accept. |
| A11-ERR-21 | p.37 | **Item** format (`)`) not recommended on RF (incompatibilities). | Encoder: prefer objects (`;`) on RF. Decoder: still accept. |
| A11-ERR-22 | p.62 | **Raw weather** formats (Peet/Ultimeter raw) not recommended; micro-controllers should convert to the *complete* format on RF. | Decoder: still accept. Encoder: emit complete format. |
| A11-ERR-23 | p.89 | Experimental format registry — see `tocalls-altnets-experimental.md`. | `{` user-defined DTI. |
| A11-ERR-24 | p.14 | TOCALL / version ID list — see `tocalls-altnets-experimental.md`. | Device identification. |
| A11-ERR-25 | p.92, p.24 | Symbol attributes & position ambiguity display — see `symbols.md`. | UI. |
| A11-ERR-26 | p.31 | Polygon and line objects: WXSVR multiline protocol (external, `wxsvr.net/APRS_Multiline_Protocol.html`; not mirrored). | Optional. |
