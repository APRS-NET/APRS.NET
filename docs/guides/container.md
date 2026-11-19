# Run the gateway in a container

The repository's `Dockerfile` builds an image of `aprscli`. As a gateway it runs `aprscli serve`: an APRS-IS feed,
KISS TNCs or both, served to APRS-IS clients on port 14580 and, with `--http`, to WebSocket clients and a health check
on port 14590.

```sh
docker build -t aprscli .
docker run -d --name gateway -p 14580:14580 -p 14590:14590 aprscli \
  serve -c YOURCALL -f "r/38.98/-94.67/300" --listen 0.0.0.0:14580 --http 0.0.0.0:14590 --history 10m
```

- **Callsign:** `-c` is the callsign for the read-only APRS-IS login; give your own. Without it, `aprscli` logs in
  with a generated listener ID such as `SWL482913`, which receives only.

- **Listen on `0.0.0.0` inside the container.** The defaults, 127.0.0.1, can't be reached through a published port.
  Publish the ports to `127.0.0.1` on the host (`-p 127.0.0.1:14580:14580`) to keep the gateway to your computer.
- **Health:** the image sets `APRSCLI_HEALTH_FILE=/tmp/aprscli.health`, and its `HEALTHCHECK` runs
  `aprscli health --file /tmp/aprscli.health`. `serve` refreshes that file every 10 s while any source is up (when
  `/health` would answer 200), so `docker ps` shows the gateway as healthy while any source is up and unhealthy about
  three minutes after none is; `--http` isn't needed for it. With `--quiet-warning`, a TNC that has sent nothing for
  that long counts as down: restarting the container won't help, since the radio is likely switched off. Podman keeps
  the check only for images built with `--format docker`.
- **TLS:** the WebSocket endpoint has none. Put a reverse proxy in front of it for `wss://`.
- **User:** the image runs as a non-root user.

## Watch stations with `listen`

The same image runs `aprscli listen`, for example to log a few stations to OpenTelemetry or a capture file. The
health check works for it too: `listen` writes the health file on each login and on every line the server sends,
its `#` keepalives (about every 20 s) included, so a filter that matches nothing for hours still shows a live link.
While it is disconnected or reconnecting the file isn't touched, and the check fails once the file is over 90 s old.

```sh
docker run -d --name aprs-monitor aprscli \
  listen -c YOURCALL-13 -f "b/WA0RC-B/KD0JWD-5" --reconnect --max-silence off --format json
docker ps --filter name=aprs-monitor   # healthy within a minute of the login
```

```yaml
services:
  aprs-monitor:
    image: ghcr.io/aprs-net/aprscli:<version>
    command: listen -c YOURCALL-13 -f "b/WA0RC-B/KD0JWD-5" --reconnect --max-silence off -q --log-packets
    environment:
      OTEL_EXPORTER_OTLP_ENDPOINT: http://collector:4317
    restart: unless-stopped
    mem_limit: 128m
    # No healthcheck section: the image's own check covers listen.
```

- `--reconnect` keeps it going after a drop; without it `listen` exits on the first one, and the container stops.
  A first login that fails also ends the run, so `restart: unless-stopped` retries it.
- Use `--health-file <path>` (or `-e APRSCLI_HEALTH_FILE=<path>`) to write it elsewhere, and match the path with
  `--health-cmd "dotnet /app/aprscli.dll health --file <path>"`. Set `APRSCLI_HEALTH_FILE=` (empty) to stop writing it.
- `aprscli health --file /tmp/aprscli.health --max-age 2m` checks it by hand: exit 0 while the file is younger than
  `--max-age` (default 90 s), 1 with the reason when it's older or missing.
- Other long-running commands (`tnc listen`, `tnc share`, `gps`) don't write the file, so give them
  `--no-healthcheck` (compose `healthcheck: disable: true`) or their own check.
- **Memory:** a `listen` container uses about 15 MiB of heap; `docker stats` shows more (about 45 MiB after a day)
  because it counts the page cache of the files the process reads and writes (its assemblies, a capture file). The
  kernel gives that back under pressure, so a `mem_limit` of 128m is plenty. To cap the .NET heap itself, set
  `DOTNET_GCHeapHardLimit` (for example `0x4000000`, 64 MiB); the image already uses workstation GC, the default for
  console apps.

## Published images

The release workflow can push the image for each release to the GitHub Container Registry as
`ghcr.io/aprs-net/aprscli:<version>`. It is off until the repository variable `PUBLISH_CONTAINER` is `true`; until
then no image is published, and you build your own as above. The `Dockerfile`'s base images are pinned by digest, and
Dependabot moves them with a `fix(deps)` update that gives a release, so once publishing is on, a new base reaches a
published image with that release.

Each published image:

- **Platforms:** `linux/amd64` and `linux/arm64` (a Raspberry Pi 4 or 5 with a 64-bit OS), under one tag.
- **Tags:** the version, such as `0.9.0-alpha.1`, always. A release that isn't a pre-release also moves `X.Y`, `X` and
  `latest`; a pre-release never does, so `latest` is the newest stable release. A tag can move to a rebuilt image,
  so to stay on one image, pin its digest: `ghcr.io/aprs-net/aprscli:<version>@sha256:...`. The release notes and
  the release workflow's summary give the digest.
- **Labels:** the OCI labels `org.opencontainers.image.source`, `revision` (the release's commit), `version` and
  `licenses` (MIT).
- **Signature and provenance:** the image is signed with [cosign](https://docs.sigstore.dev/) without a key: the
  signature's certificate names the release workflow that built it. It also carries a build provenance attestation and
  an SBOM.

Check an image before you run it:

```sh
cosign verify ghcr.io/aprs-net/aprscli:<version> \
  --certificate-identity-regexp 'https://github.com/APRS-NET/APRS.NET/.*' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
gh attestation verify oci://ghcr.io/aprs-net/aprscli:<version> --repo APRS-NET/APRS.NET
```

Every pull request that changes the image builds it for both platforms and tests it offline: `--version`, `parse`,
the non-root user and a size budget (`tests/container/smoke.sh`), a gateway, and `listen` exporting packet logs and
metrics to an OpenTelemetry Collector (`tests/container/e2e.sh`). It also scans the image with Trivy and fails on a
HIGH or CRITICAL vulnerability that has a fix. Run the tests on an image you built:

```sh
docker build --build-arg VERSION=0.0.0-dev -t aprscli .
tests/container/smoke.sh aprscli 0.0.0-dev
tests/container/e2e.sh aprscli 0.0.0-dev
```

## With Dire Wolf

`samples/compose` runs Dire Wolf next to the gateway, which reads it as a TNC at `direwolf:8001`:

```sh
cd samples/compose
# Set MYCALL and ADEVICE in direwolf.conf, and the callsign and filter in compose.yaml.
docker compose up -d
docker compose ps
```

Dire Wolf needs the sound card the radio is on (`/dev/snd`, in the `audio` group). For a TNC on a USB serial port
instead, pass the device to the gateway (`devices: ["/dev/ttyUSB0:/dev/ttyUSB0"]`) and give
`--tnc serial:/dev/ttyUSB0`.

## Check it

```sh
curl -s http://127.0.0.1:14590/health          # {"status":"healthy",...}
aprscli listen --server 127.0.0.1:14580 -f "r/38.98/-94.67/50"
```

A browser can follow the same feed at `ws://127.0.0.1:14590/aprs?filter=r/38.98/-94.67/50`; see
[the JSON format](../json.md).
