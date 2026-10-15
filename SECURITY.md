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

The parser treats every packet as hostile input, so a crash, hang, or unbounded memory use caused by a packet, a
server response or a KISS frame is in scope, as is anything that sends packets the caller did not ask for. Problems in
APRS itself, such as unauthenticated traffic on the air, are not.
