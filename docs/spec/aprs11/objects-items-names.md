---
id: aprs11.objects
spec: APRS 1.1 Addendum
status: approved
topic: Objects, items, kill semantics, ownership, name/callsign equality, area objects
affects_aprs101_pages: [37, 57, 58, 59, 60]
sources:
  - https://www.aprs.org/aprs11/Objects.txt
  - https://www.aprs.org/aprs11/aprs-names-and-spaces.txt
  - https://www.aprs.org/aprs11/areaobjects.txt
  - source/aprs.org/aprs11/Objects.txt
  - source/aprs.org/aprs11/aprs-names-and-spaces.txt
  - source/aprs.org/aprs11/areaobjects.txt
see_also:
  - ../aprs12/objects.md          # permanent objects (111111z), object caching
  - ../aprs12/messaging-extensions.md  # ITEM-in-MSG, MESSAGE OBJECT
  - symbols.md                   # area object (\l) encoding
---

# APRS 1.1 — Objects, Items and Names

## Object = position report

- **A11-OBJ-01** An object is **100 % identical to a position report** after removing the
  9-char name (and the live/killed flag and timestamp). Parse the remainder with the normal
  position parser, so an object can carry anything a station can: weather, course/speed, DF, PHG, etc.
- **A11-OBJ-02** Weather data **may** be included in object and item packets (APRS101 omitted this).
- **A11-OBJ-03** The 9-char object name field is *not* restricted to callsign syntax: upper **and**
  lower case and all 9 characters are permitted.

## One namespace

- **A11-NAME-01** Stations, items, objects (and, per 2016 note, **message addressees**) share a
  **single name space**. A thing is identified by its name, regardless of which packet type carried it.
- **A11-NAME-02** Name normalisation:
  - no leading spaces;
  - **trailing spaces are not part of the name** (object names are right-padded to 9 bytes);
  - embedded spaces are allowed but discouraged;
  - item names drop the padding by definition;
  - the leading character of any name must be **alphanumeric** (WB4APR concurs with Steve Dimse).
- **A11-NAME-03** **Comparison is case-sensitive and exact.** `WB4APR` ≠ `wb4apr`; `LeadMan` ≠ `LEADman`.
  This is intentional: it lets users post alternative "opinions" of a thing without replacing it.
  Mixed-case callsign fields were intended to be allowed (2016 note).
- **A11-NAME-04** Object names should avoid punctuation that can't become a valid waypoint name on
  common GPS receivers.
- **A11-NAME-05** Station/item/object are interchangeable for update purposes when names match,
  but the **packet type should be retained for display** (immediate recognition of what it is).

## Ownership

- **A11-OWN-01** The **sending station (owner) must always be retained and shown** for every object.
  Ownership can change packet-by-packet when someone else transmits the same object name; that
  station takes over reporting responsibility.
- **A11-OWN-02** A station that is transmitting an object and hears the **same name** from a
  different station **ceases** its own transmissions of it (accepts the takeover).
- **A11-OWN-03** Display must distinguish *my* objects vs *other stations'* objects vs stations
  (APRSdos: own objects yellow, others purple).

## Killing

- **A11-KILL-01** A killed object (`_` flag) kills **any matching named** object, item **or station**.
- **A11-KILL-02** A kill is a *replacement* marked killed — it is **retained in lists** (for later
  reference) but not displayed (or displayed faintly). Nobody can make others' data vanish completely.
- **A11-KILL-03** Clients should only let the **current originator/owner** send a kill for an object.
  Anyone may first take over an object, and having taken it over, may then kill it
  (this is how dead objects get cleaned up).
- **A11-KILL-04** If the owner keeps transmitting after a kill, the object reappears (the new live
  copy overwrites the kill). This "dueling" is intentional so kills are hard to do by accident.

## Format recommendations

- **A11-OBJ-04** Compressed object format: **not recommended on RF** (incompatibilities). Decoders
  must still accept it.
- **A11-OBJ-05** Item format (`)`): **not recommended on RF**. Decoders must still accept it.
- **A11-OBJ-06** Polygon/line objects: see WXSVR multiline protocol (external).

## Area objects (`\l`)

See `symbols.md` → *Area objects* for the `lTyy/Cxx` field layout and the corrected rules
(A11-AREA-01..03): `offset_degrees = offset^2 / 1500`; transmitted position is the lower-right
(or circle-centre) point.

## Implementation checklist

- [ ] Station store keyed by exact, case-sensitive, trailing-space-trimmed name.
- [ ] Keep `Owner` (source callsign of last packet) and `Kind` (Station/Object/Item) on every entry.
- [ ] Kill = state flag, not deletion; retained in history.
- [ ] Object payload delegates to the same position/extension parser as `!`/`=`/`/`/`@`.
- [ ] When we originate an object and hear the same name from another station, stop transmitting it.
