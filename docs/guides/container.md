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
- **Health:** the image's `HEALTHCHECK` runs `aprscli health` against `http://127.0.0.1:14590/health`, so give
  `--http` on port 14590. `docker ps` then shows the gateway as healthy while any source is connected, and unhealthy
  when none is. Podman keeps the check only for images built with `--format docker`.
- **TLS:** the WebSocket endpoint has none. Put a reverse proxy in front of it for `wss://`.
- **User:** the image runs as a non-root user.

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
