# APRS.NET services

Run `aprscli` as systemd services on a Linux machine next to the radio, such as a Raspberry Pi or a small PC, so the
radio and APRS-IS are there for every APRS app on your network:

- **`aprsnet-tnc-share@`** shares a hardware TNC on a serial port with KISS apps over TCP (`aprscli tnc share`). Every
  app hears what the TNC hears, and each frame an app sends reaches the TNC whole, one at a time.
- **`aprsnet-serve@`** serves APRS packets to APRS clients as an APRS-IS server (`aprscli serve`): what the TNC hears,
  an APRS-IS feed, or both, each packet once, and optionally runs an IGate: receive-only, or two-way with messages
  from APRS-IS sent back on RF.

Each service reads its settings from a TOML file in `/etc/aprsnet`. `install.sh` installs everything, `aprsnet-check`
checks the settings and the running services, and `aprsnet-logs` shows what they logged. With an OpenTelemetry
Collector, the services also send it their logs and metrics.

> **Alpha**, like the rest of APRS.NET.

## How the pieces fit

Only one program can have a serial port open, so the TNC share is the only thing that talks to the TNC. Everything
else that needs the radio connects to it on port 8001, the way apps connect to Dire Wolf:

```text
                         ┌────────────── the Pi ──────────────────┐
 radio ── TNC ── serial ─┤ aprsnet-tnc-share@tncx  :8001 KISS     ├── KISS apps that transmit: Xastir, YAAC,
                         │      │                                  │   aprscli tnc send
                         │      └─ localhost:8001                  │
 APRS-IS ────────────────┤ aprsnet-serve@gateway  :14580 APRS-IS  ├── AprsMap on a laptop, a desktop and in
                         │        (optional IGate)     :14590 HTTP │   Docker; aprscli listen; YAAC
                         └─────────────────────────────────────────┘
```

- **Maps and other clients that only receive** connect to the gateway on port 14580. Each logs in with its own filter,
  gets the last 30 minutes of packets when it connects, and doesn't take one of the TNC share's 16 places. In Docker,
  use the Pi's address on your network, not `localhost`.
