# APRS specification reference

| Document | Form here | Status |
|---|---|---|
| **APRS Protocol Reference 1.0.1** (29 Aug 2000) | [`APRS101.PDF`](APRS101.PDF) | Base spec |
| **APRS 1.1 Addendum** (frozen Jul 2004) | [`aprs11/`](aprs11/README.md) — markdown summaries | Approved by APRS-WG |
| **APRS 1.2 Addendum** (living draft) | [`aprs12/`](aprs12/README.md) — markdown summaries | Proposals; many de-facto standard |
| Original source notes | [`source/`](source/) | Mirrored from aprs.org (+ he.fi) |

## Why markdown instead of PDFs

Neither addendum has ever been published as a PDF. Both exist only as HTML index pages on
aprs.org (`aprs11.html`, `aprs12.html`) that link to dozens of short `.txt` notes. There is also no
separate "1.2c" document: `aprs12.html` is the single 1.2 draft. (One file calls its weather changes
"1.2.1".) The 2022 aprssig "APRS roadmap" thread proposed consolidating these into a maintained
document, but nothing in that thread is a replacement spec. So:

- `aprs11/` and `aprs12/` hold **structured summaries** for implementing APRS.NET.
- `source/` holds the **original notes** they were built from, for checking details.

aprs.org is currently behind a Cloudflare bot challenge, so everything was fetched from the
Internet Archive's Wayback Machine (snapshots from 2024–2026) on 2026-10-11.

## Summary file conventions (for later parsing)

Each summary file starts with YAML front matter:

```yaml
id: aprs12.dao                 # stable identifier
spec: APRS 1.2 Addendum ...    # which addendum
status: ...                    # approved | proposal | de-facto standard | deprecated | informational
topic: ...
affects_aprs101_pages: [...]   # pages of APRS101.PDF amended (when applicable)
sources: [...]                 # canonical URLs + local mirror paths
see_also: [...]
```

Body conventions:

- Each rule has a stable ID: `A11-<AREA>-nn` / `A12-<AREA>-nn` (e.g. `A11-RACK-03`, `A12-DAO-01`).
  Cite them in code comments and test names.
- Wire formats are in fenced code blocks; field tables use `| Field | Meaning |`.
- Most files have a **Test vectors** section with inputs and expected decoded values, ready to turn
  into unit tests.
- Text marked *implementation guidance* or *implementation choice* is my own suggestion and is
  **not** from the source documents.
- `aprs12/open-issues.md` lists unresolved spec questions and contradictions between source files.

## Source mirror notes

- `.txt` files are verbatim (CRLF → LF).
- `.html.md` files are HTML pages converted with `html2text`. Lines linking to spam domains that were
  injected into the live aprs.org pages have been removed. Images are not mirrored.
- Not available from the archive: `aprs.org/specfuture.html` (the APRS 1.3 ideas page).
- External specs referenced but not mirrored: aprs-is.net q-construct (`q.htm`), WXSVR multiline
  protocol, TAPR AX.25 2.2. The community-maintained device-ID list (`aprs-deviceid` /
  `tocalls.yaml`) supersedes `source/aprs.org/aprs11/tocalls.txt` and `aprs12/mic-e-types.txt`
  for device identification.
