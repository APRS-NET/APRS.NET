# AprsNet.AprsIsCli

The `aprscli` command-line tool for APRS.NET.

## listen

Connect to APRS-IS and stream raw packet lines to stdout.

```sh
aprscli listen --region noam --filter "r/35/-97/200" -t
aprscli listen -c N0CALL --capture captures/noam.log -q -d 10m
aprscli listen --full-feed -n 1000 > sample.txt
```

| Option | Meaning |
| --- | --- |
| `-c`, `--callsign` | Login callsign. Default `N0CALL`. Some servers refuse it; pass your own if so. |
| `--passcode` | Passcode, default `-1` (read-only). Prefer the `APRS_PASSCODE` environment variable: an option is visible in the process list and shell history. Never printed or logged. |
| `-s`, `--server host[:port]` | Server. Not combinable with `--region`. Default `rotate.aprs2.net:14580`. |
| `-r`, `--region` | `noam`, `soam`, `euro`, `asia` or `aunz` rotate. |
| `--port`, `--full-feed` | Port override, or the full feed port 10152. |
| `-f`, `--filter` | APRS-IS filter sent with the login. |
| `-t`, `--timestamps` | Prefix printed lines with a UTC timestamp. |
| `--capture <file>` | Append `<UTC timestamp> <raw line>` for every packet. |
| `-q`, `--quiet` | No packet output on the console. |
| `--log-packets` | Also log every packet to OpenTelemetry (never the console). Needs `OTEL_EXPORTER_OTLP_ENDPOINT`. |
| `-n`, `--count` / `-d`, `--duration` | Stop after N packets / after `90s`, `10m`, `2h`, `1d`. Ctrl+C also stops cleanly. |

Packets go to stdout and logs and errors to stderr, so output pipes cleanly. Control characters in packets are escaped on the console (`\x1B`); capture files keep every byte.

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Finished cleanly: Ctrl+C, `--count` or `--duration` reached |
| 1 | Unexpected error |
| 2 | Connection failed or dropped |
| 3 | Usage error |
| 4 | Login rejected or timed out |

### OpenTelemetry

Set `OTEL_EXPORTER_OTLP_ENDPOINT` (plus the usual `OTEL_EXPORTER_OTLP_*` variables, such as the protocol and headers) to export over OTLP, with service name `aprscli`:

- **Logs:** connection, login and filter events. Add `--log-packets` to log every received packet as `Packet from {Source}: {Packet}` (control characters escaped). Packet logs go to OpenTelemetry only, so they never appear on the terminal.
- **Metrics:** the client's `AprsNet.*` meters (lines received, connection attempts, login results, drops), flushed when the command exits.
