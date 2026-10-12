---
id: aprs11.tocalls
spec: APRS 1.1 Addendum
status: approved (living list; snapshot dated 14 Dec 2021)
topic: AX.25 destination (TOCALL) device IDs, ALTNETs, user-defined `{` experimental formats
affects_aprs101_pages: [13, 14, 89]
sources:
  - https://www.aprs.org/aprs11/tocalls.txt
  - https://www.aprs.org/aprs11/expfmts.txt
  - source/aprs.org/aprs11/tocalls.txt
  - source/aprs.org/aprs11/expfmts.txt
---

# APRS 1.1 — TOCALLs, ALTNETs and experimental formats

## TOCALL (AX.25 destination) usage

- **A11-TOC-01** APRS does not route on the destination address. `AP` prefix = "this is APRS";
  the remaining 4 characters (`APxxxx`) identify the **software / device and version**.
- **A11-TOC-02** Normal APRS destinations that clients process: **`ALL`, `BEACON`, `CQ`, `QST`,
  `GPSxxx`, and `APxxxx`** (plus `SPCxyz`/`SYMxyz` symbol TOCALLs; see `symbols.md`).
  Mic-E packets use the destination for latitude data instead.
- **A11-TOC-03 ALTNET.** **Any** destination that is *not* one of the normal TOCALLs above is an
  ALTNET. Compliant clients **ignore ALTNET traffic by default** unless the operator selects that
  ALTNET (e.g. `TEST` for on-air testing without cluttering maps). Registered examples:
  `SATERN` (Salvation Army), `AFMARS` (Air Force MARS), `AMARS` (Army MARS).
- **A11-TOC-04** `APRS` (bare) is generic/obsolete; digis should use `APNxxx`.
- Some TOCALLs signal state rather than software: `APDIGI`/`APRSON`/`APSAT` = satellite digi ON,
  `APOFF` = digi OFF, `APDTMF` = digi off & DTMF on (PSAT/QIKCOM).
- Special APRS-1.2-proposal TOCALLs: `AP0Cxy` / `AP0Oxy` (object caching request/response, see
  `../aprs12/objects.md`), `APOBJ` (3rd-party permanent object, unused — see same file).

