# Security policy

## Reporting a vulnerability

Please do not open a public issue for a security problem. Report it privately through GitHub's
[private vulnerability reporting](https://github.com/APRS-NET/APRS.NET/security/advisories/new) on this repository, so
it can be fixed before it is made public.

Include what you can of:

- the package and version (or commit) affected;
- the input that triggers it, such as a packet line or a sequence of server responses;
- what happens, and what you expected.

## Supported versions

APRS.NET is in alpha (0.x). Fixes go into the next release only; earlier releases are not patched.

## Scope

This policy covers everything the repository ships:

- the `AprsNet.*` NuGet packages under `src/`;
- the `aprscli` tool (`apps/AprsNet.AprsIsCli`);
- the container image built from the `Dockerfile`;
- the Linux services in `apps/services`: the systemd units, `install.sh`, the helper scripts and the example
  configuration files.

Tests, benchmarks, samples and captures are not shipped and are out of scope.

### Who sends what

| Source | Trust |
| --- | --- |
| Packets heard on RF, through a TNC (KISS over TCP or serial, AGWPE) | Hostile: anyone with a radio |
| Lines from an APRS-IS server, and anything relayed through it | Hostile |
| Clients of the gateway (`aprscli serve`): APRS-IS clients and WebSocket clients | Hostile and unauthenticated |
| Apps connected to a shared TNC (`aprscli tnc share`) | Unauthenticated; trusted to transmit (see below) |
| GPS input (gpsd, NMEA) | Hostile as data; its position is trusted |
| The operator: the calling code, command-line options, config files, environment | Trusted |

### What is in scope

- A crash, hang or unbounded memory or CPU use caused by a packet, a server response, a KISS or AGWPE frame, an
  NMEA sentence, or a gateway client's login line, filter or HTTP/WebSocket request.
- Anything that sends packets the caller did not ask for, on RF or to APRS-IS. In particular, the IGate must only gate
  internet traffic to RF as its rules say (`InternetToRfGate`), and its rate limits must hold.
- Getting past a documented limit, such as the gateway's client limit, a client's queue length or a request size
  limit.
- Gateway clients doing anything other than reading the feed and setting their filter.
- The services or the container running with more privilege than they document, or making a file another local user
  can write.

### What is not in scope

- APRS itself: traffic on the air and on APRS-IS is unauthenticated, so anyone can send a packet as any callsign.
- Reading the gateway's feed. It serves public APRS traffic to anyone who can reach it, without a login, by design.
- APRS-IS passcodes. They are computed from the callsign with a public algorithm and are not secrets, so showing one
  in a log or an error is not a vulnerability.
- Anything that needs control of the operator's options, configuration, environment or machine.

## Known limitations

- **A shared TNC has no login.** Any app that can reach `aprscli tnc share`'s port can transmit through that TNC,
  under the operator's callsign. Only data frames with a valid AX.25 frame are taken from apps, and KISS commands that
  set TNC parameters are ignored, but there is no other check. The command listens on `127.0.0.1:8001` unless told
  otherwise; keep the port on a network you trust. Turning on `transmit-limit` caps what all apps send together.
  This is an accepted risk for now and is not reportable.
- **Gateway clients don't log in.** A passcode in a client's login line only marks the client as verified; it grants
  nothing, since clients cannot send. Keep the gateway's ports on a network you trust if what its TNCs hear should stay
  local.
