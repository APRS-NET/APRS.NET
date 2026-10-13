# AprsNet.AprsIsCli

The `aprscli` command-line tool for APRS.NET.

> **Alpha.** Like the rest of APRS.NET, the tool can change between any two releases until at least 1.0.0. Not for
> production use.

It is not published as a package yet. Run it from the repository root:

```sh
dotnet run --project apps/AprsNet.AprsIsCli -- listen --help
```

The examples below use `aprscli` for short.

## listen

Connect to APRS-IS and stream packets to stdout: raw lines by default, or decoded.

```sh
aprscli listen --region noam --filter "r/35/-97/200" -t
aprscli listen -c N0CALL --capture captures/noam.log -q -d 10m --reconnect
aprscli listen --full-feed -n 1000 > sample.txt
aprscli listen --filter "r/35/-97/200" --format decoded
aprscli listen --filter "r/35/-97/200" --format json | jq .position
aprscli listen --filter "r/35/-97/200" --format pretty
```

| Option | Meaning |
| --- | --- |
| `-c`, `--callsign` | Login callsign. Default `N0CALL`. Some servers refuse it; pass your own if so. |
| `--passcode` | Passcode, default `-1` (read-only). Prefer the `APRS_PASSCODE` environment variable: an option is visible in the process list and shell history. Never printed or logged. |
| `-s`, `--server host[:port]` | Server. Not combinable with `--region`. Default `rotate.aprs2.net:14580`. |
| `-r`, `--region` | `noam`, `soam`, `euro`, `asia` or `aunz` rotate. |
| `--port`, `--full-feed` | Port override, or the full feed port 10152. |
| `-f`, `--filter` | APRS-IS filter sent with the login. |
| `-t`, `--timestamps` | Prefix printed lines with a UTC timestamp (`raw` and `decoded`; `json` and `pretty` always show the received time). |
| `--format` | `raw` (default): the line as received. `decoded`: one readable line with the header and decoded fields. `json`: one JSON object per line with the raw line, header, outcome, issues and decoded fields (pipe it to `jq` to read it indented). `pretty`: a block per packet with the received time, the header and labelled fields in plain units, plus any parse issues. Position reports, status reports and messages (including telemetry definitions) are decoded so far; other types print as not decoded yet. |
| `--color` | Colour for `--format pretty`: `auto` (default; only on a terminal, and not when `NO_COLOR` is set or `TERM=dumb`), `always` (for IDE consoles that show ANSI colours) or `never`. |
| `--capture <file>` | Append `<UTC timestamp> <raw line>` for every packet, whatever `--format` says. |
| `-q`, `--quiet` | No packet output on the console. |
| `--log-packets` | Also log every packet to OpenTelemetry (never the console). Needs `OTEL_EXPORTER_OTLP_ENDPOINT`. |
| `-n`, `--count` / `-d`, `--duration` | Stop after N packets / after `90s`, `10m`, `2h`, `1d`. Ctrl+C also stops cleanly. |
| `--reconnect` | If the connection drops or the server sends nothing for 90 s, reconnect (wait 5 s, doubling to 60 s) and carry on; `--count` spans connections. Only after the first login succeeds, and never after a rejected login. Use it for long captures. |
| `--max-silence <duration\|off>` | Treat the connection as failed after this long with **no packets**, even if keepalives continue (the server's own feed can stall while it still answers). Default `10m` with `--reconnect`, otherwise off. Raise it or use `off` for filters over quiet areas. |

APRS-IS servers send a `#` keepalive about every 20 s, so 90 s of silence means the link is dead even if TCP has not noticed. A server can also keep sending keepalives after its feed has stalled; `--max-silence` catches that, and its message reports how many keepalives arrived in the gap so you can tell the two apart. Without `--reconnect` either one ends the run with exit code 2.

Packets go to stdout and logs and errors to stderr, so output pipes cleanly. Control characters in packets are escaped on the console (`\x1B`); capture files keep every byte.

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Finished cleanly: Ctrl+C, `--count` or `--duration` reached |
| 1 | Unexpected error |
| 2 | Connection failed, dropped or went silent (without `--reconnect`) |
| 3 | Usage error |
| 4 | Login rejected or timed out |

### OpenTelemetry

Set `OTEL_EXPORTER_OTLP_ENDPOINT` (plus the usual `OTEL_EXPORTER_OTLP_*` variables, such as the protocol and headers) to export over OTLP, with service name `aprscli`:

- **Logs:** connection, login and filter events. Add `--log-packets` to log every received packet as `Packet from {Source}: {Packet}` (control characters escaped). Packet logs go to OpenTelemetry only, so they never appear on the terminal.
- **Metrics:** the client's `AprsNet.*` meters (lines received, connection attempts, login results, drops), flushed when the command exits.

<sub>Developed with AI assistance; see the [AI declaration](https://github.com/APRS-NET/APRS.NET/blob/main/AI-DECLARATION.md).</sub>
