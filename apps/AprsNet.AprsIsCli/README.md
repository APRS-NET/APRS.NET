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
| `-c`, `--callsign` | Login callsign. Default: a listener ID such as `SWL482913`, which logs in to receive only, for anyone without a licence. |
| `--passcode` | Passcode, default `-1` (read-only). Prefer the `APRS_PASSCODE` environment variable: an option is visible in the process list and shell history. Never printed or logged. |
| `-s`, `--server host[:port]` | Server. Not combinable with `--region`. Default `rotate.aprs2.net:14580`. |
| `-r`, `--region` | `noam`, `soam`, `euro`, `asia` or `aunz` rotate. |
| `--port`, `--full-feed` | Port override, or the full feed port 10152. |
| `--tls` | Connect over TLS, checking the server's certificate. Default server `ssl.aprs2.net:24580`; a `--server` without a port uses 24580. Not with `--region` or `--full-feed`. |
| `-f`, `--filter` | APRS-IS filter sent with the login. |
| `-t`, `--timestamps` | Prefix printed lines with a UTC timestamp (`raw` and `decoded`; `json` and `pretty` always show the received time). |
| `--format` | `raw` (default): the line as received. `decoded`: one readable line with the header and decoded fields. `json`: one JSON object per line with the raw line, header, outcome, issues and decoded fields, in the format [`AprsNet.Packets.Json`](../../docs/json.md) documents (pipe it to `jq` to read it indented). `pretty`: a block per packet with the received time, the header and labelled fields in plain units, plus any parse issues. Position reports, Mic-E reports, objects, items, status reports, messages (including telemetry definitions), telemetry reports, weather, station capabilities, user-defined data and raw NMEA (`$GPRMC` …) are decoded so far; other types print as not decoded yet. |
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

`message` sends through `MessagingStation` (`AprsNet.Messaging`) and waits for the recipient's acknowledgement,
resending on the `--retry` schedule: `default` (after 30 s, 1, 2, 4 and 8 minutes), `aprs-is` (30 s, 2 and 5
minutes), `slow-hf` (2, 4, 8 and 16 minutes) or `none`. The message ID is in the APRS 1.1 reply-ack form (`{01}`), so a
reply that carries the ack (`{7}01`) counts as one. It then keeps listening for `--wait-replies` (default 2m; `0`
stops at once), acknowledging and printing each reply (`Reply from WU2Z: Received`), so the other station is not left
resending its answer. It exits 0 when the message was acknowledged and 5 when it was rejected or never acknowledged.
If the connection drops meanwhile, `send` reconnects as `listen --reconnect` does and carries on waiting.

`--show-received` also prints every packet received while the message runs, as `< ` and the packet, so you can see
what a service answers. Add `--filter` to see more than what is addressed to you: its terms go after `g/<callsign>`.
To watch ANSRVR's notices and relays while joining a group:

```sh
aprscli send message ANSRVR "J NZEVENT" -c KD9XYZ-9 --show-received --filter b/ANSRVR --wait-replies 5m
```

`beacon` keeps sending a position, and with `--status` a status report, on a schedule from `AprsNet.Beacons`, printing
each packet with the time it went:

```sh
aprscli send beacon --lat 49.05833 --lon -72.02917 --symbol "/-" --comment "Home" -c KD9XYZ-9   # every 30m
aprscli send beacon --lat 49.05833 --lon -72.02917 -c KD9XYZ-9 --every 10m --count 6
aprscli send beacon --lat 49.05833 --lon -72.02917 -c KD9XYZ-9 --decay 1m --every 30m      # 1, 2, 4 … 30 minutes
```

The first beacon goes at once (APRS-IS costs no airtime). `--every` sets the interval, 1m or more (default 30m); with
`--decay` the wait starts at that and doubles after each beacon up to `--every`. It runs until `--count` beacons,
`--duration` or Ctrl+C, reconnecting if the connection drops, and exits 0. It is for APRS-IS only; `tnc send` has no
`beacon`.

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
APRS_PASSCODE=12345 aprscli serve -c KD9XYZ-10 --tnc localhost --igate --lat 49.0583 --lon -72.0292
                                                                         # a receive-only IGate
