# AprsNet.AprsIsCli

The `aprscli` command-line tool for APRS.NET.

> **Alpha.** Like the rest of APRS.NET, the tool can change between any two releases until at least 1.0.0. Not for
> production use.

Install it as a .NET tool (it needs the .NET 10 runtime):

```sh
dotnet tool install --global AprsNet.AprsIsCli --prerelease
aprscli listen --help
```

Or run it from a clone of the repository with `dotnet run --project apps/AprsNet.AprsIsCli -- listen --help`.

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
| `--tls` | Connect over TLS, checking the server's certificate. Default server `ssl.aprs2.net:24580`; a `--server` without a port uses 24580. Not with `--region` or `--full-feed`. |
| `-f`, `--filter` | APRS-IS filter sent with the login. |
| `-t`, `--timestamps` | Prefix printed lines with a UTC timestamp (`raw` and `decoded`; `json` and `pretty` always show the received time). |
| `--format` | `raw` (default): the line as received. `decoded`: one readable line with the header and decoded fields. `json`: one JSON object per line with the raw line, header, outcome, issues and decoded fields, in the format [`AprsNet.Packets.Json`](../../docs/json.md) documents (pipe it to `jq` to read it indented). `pretty`: a block per packet with the received time, the header and labelled fields in plain units, plus any parse issues. Position reports, Mic-E reports, objects, items, status reports, messages (including telemetry definitions), telemetry reports and weather are decoded so far; other types print as not decoded yet. |
| `--color` | Colour for `--format pretty`: `auto` (default; only on a terminal, and not when `NO_COLOR` is set or `TERM=dumb`), `always` (for IDE consoles that show ANSI colours) or `never`. |
| `--capture <file>` | Append `<UTC timestamp> <raw line>` for every packet, whatever `--format` says. |
| `-q`, `--quiet` | No packet output on the console. |
| `--log-packets` | Also log every packet to OpenTelemetry (never the console). Needs `OTEL_EXPORTER_OTLP_ENDPOINT`. |
| `-n`, `--count` / `-d`, `--duration` | Stop after N packets / after `90s`, `10m`, `2h`, `1d`. Ctrl+C also stops cleanly. |
| `--reconnect` | If the connection drops or the server sends nothing for 90 s, reconnect (wait about 5 s, doubling to 60 s, varied by up to 20% so dropped clients do not all return at once) and carry on; `--count` spans connections. Only after the first login succeeds, and never after a rejected login. Use it for long captures. |
| `--max-silence <duration\|off>` | Treat the connection as failed after this long with **no packets**, even if keepalives continue (the server's own feed can stall while it still answers). Default `10m` with `--reconnect`, otherwise off. Raise it or use `off` for filters over quiet areas. |
| `--stats <duration>` | Every so often (`1m`, `10m`) and at the end, print a summary to stderr: packets by type, how many were malformed or unsupported, and messages to your callsign. Handy during long captures. |

APRS-IS servers send a `#` keepalive about every 20 s, so 90 s of silence means the link is dead even if TCP has not noticed. A server can also keep sending keepalives after its feed has stalled; `--max-silence` catches that, and its message reports how many keepalives arrived in the gap so you can tell the two apart. Without `--reconnect` either one ends the run with exit code 2.

Packets go to stdout and logs and errors to stderr, so output pipes cleanly. Control characters in packets are escaped on the console (`\x1B`); capture files keep every byte.

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Finished cleanly: Ctrl+C, `--count` or `--duration` reached |
| 1 | Unexpected error |
| 2 | Connection failed, dropped or went silent (without `--reconnect`) |
| 3 | Usage error |
| 4 | Login rejected or timed out; for `send`, the passcode was not accepted |
| 5 | `send message`: rejected or never acknowledged |

### OpenTelemetry

Set `OTEL_EXPORTER_OTLP_ENDPOINT` (plus the usual `OTEL_EXPORTER_OTLP_*` variables, such as the protocol and headers) to export over OTLP, with service name `aprscli`:

- **Logs:** connection, login and filter events. Add `--log-packets` to log every received packet as `Packet from {Source}: {Packet}` (control characters escaped). Packet logs go to OpenTelemetry only, so they never appear on the terminal.
- **Metrics:** the client's `AprsNet.*` meters (lines received, connection attempts, login results, drops), flushed when the command exits.

## parse

Decode packets and print their fields. Give packets as arguments (quote each one), or read lines from `--file` or
standard input. Capture files from `listen --capture` work as input, and their timestamps become the received time.

```sh
aprscli parse 'N0CALL-9>APRS,WIDE1-1:=4903.50N/07201.75W>088/036 Hi'
aprscli parse --format decoded -i captures/noam.log
aprscli listen -n 20 | aprscli parse --format json | jq .type
```

| Option | Meaning |
| --- | --- |
| `-i`, `--file` | Read packets from a file, one per line. Not combinable with packet arguments. |
| `--format` | `pretty` (default), `decoded` or `json`, as for `listen`. |
| `--color` | `auto` (default), `always` or `never`, for `--format pretty`. |

Arguments are text, so they are parsed as their UTF-8 bytes; files and standard input are read byte for byte
(Latin-1), exactly like `listen`. Server `#` comments and blank lines in files are skipped. With no packets and no
`--file`, `parse` reads standard input until it closes, and refuses to wait if standard input is a terminal.

## replay

Run captured lines through the parser and report what it made of them: packets by outcome and type, every parse
issue with its description, and the data types that are not decoded yet, each with example lines and where they came
from (`file:line`). Use it on new captures to find parser gaps.

```sh
aprscli replay captures/*.log
aprscli replay tests/corpus/aprs-is/*.log --examples 1
aprscli replay captures/noam.log --format json | jq '.undecoded[] | {identifier, packets}'
cat captures/noam.log | aprscli replay -
```

| Option | Meaning |
| --- | --- |
| `<files>` | One or more capture files or files of raw lines; `-` reads standard input. All files are checked before any is read. |
| `-e`, `--examples` | Example lines per issue and per undecoded data type. Default 3; `0` hides them. |
| `--format` | `text` (default) or `json`. |

Server `#` comments and blank lines are counted and skipped. Undecoded packets are grouped by their data type
identifier (the first byte of the information field, in hex when unprintable).

## passcode

Print the APRS-IS passcode for a callsign: the number a verified login sends instead of `-1`. The SSID and letter
case do not change it. Only the number goes to stdout, so a script can capture it:

```sh
aprscli passcode N0CALL          # 13023
export APRS_PASSCODE=$(aprscli passcode N0CALL)
```

The passcode is a public hash of the callsign, not a secret; it only stops people from transmitting by mistake. Use
it only for your own licensed callsign. An invalid callsign exits with code 3.

## send

Send a status report, message, position, object, weather or telemetry report to APRS-IS as your own callsign. Sending needs a
verified login: set `APRS_PASSCODE` (or `--passcode`) to the passcode for the callsign. `--dry-run` prints the packet
without connecting, so you can check it before it goes out:

```sh
export APRS_PASSCODE=$(aprscli passcode KD9XYZ)
aprscli send status "Testing APRS.NET" -c KD9XYZ-9 --dry-run   # KD9XYZ-9>APZNET:>Testing APRS.NET
aprscli send status "Testing APRS.NET" -c KD9XYZ-9
aprscli send position --lat 49.05833 --lon -72.02917 --symbol "/>" --comment "On the road" -c KD9XYZ-9
aprscli send object LEADER --lat 49.05833 --lon -72.02917 -c KD9XYZ-9          # --kill removes it
aprscli send message WU2Z "Hello there" -c KD9XYZ-9
aprscli send weather --lat 49.05833 --lon -72.02917 --wind-dir 220 --wind-speed 4 --temp 77 --humidity 50 -c KD9XYZ-9
```

`weather` takes the readings in APRS's units: `--wind-dir` (degrees), `--wind-speed` and `--gust` (mph), `--temp`
(°F), `--rain-1h`, `--rain-24h`, `--rain-midnight` and `--snow` (inches), `--humidity` (%), `--pressure` (mb) and
`--luminosity` (W/m²). With `--lat` and `--lon` it sends a complete report on the weather station symbol (`/_`);
without them a positionless report timestamped now, which cannot carry snowfall. Wind, gust and temperature left out
are sent as unknown (dots); other readings are left out.

`telemetry` sends a `T#` report: `--seq` (0–999) and `--analog` (one to five whole numbers 0–999), with optional
`--digital` (eight `0`/`1`, after all five analog values) and `--comment`. `--names`, `--units`, `--equations`
(`a,b,c` per channel, `;` between channels) and `--bits` with `--project` also send the `PARM.`, `UNIT.`, `EQNS.` and
`BITS.` definitions, addressed to your own callsign, before the report:

```sh
aprscli send telemetry -c KD9XYZ-11 --seq 5 --analog 199,0,255,73,123 --digital 01101001 \
  --names Battery,Btemp,ATemp,Pres,Alt --equations "0,5.2,0;0,.53,-32;3,4.39,49;-32,3,18;1,2,3"
```

`message` waits for the recipient's acknowledgement, resending after 30 s, 1, 2, 4 and 8 minutes. It then keeps
listening for `--wait-replies` (default 2m; `0` stops at once), acknowledging and printing each reply
(`Reply from WU2Z: Received`), so the other station is not left resending its answer. It exits 0 when the message
was acknowledged and 5 when it was rejected or never acknowledged. If the connection drops meanwhile, `send`
reconnects as `listen --reconnect` does and carries on waiting.

| Option | Meaning |
| --- | --- |
| `-c`, `--callsign` | Your callsign, with optional SSID. Required; `N0CALL` is refused. |
| `--passcode` | Passcode for the callsign; prefer `APRS_PASSCODE`. |
| `-s`, `--server`, `-r`, `--region` | Server, as for `listen`. Default: `rotate.aprs2.net:14580`. |
| `--tls` | Connect over TLS, as for `listen`. |
| `--tocall` | Destination TOCALL. Default: `APZNET` (the experimental `APZ` range). |
| `--path` | Digipeater path, comma-separated. Default: none, for APRS-IS only. |
| `-n`, `--dry-run` | Print the packet without connecting. |

The client spreads sends out (5 at once, then one every 6 seconds) and refuses to send on an unverified login, since
the server would drop the packet. Exit codes are those of `listen`, plus 5 for an undelivered message.

## serve

Relay an APRS-IS feed, what your KISS TNCs hear, or both, to APRS clients on this computer or your network. `serve`
makes one read-only connection upstream with a broad filter, reads each `--tnc`, drops duplicates (a packet heard both
on the air and on APRS-IS is served once), and serves the packets on a local port that behaves like an APRS-IS server:
each client logs in as usual and gets its own filter, plus messages to its callsign.

```sh
aprscli serve -c KD9XYZ -f "r/38.98/-94.67/300" --history 10m          # on 127.0.0.1:14580
aprscli serve -c KD9XYZ -f "r/38.98/-94.67/300" --listen 0.0.0.0:14580 # for your network
aprscli serve -f "r/38.98/-94.67/300" --tnc localhost                  # APRS-IS and Dire Wolf
aprscli serve --tnc localhost --tnc 192.168.1.20:8001                   # two TNCs, no internet
aprscli serve -f "r/38.98/-94.67/300" --tnc serial:/dev/ttyUSB0         # APRS-IS and a USB TNC
aprscli serve -f "r/38.98/-94.67/300" --http 127.0.0.1:14590           # also WebSocket and /health
aprscli listen --server 127.0.0.1:14580 -f "r/38.98/-94.67/50 t/w"     # a client
```

| Option | Meaning |
| --- | --- |
| `-c`, `--callsign` | Callsign for the upstream login. Default: `N0CALL`. |
| `-s`, `--server` | Upstream server as `host[:port]`. Default: `rotate.aprs2.net:14580`. |
| `-f`, `--filter` | Upstream filter: everything clients can ask for. Without it, APRS-IS is not used and only `--tnc` sources are served. |
| `-t`, `--tnc` | A KISS TNC to serve, as `host[:port]` (default port 8001, Dire Wolf's) or `serial:PORT[@BAUD]` (default 9600 baud). Repeat for several. A lost or unplugged TNC is reconnected. |
| `-l`, `--listen` | Address and port to serve on. Default: `127.0.0.1:14580`. |
| `--http` | Also serve HTTP on this `address:port`. WebSocket clients connect to `ws://address:port/aprs?filter=…`, get packets as [JSON](../../docs/json.md) and can change their filter by sending `{"filter":"…"}`. `GET /health` answers 200 with each source's state, or 503 when no source is connected. Default: off. |
| `--name` | Server name clients see. Default: `APRSNET`. |
| `--history` | Send each client the packets from this long before it connected. Default: off. |
| `-d`, `--duration` | Stop after this long. Default: until Ctrl+C. |

The upstream connection and each TNC always reconnect. When there is more than one source, the summary at the end
counts each one's packets and duplicates. Clients are read-only for now: anything they send is ignored. A client
that falls 1,000 lines behind is disconnected, as on APRS-IS.

## tnc listen

Print the APRS packets a KISS TNC hears, in the same formats as `listen`. The TNC can serve KISS over TCP, as Dire
Wolf does on port 8001, or sit on a serial port, such as a USB TNC, given as `serial:PORT[@BAUD]` (default 9600 baud;
on Linux, prefer the port's stable name under `/dev/serial/by-id/`):

```sh
aprscli tnc listen --tnc localhost:8001 --format decoded
aprscli tnc listen --tnc 192.168.1.20 --radio-port 1 -d 1h
aprscli tnc listen --tnc localhost --reconnect      # survives Dire Wolf restarts
aprscli tnc listen --tnc serial:/dev/ttyUSB0 --reconnect     # a USB TNC; survives unplugging it
aprscli tnc listen --tnc serial:COM3@57600          # a NinoTNC on Windows
aprscli tnc ports                                   # the serial ports present
```

`--radio-port` picks the port on a multi-port TNC (default 0); `-n` and `-d` stop after a count or a time. Exit code 2
when the TNC cannot be reached or closes the connection, unless `--reconnect` is given: then the command keeps trying
(waiting 5 s, doubling to 60 s; 2 s to 30 s for a serial port), says so on stderr, and carries on when the TNC is back.
`aprscli tnc ports` lists the serial ports present.

## tnc send

Transmit on the air through a KISS TNC: the same `status`, `message`, `position`, `object`, `weather` and `telemetry`
commands as `send`, built the same way. You need a licence to transmit, and to send as your own callsign. Check the
packet with `--dry-run` (`-n`) first: it prints what would go out without connecting.

```sh
aprscli tnc send status "On the air" -c KD9XYZ-9 --tnc localhost -n      # check it
aprscli tnc send status "On the air" -c KD9XYZ-9 --tnc localhost         # transmit it
aprscli tnc send position --lat 38.98 --lon -94.67 --symbol "/>" -c KD9XYZ-9 --tnc localhost --path WIDE1-1
```

| Option | Meaning |
| --- | --- |
| `-c`, `--callsign` | Your callsign, with optional SSID. It must fit AX.25: up to six letters or digits and an SSID of 0 to 15. Required. |
| `-t`, `--tnc` | The TNC as `host[:port]` (default port 8001) or `serial:PORT[@BAUD]` (default 9600 baud). Required unless `--dry-run`. |
| `--radio-port` | The radio port on a multi-port TNC, 0 to 15. Default 0. |
| `--path` | Digipeater path. Default `WIDE1-1,WIDE2-1`; `--path ""` for none. |
| `--tocall` | Destination TOCALL. Default `APZNET` (experimental). |
| `-n`, `--dry-run` | Print the packets without connecting. |

Each packet is printed once the TNC has it. Frames are spread out by the transmit limit: three at once, then one every
10 seconds, so telemetry with its definitions takes a few seconds. A message is sent once, without a message number,
so no acknowledgement comes back. Exit code 2 when the TNC cannot be reached.

<sub>Developed with AI assistance; see the [AI declaration](https://github.com/APRS-NET/APRS.NET/blob/main/AI-DECLARATION.md).</sub>