> The authoritative device-ID list today is community-maintained outside aprs.org
> (the `aprs-deviceid` project's `tocalls.yaml`, covering both TOCALLs and Mic-E codes). The snapshot
> below is from aprs.org's 2021 file; prefer the current YAML for implementation, and use this table
> as a fallback/sanity check.

## TOCALL prefix families (summary of `tocalls.txt`, 14 Dec 2021)

| Prefix | Family / notable entries |
|---|---|
| `AP1`,`AP4`,`APn` | numeric 3rd char: `AP1WWX` TAPR T-238+ WX; `AP4Rxy` APRS4R; `APnnnD`/`APnnnU` uSmartDigi |
| `APA` | AFilter (`APAFxx`), AGATE, AGWtracker (`APAGWx`), Alinco (`APALxx`), APRSdroid pre-release (`APAND1`), Altus Metrum (`APAMxx`), Anytone (`APAT8x`,`APAT51`), AP510 (`APAVT5`), AGWPE (`APAWxx`), ATA-R (`APATAR`) |
| `APB` | BeeLine (`APBLxx`), BPQ (`APBPQx`), BrandMeister (`APBMxx`), HamBSD (`APBSDx`), BTech (`APBT62`), radiosonde (`APB2MF`) |
| `APC` | Cellular apps (`APCxxx`), cubesats (`APCSS`), carNET (`APCNxx`) |
| `APD` | D-Star gateways (`APDGxx`,`APDKxx`), DIXPRS (`APDInn`), **APRSdroid (`APDRxx`)**, DF units (`APDFxx`), APRStt (`APDTxx`), **Dire Wolf (`APDWxx`)**, D-Star posits (`APDPRS`) |
| `APE` | Telemetry devices, balloon trackers |
| `APF`,`APG`,`APH` | Firenet/flood gauge (`APFGxx`), aprs.fi (`APFIxx`), gates, HamWAN (`APHWxx`) |
| `API` | ICOM radios (`API282`, `API31`, `API51`, `API510`, `API710`, `API880`, `API910`, `API92`, `API970`…), `APIxxx` = all D-Star ICOMs via DPRS |
| `APJ` | JS8Call (`APJ8xx`), Java clients, YAAC (`APJYnn`) |
| `APK` | **Kenwood**: `APK0xx` TH-D7, `APK003` TH-D72, `APK004` TH-D74, `APK1xx` D700, `APK102` D710 |
| `APL` | LoRa (`APLGxx`,`APLOxx`,`APLTxx`), LightAPRS (`APLIGx`) |
| `APM` | MacAPRS, PiCrumbs/MiniGate, WB2OSZ HamRadio-of-Things (`APMQxx`) |
| `APN` | Network nodes/digis: KPC-3 (`APN3xx`), KPC-9612 (`APN9xx`), DIGI_NED (`APNDxx`), UIdigi (`APNUxx`), TNC-X (`APNXxx`), JNOS (`APN2xx`) |
| `APO` | APRSpoint, OpenTracker (`APOTxx`), openSPOT (`APOSWx`,`APOSBx`), OpenAPRS (`APOAxx`) |
| `APP`,`APQ` | PicoAPRS (`APPICx`), python iGate (`APPMxx`), Earthquake data (`APQxxx`) |
| `APR` | APRSdos (`APR8xx`), **`APRS` generic/obsolete**, APRSmax / aprx (`APRXxx`), radiosonde (`APRARX`) |
| `APS` | APRS+SA, **aprsc server (`APSCxx`)**, SARTrack (`APSARx`) |
| `APT` | TinyTrak (`APT2xx`,`APT3xx`,`APTTxx`), TinyAPRS (`APTBxx`), WXTrac (`APTWxx`) |
| `APU` | UI-View (`APU1xx`,`APU2xx`) |
| `APV` | VoIP: IRLP (`APVRxx`), EchoLink (`APVExx`), I-LINK (`APVLxx`) |
| `APW` | WinAPRS, **APRSIS32 (`APWWxx`, `APWMxx`, Android `APWAxx`)** |
| `APX` | **Xastir (`APXnnn`)**, Xrouter (`APXRnn`) |
| `APY` | **Yaesu**: `APY008` VX-8, `APY01D` FT1D, `APY02D` FT2D, `APY03D` FT3D, `APY05D` FT5D, `APY100` FTM-100D, `APY300` FTM-300D, `APY350` FTM-350, `APY400` FTM-400D |
| `APZ` | **Experimental** (`APZxxx`); old Xastir `APZ0xx` |

Full list: `source/aprs.org/aprs11/tocalls.txt` (machine-friendly: lines under `<tocalls>` are
`[family] CODE description`; `x`/`n`/`y` in a code are wildcards).

## User-defined / experimental formats (`{` data type)

- **A11-EXP-01** The `{` user-defined DTI is for special-purpose experiments. Do **not** use it where
  broad receipt or backward compatibility matters (≈55 % of stations in 2004 ran fixed firmware);
  prefer message/status/position-comment encodings.
- Reserved headers (`{` + user ID + type), as of 17 Apr 2020:

| Header | Owner | Description |
|---|---|---|
| `{BT` | KB3UKS | BRICSAT telemetry |
| `{BP` | KB3UKS | BRICSAT pictures (jpg) |
| `{Dx` | WB2OSZ | Dire Wolf software TNC |
| `{Fnn` | WB4APR | MITEL GPS data formats (`nn` = 2-byte descriptor) |
| `{HT` | WB4APR | HAM-Trail reporting devices (tbd) |
| `{ID` | WB4APR | RFID tags |
| `{K1` | WB4APR | Keps, NASA one-line |
| `{K2n` | WB4APR | NASA two-line elements, `n` = line 1 or 2 |
| `{KY` | KI4YLJ | KYSAT (Univ. of Kentucky) |
| `{Q1` / `{Q2` | WB4APR | QIKcom-1 / QIKcom-2 data |
| `{Sx` | ZL4FOX | SARTrack protocols |
| `{TP` / `{TU` | WB4APR | PCSAT2 solar-panel telemetry |
| `{x1` / `{x2` | J Bennet | WX |

Also reserved elsewhere in 1.2: `{IP-` (IGate path report, `../aprs12/messaging-extensions.md`).