```

| Option | Meaning |
| --- | --- |
| `-c`, `--callsign` | Callsign for the upstream login. Default: a listener ID such as `SWL482913` (receive only). |
| `-s`, `--server` | Upstream server as `host[:port]`. Default: `rotate.aprs2.net:14580`. |
| `-f`, `--filter` | Upstream filter: everything clients can ask for. Without it, APRS-IS is not used and only `--tnc` sources are served. |
| `-t`, `--tnc` | A KISS TNC to serve, as `host[:port]` (default port 8001, Dire Wolf's) or `serial:PORT[@BAUD][,dtr=off][,rts=off]` (default 9600 baud). Repeat for several. A lost or unplugged TNC is reconnected. |
| `-l`, `--listen` | Address and port to serve on. Default: `127.0.0.1:14580`. |
| `--http` | Also serve HTTP on this `address:port`. WebSocket clients connect to `ws://address:port/aprs?filter=…`, get packets as [JSON](../../docs/json.md) and can change their filter by sending `{"filter":"…"}`. `GET /health` answers 200 with each source's state, or 503 when no source is connected. Default: off. |
| `--name` | Server name clients see. Default: `APRSNET`. |
| `--history` | Send each client the packets from this long before it connected. Default: off. |
| `-d`, `--duration` | Stop after this long. Default: until Ctrl+C. |
| `--igate` | Also gate what the `--tnc` sources hear to APRS-IS as a receive-only IGate (below). Needs `-c`, its passcode in `APRS_PASSCODE` (or `--passcode`), `--lat` and `--lon`. |
| `--lat`, `--lon` | With `--igate`: the IGate's position, which it beacons to APRS-IS. |
| `--symbol` | With `--igate`: the beacon's symbol. Default: `R&`, a receive-only IGate. |
| `--comment` | With `--igate`: text after `RX-only IGate` in the beacon's comment. |
| `--beacon-interval` | With `--igate`: how often to beacon, at least `1m`. Default: `30m`. |
| `--quiet-warning` | Warn when a TNC sends nothing at all for this long, such as `10m`: it may be switched off behind a USB port that stays open. `/health` shows it as `"quiet": true` while it lasts. Default: off. A TNC sending text instead of KISS frames shows as `"protocolMismatch": true` and doesn't count as up. |
| `--config` | Read options from a TOML file, as the [services](../services/README.md) do. See [Config files](#config-files). |
| `--check` | Check the options and any `--config` file, say what would be served, and exit without connecting. |

The upstream connection and each TNC always reconnect. A TNC sending text instead of KISS frames (not in KISS mode) is
reported on stderr. When there is more than one source, the summary at the end
counts each one's packets and duplicates. Clients are read-only for now: anything they send is ignored. A client
that falls 1,000 lines behind is disconnected, as on APRS-IS.

### A receive-only IGate

With `--igate`, `serve` also uploads what its TNCs hear to APRS-IS, following the aprs-is.net IGate rules (see
[AprsNet.IGate](../../src/AprsNet.IGate/README.md)): each packet once, with its path as heard and `qAO` plus your
callsign after it, and none with `TCPIP`, `TCPXX`, `NOGATE` or `RFONLY` in the path, no general queries, nothing
that came from APRS-IS. It needs a verified login: it stops at once if the server doesn't accept the passcode. It
beacons its position to APRS-IS when it starts and every `--beacon-interval`, with an `R&` symbol and a comment
that says it is receive-only. It never transmits; a `-f` filter is optional and only feeds local clients.

Receive-only IGates break messaging for stations nearby: they see their messages reach APRS-IS and expect replies,
which never come back on RF. Run one where no two-way IGate covers the area, or only for a while, and say so in the
comment. The [gateway guide](../../docs/guides/gateway.md#gate-to-aprs-is) says more.

The summary at the end, and `/health` with `--http`, count what was gated, sent, dropped and refused, by reason
(`"igate.refused.duplicate"` and so on); so do the `AprsNet.IGate` metrics.

## health

Ask a running `aprscli serve --http` whether it is serving, as the container image's `HEALTHCHECK` does. It prints the
health report and exits 0 while any source is connected, 1 when none is, and 2 when nothing answers:

```sh
aprscli health                                       # http://127.0.0.1:14590/health
aprscli health http://gateway.lan:14590/health --timeout 10s
```

## tnc listen

Print the APRS packets a KISS TNC hears, in the same formats as `listen`. The TNC can serve KISS over TCP, as Dire
Wolf does on port 8001, or sit on a serial port, such as a USB TNC, given as `serial:PORT[@BAUD]` (default 9600 baud;
on Linux, prefer the port's stable name under `/dev/serial/by-id/`). DTR and RTS are raised; add `,dtr=off` or
`,rts=off` for a board that resets on them, such as an ESP32 TNC:

```sh
aprscli tnc listen --tnc localhost:8001 --format decoded
aprscli tnc listen --tnc 192.168.1.20 --radio-port 1 -d 1h
aprscli tnc listen --tnc localhost --reconnect      # survives Dire Wolf restarts
aprscli tnc listen --tnc serial:/dev/ttyUSB0@115200,dtr=off,rts=off   # an ESP32 TNC
aprscli tnc listen --tnc serial:/dev/ttyUSB0 --reconnect     # a USB TNC; survives unplugging it
aprscli tnc listen --tnc serial:COM3@57600          # a NinoTNC on Windows
aprscli tnc ports                                   # the serial ports present
aprscli tnc listen --tnc serial:/dev/ttyUSB0,setup=th-d72 --reconnect   # a TH-D72A, switched into KISS
aprscli tnc listen --tnc serial:/dev/ttyUSB0,text --reconnect   # a TH-D72A in APRS mode, read as text
```

A radio's built-in TNC has to be told to speak KISS. Add `,setup=th-d72` (a Kenwood TH-D72, in PACKET12 mode) and every
TNC command switches it into KISS when it opens the port, does so again when the radio starts sending text after a
power cut, and switches it back out when it closes. Without a setup, the TNC must already be in KISS mode. Add `,text`
instead to read a radio in its APRS mode, which prints each packet as a line of text: receive only, with no setup;
`tnc send` refuses it.

`--radio-port` picks the port on a multi-port TNC (default 0); `-n` and `-d` stop after a count or a time. Exit code 2
when the TNC cannot be reached or closes the connection, unless `--reconnect` is given: then the command keeps trying
(waiting 5 s, doubling to 60 s; 2 s to 30 s for a serial port), says so on stderr, and carries on when the TNC is back.
`aprscli tnc ports` lists the serial ports present. A TNC sending text instead of KISS frames, because it isn't in KISS
mode, is reported on stderr. With `--reconnect`, `--quiet-warning 10m` also warns when the TNC sends nothing at all for
that long, as a radio's built-in TNC does when switched off behind a USB port that stays open; see the
[TNC guide](../../docs/guides/tnc.md#a-radios-built-in-tnc).

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
| `-t`, `--tnc` | The TNC as `host[:port]` (default port 8001) or `serial:PORT[@BAUD][,dtr=off][,rts=off]` (default 9600 baud). Required unless `--dry-run`. |
| `--radio-port` | The radio port on a multi-port TNC, 0 to 15. Default 0. |
| `--path` | Digipeater path. Default `WIDE1-1,WIDE2-1`; `--path ""` for none. |
| `--tocall` | Destination TOCALL. Default `APZNET` (experimental). |
| `-n`, `--dry-run` | Print the packets without connecting. |
| `--retry` | For a message: `default`, `aprs-is`, `slow-hf` or `none`, as for `send message`. |
| `--wait-replies` | For a message: how long to keep listening for replies once it is answered. Default `2m`. |
| `--show-received` | For a message: also print every packet heard while it runs, as for `send message`. |

Each packet is printed once the TNC has it. Frames are spread out by the transmit limit: three at once, then one every
10 seconds, so telemetry with its definitions takes a few seconds. Exit code 2 when the TNC cannot be reached.

`tnc send message` holds a conversation on the air as `send message` does on APRS-IS: the message carries an ID, is
resent on the `--retry` schedule until the recipient's ack arrives over RF, and replies heard meanwhile and for
`--wait-replies` afterwards are printed and acked. The transmit limit spaces out every send, acks included. Exit code 5
when no ack comes.

## tnc kiss and tnc terminal

Set up a serial TNC by hand, at its own command line. Neither transmits anything on the air.

```sh
aprscli tnc kiss on --tnc serial:/dev/ttyUSB0,setup=th-d72    # switch a TH-D72A into KISS
aprscli tnc kiss off --tnc serial:/dev/ttyUSB0,setup=th-d72   # and back to its cmd: prompt
aprscli tnc terminal --tnc serial:/dev/ttyUSB0                # type commands, see the answers
aprscli tnc terminal --tnc serial:/dev/ttyUSB0 --script tnc-setup.txt
```

`tnc kiss on|off` runs the setup's script once and says whether it worked. Exit code 2 when the TNC doesn't answer, with
what to check (a TH-D72 in APRS mode can't switch).

`tnc terminal` sends each line typed as a command, with a carriage return, and prints what the TNC says, with control
bytes and KISS frames shown as `\xNN`. A line starting with `~` is a script step: `~bytes C0 FF C0` sends the KISS
exit frame, `~pause 1s` waits. Ctrl+D ends it. `--script FILE` runs a script instead and prints the answers; exit code 2
if an expected answer doesn't come. A script has one step per line:

| Step | Does |
| --- | --- |
| `send TEXT` | Sends the text and a carriage return; `send` alone sends just the carriage return. |
| `bytes HEX` | Sends bytes as they are, such as `bytes C0 FF C0`. |
| `expect TIME TEXT` | Waits up to the time (`500ms`, `3s`) for the TNC to send the text, without regard to case. |
| `pause TIME` | Waits. |
| `drain TIME` | Waits, then throws away what the TNC sent so far, such as packets it printed at its prompt, so an `expect` can't match an old prompt. |

Lines starting with `#` are comments.

## tnc share

Share one TNC with several KISS apps over TCP, the way Dire Wolf offers its own on port 8001. Run it on the small
computer the TNC is plugged into, such as a Raspberry Pi, and point Xastir, YAAC, another `aprscli` or anything else
that speaks KISS over TCP at that computer:

```sh
aprscli tnc share --tnc serial:/dev/ttyUSB0@1200                       # apps on this computer, port 8001
aprscli tnc share --tnc serial:/dev/serial/by-id/usb-FTDI_TNC-X-if00-port0@1200 --listen 0.0.0.0:8001
aprscli tnc listen --tnc pi.local:8001                                 # from another computer
```

Unlike a byte-for-byte serial bridge such as ser2net, `tnc share` handles whole frames:
- **Frames in one piece.** Each frame an app sends is read whole before it goes to the TNC, then written to the port
  in one go.
- **One frame at a time.** The next frame waits until the TNC has had time to take the last one over the serial port
  and send it on the air. That time is worked out from the frame's length, the port's baud rate and `--air-rate`,
  plus `--gap`.
- **Why it matters:** a TNC with little buffering, such as the TNC-X, never holds more than the frame it is sending,
  and gets each one complete.

Every frame the TNC hears goes to every app. A frame one app sends isn't echoed to the others. An app that stops
reading and falls far behind is disconnected so it can't hold up the rest.

If the TNC is unplugged or its TCP connection drops, `tnc share` reconnects, as `tnc listen --reconnect` does. The apps
stay connected meanwhile, and frames they send while the TNC is away are dropped and reported. KISS commands from apps
that set TNC parameters, such as TXDELAY, are ignored, so one app can't change the TNC under the others.

Each frame heard and sent is printed with the time (`2026-10-22 12:00:00Z heard N1ABC>APRS…`); apps connecting and
leaving, the TNC's state and a summary at the end go to stderr.

| Option | Meaning |
| --- | --- |
| `-t`, `--tnc` | The TNC: `serial:PORT[@BAUD]` (default 9600 baud), or `host[:port]` for a TNC over TCP that takes one connection. `,setup=th-d72` works as for `tnc listen`; `,text` is refused. Required. |
| `-l`, `--listen` | Where apps connect. Default `127.0.0.1:8001`, this computer only; `0.0.0.0:8001` for the network. |
| `--max-clients` | How many apps at once. Default 16; more are turned away. |
| `--radio-port` | The radio port on a multi-port TNC, 0 to 15. Default 0. |
| `--air-rate` | The radio's data rate in bit/s, for pacing. Default 1200. |
| `--gap` | Extra wait after each frame's estimated time on the air, for the TNC's keyup delay and a busy channel: `500ms`, `1s`, or `0`. Default `500ms`. |
| `--transmit-limit` | Limit all apps together to 3 frames at once, then one each 10 s. Default off, as apps pace themselves. |
| `-q`, `--quiet` | Don't print each frame. |
| `-d`, `--duration` | Stop after this long. Default: until Ctrl+C. |
| `--quiet-warning` | Warn when the TNC sends nothing for this long, as for `tnc listen`. |
| `--config` | Read options from a TOML file, as the [services](../services/README.md) do. See [Config files](#config-files). |
| `--check` | Check the options and any `--config` file, say what would be shared, and exit without connecting. |

KISS over TCP has no login: anyone who can reach the port can transmit through your TNC. Keep `--listen` on
`127.0.0.1` or a trusted network, behind a firewall. To run it as a service, see [the services](../services/README.md).

### Config files

`serve` and `tnc share` can read their options from a TOML file with `--config`, which is how the
[services](../services/README.md) run them. Each key is an option's long name without the dashes, a list repeats an
option, and `[tables]` only group the keys for reading. An option on the command line wins over the file.

```toml
# gateway.toml
[aprs-is]
callsign = "N0CALL"
filter = "r/35/-97/300"

[sources]
tnc = ["localhost:8001", "192.168.1.20:8001"]

[clients]
listen = "0.0.0.0:14580"
history = "30m"
```

```sh
aprscli serve --config gateway.toml --check        # aprscli: the settings are good. Would serve …
aprscli serve --config gateway.toml -l 127.0.0.1:14581
```

A key that isn't one of the command's options, a key set twice, or a value that isn't a string, a number, `true` or
`false`, or a list of them, is refused with its line number. A file can't hold `passcode`: set `APRS_PASSCODE` instead.

## gps

Read a GPS and print a line for each fix: a quick check of a GPS setup. It reads gpsd, or the NMEA sentences a GPS
sends over TCP (a GPS-to-network bridge, or gpsd's own NMEA port) or on a serial port (a USB GPS, or a radio's GPS
output) with no gpsd running:

```sh
aprscli gps --gpsd                          # gpsd on this machine, port 2947
aprscli gps --gpsd raspberrypi.local --device /dev/ttyACM0 -n 10
aprscli gps --nmea 192.168.1.20:10110 --satellites
aprscli gps --serial /dev/ttyACM0                # 4800 baud, NMEA's standard speed
aprscli gps --serial COM4@9600
```

```text
2026-10-20 14:00:02Z 3D 49.05833 -72.02917 105.3 m 48.6 kn 90° 9/12 sats HDOP 0.92 ±3.1 m
```

Each line has the time, the fix (`no fix`, `fix`, `2D` or `3D`), then whatever the GPS gave: the position, the altitude
above mean sea level, speed and course, the satellites used and in view, HDOP and the estimated horizontal error.
gpsd's error is at 95% confidence; from NMEA it is GST's one standard deviation, when the GPS sends GST. From NMEA a
second's fix is printed when the next second starts, once all of its sentences are in.

- `--satellites` (`-s`) lists the satellites in view after a fix, when they have changed: the system, number, signal,
  elevation, azimuth, signal-to-noise ratio and whether the fix uses it.
- `--raw` prints the lines as read instead: gpsd's JSON or the NMEA sentences.
- `-n` stops after that many fixes, `-d` after that long.

When the source goes away (gpsd restarts, the bridge drops, the GPS is unplugged) it keeps trying, waiting 5 seconds
and doubling to a minute, and says so on standard error.
Ctrl+C ends it with exit code 0.

<sub>Developed with AI assistance; see the [AI declaration](https://github.com/APRS-NET/APRS.NET/blob/main/AI-DECLARATION.md).</sub>