- **Apps that transmit** connect to the TNC share on port 8001. Gateway clients are receive-only for now.
- **An IGate** is part of the gateway: turn it on in `gateway.toml`, receive-only (`igate = true`) or two-way
  (`igate-to-rf = true` as well), which transmits through the TNC share. Read the
  [gateway guide](../../docs/guides/gateway.md#a-two-way-igate) before turning on the two-way one.
- **gpsd** isn't part of this. Install it from your distribution; `aprscli gps` reads it.
- **A digipeater** connects to the TNC share like any other app: `aprscli tnc digi --tnc localhost:8001 --call
  N0CALL-1` (see the [TNC guide](../../docs/guides/tnc.md#digipeat)). There is no service unit for it yet; run it under
  your own unit, or by hand. The TNC share sends its frames one at a time with everyone else's.

## Install

The machine needs systemd and the .NET 10 SDK, which `dotnet tool install` uses. Install .NET where every user can
reach it, such as from your distribution's packages or in `/usr/share/dotnet`: the services run as their own user,
which can't see into your home folder.

From a clone of the repository:

```sh
sudo apps/services/install.sh
```

It:

- installs the latest `aprscli`, prereleases included, as a .NET tool in `/opt/aprsnet`;
- adds an `aprsnet` system user in the `dialout` group;
- installs the two services in `/etc/systemd/system` and the scripts in `/usr/local/bin`;
- writes the examples to `/etc/aprsnet/examples`, and `/etc/aprsnet/secrets.env` and `/etc/aprsnet/otel.env` if
  they aren't there.

It doesn't start anything.

| Option | Meaning |
| --- | --- |
| `--version V` | Install that version of `aprscli`, such as `0.6.0-alpha.2`. Default: the latest. |
| `--source DIR` | Also look for the package in this folder, for packages you built with `dotnet pack -c Release -o artifacts`. |
| `--dotnet PATH` | The `dotnet` to use, when root can't find it. |
| `--no-restart` | When upgrading, don't restart the services that are running. |
| `--uninstall` | Stop and remove the services, the scripts and `/opt/aprsnet`. Keeps `/etc/aprsnet` and the `aprsnet` user. |

Run it again to upgrade `aprscli`. It restarts the services that are running, and never changes your settings.

## Set it up

1. Find the TNC's stable name, which stays the same when USB devices are plugged in a different order:

   ```sh
   ls -l /dev/serial/by-id/
   ```

2. Copy the examples, name each file after what it serves, and edit them. Each file says what its settings do.

   ```sh
   sudo cp /etc/aprsnet/examples/tncx.toml /etc/aprsnet/tnc-share/tncx.toml
   sudo cp /etc/aprsnet/examples/gateway.toml /etc/aprsnet/serve/gateway.toml
   sudo nano /etc/aprsnet/tnc-share/tncx.toml /etc/aprsnet/serve/gateway.toml
   ```

3. Stop anything else that has the serial port open, such as ser2net or a terminal, and check the settings:

   ```sh
   sudo systemctl disable --now ser2net
   sudo aprsnet-check --config-only
   ```

4. Start the services. The file's name is the service's instance name:

   ```sh
   sudo systemctl enable --now aprsnet-tnc-share@tncx aprsnet-serve@gateway
   sudo aprsnet-check
   ```

For a second TNC, add another file in `/etc/aprsnet/tnc-share` with its own `listen` port, and start
`aprsnet-tnc-share@<its name>`.

## The config files

Each key is one of the command's options without the dashes: `aprscli tnc share --help` and `aprscli serve --help`
list them, and the [aprscli README](../AprsNet.AprsIsCli/README.md) explains them. A list repeats an option.
`[tables]` only group the keys for reading, and `#` starts a comment.

```toml
[tnc]
tnc = "serial:/dev/serial/by-id/usb-FTDI_FT232R_USB_UART_A1234567-if00-port0@1200"

[apps]
listen = "0.0.0.0:8001"   # every network this machine is on
```

`aprscli` reads the file itself (`--config`), so you can try one by hand. An option on the command line wins over the
file:

```sh
aprscli tnc share --config /etc/aprsnet/tnc-share/tncx.toml --check
aprscli tnc share --config /etc/aprsnet/tnc-share/tncx.toml --listen 127.0.0.1:8002
```

A key that isn't an option, a value of the wrong kind, or a key given twice is refused with its line number. A config
file can't hold the IGate's passcode: put it in `/etc/aprsnet/secrets.env`, which only root can read, as
`APRS_PASSCODE=12345`. systemd hands it to `aprsnet-serve@` services, and `aprscli` never prints it.

| File | What it's for |
| --- | --- |
| `/etc/aprsnet/tnc-share/NAME.toml` | `aprsnet-tnc-share@NAME` |
| `/etc/aprsnet/serve/NAME.toml` | `aprsnet-serve@NAME` |
| `/etc/aprsnet/secrets.env` | The passcode, for `serve` with `igate = true`. |
| `/etc/aprsnet/otel.env` | Where to send logs and metrics: see [OpenTelemetry](#opentelemetry). |
| `/etc/aprsnet/environment` | Where .NET is, written by `install.sh`. |
| `/etc/aprsnet/examples/` | The examples, replaced by each install. |

To change how a service runs rather than what it does, such as to start the gateway after the TNC share, use a
drop-in, which `install.sh` leaves alone:

```sh
sudo systemctl edit aprsnet-serve@gateway
#   [Unit]
#   After=aprsnet-tnc-share@tncx.service
```

## Check it

```sh
sudo aprsnet-check                  # every config and service
sudo aprsnet-check tncx             # one, by its file's name
sudo aprsnet-check --config-only    # the settings only, before enabling a service
```

For each config file, `aprsnet-check` checks:

- **The settings:** that `aprscli` accepts them, run as the `aprsnet` user with the passcode, as the service does.
- **The serial port:** that it's there and the `aprsnet` user can open it, and whether ser2net or another program has
  it open too.
- **A running service:**
  - that it is running and listening on its port;
  - with `http`, what `/health` says: it fails when nothing is up, or when a TNC has sent nothing for its
    `quiet-warning` and the radio is likely switched off; a gateway with only some sources up is a warning;
  - for a TNC share without `http`, whether it has lost the TNC since it started.
- **OpenTelemetry:** whether the collector in `otel.env` takes connections. Not reaching it is a warning, since the
  services run without it.

It exits 1 when anything failed, so it can run from cron or a monitoring system.

```text
aprsnet-tnc-share@tncx.service (/etc/aprsnet/tnc-share/tncx.toml)
  ok    Would share the TNC at /dev/serial/by-id/usb-FTDI_…-if00-port0 at 1200 baud on 0.0.0.0:8001 with up to 16 apps, …
  ok    /dev/serial/by-id/usb-FTDI_…-if00-port0 is there, and the aprsnet user can open it
  ok    aprsnet-tnc-share@tncx.service is running, since Thu 2026-10-22 04:43:24 UTC
  ok    listening on 0.0.0.0:8001
  ok    the TNC has been connected since the service started
```

## Logs

The services log to the systemd journal. A TNC share logs each frame heard and sent unless `quiet = true`; both log
apps and clients connecting and leaving, the TNC and APRS-IS coming and going, and warnings.

```sh
aprsnet-logs                         # the last 100 lines of every service
aprsnet-logs -f tncx                 # follow one
aprsnet-logs --packets tncx          # only frames heard and sent
aprsnet-logs --events                # everything but the frames
aprsnet-logs --since "1 hour ago"
aprsnet-logs --since today --packets tncx -- -o cat > heard.txt   # after --: options for journalctl
```

Use `sudo`, or add yourself to the `adm` or `systemd-journal` group, to see the services' logs. On a Pi's SD card, cap
the journal's size with `SystemMaxUse=200M` in `/etc/systemd/journald.conf`.

## OpenTelemetry

To send the services' logs and metrics to an OpenTelemetry Collector, give its OTLP receiver in
`/etc/aprsnet/otel.env`, then restart them:

```sh
sudo nano /etc/aprsnet/otel.env        # OTEL_EXPORTER_OTLP_ENDPOINT=http://collector.lan:4317
sudo systemctl restart 'aprsnet-*@*'
sudo aprsnet-check                     # says whether the collector takes connections
```

The file's comments cover OTLP over HTTP, headers with a token, and extra attributes. `install.sh` never changes it,
and only root can read it.

Each service reports `service.name` `aprsnet-tnc-share` or `aprsnet-serve`, with `service.instance.id` the name of its
config file, such as `tncx`. They send:

- **Metrics:** every 60 s, by default. [docs/metrics.md](../../docs/metrics.md) lists them.
  - A TNC share: frames heard and sent, the TNC's connection, apps connected, leaving and turned away, frames from apps
    that couldn't be sent, and how long frames wait for the TNC.
  - A gateway: its TNCs and APRS-IS, clients, packets and duplicates by source, and the IGate's gated and refused
    packets.
- **Logs:** what they write to the journal, as structured logs with fields such as `Tnc`, `Remote` and `Endpoint`:
  - apps and clients connecting and leaving;
  - the TNC and APRS-IS lost and back;
  - each run's start and summary.
- **Packets:** with `log-packets = true` in a config file, each packet as a log too: each frame heard and sent for a
  TNC share, or each packet from each source for a gateway. The packets don't go to the journal.

A collector config that takes OTLP and prints what arrives, to try it:

```yaml
receivers:
  otlp:
    protocols:
      grpc: { endpoint: 0.0.0.0:4317 }
exporters:
  debug: { verbosity: detailed }
service:
  pipelines:
    logs: { receivers: [otlp], exporters: [debug] }
    metrics: { receivers: [otlp], exporters: [debug] }
```

## Security

- **No logins:** neither KISS over TCP nor the gateway's clients log in. Anyone who can reach port 8001 can transmit
  through your radio. Keep both ports on your own network: allow only it in the firewall, or listen on `127.0.0.1`
  and use an SSH tunnel.
- **Sandboxing:** the services run as the `aprsnet` user. systemd sandboxes them: no privileges, a read-only system,
  and only the network and the serial port.
- **Your licence:** what the radio transmits is on your licence, whichever app sent it.
