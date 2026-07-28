# fivem-enhanced

Alpine-based Docker image for running a **FiveM dedicated server for GTA V Enhanced** (FXServer), with **txAdmin**.

> Based on [`spritsail/fivem`](https://hub.docker.com/r/spritsail/fivem), adapted for the GTA V Enhanced server artifact.

## Features

- **Built for GTA V Enhanced** — targets the FiveM "Enhanced" server artifact.
- **txAdmin included** — the FiveM web management panel ships in the image; boot straight into it or run a static config.

## Ports

| Port    | Protocol | Purpose                                  |
| ------- | -------- | ---------------------------------------- |
| `30120` | TCP+UDP  | FiveM game server (players connect here) |
| `40120` | TCP      | txAdmin web management panel             |

> **Security:** Preferably don't expose the txAdmin port (`40120`) to the public internet. Reach it over a VPN or SSH tunnel, or bind it to localhost.

## Volumes

| Path       | Purpose                                                                 |
| ---------- | ----------------------------------------------------------------------- |
| `/config`  | Server data + `server.cfg` (working dir). Seeded with a default config on first run if empty. |
| `/txData`  | txAdmin data (profiles, admins, settings) — persist this if you use txAdmin. |

## Environment variables

All of the following are read by the image's entrypoint (verified against the image):

| Variable                          | Effect                                                                                                                            |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `LICENSE_KEY` (or `LICENCE_KEY`)  | Cfx.re server license key, injected as `sv_licenseKey`. **Required** in default-config mode. Get one at the keymaster (below).    |
| `RCON_PASSWORD`                   | Sets the RCON password in `server.cfg`. If unset, a random 16-char password is generated and printed to the logs on first run.   |
| `NO_DEFAULT_CONFIG`               | Skip executing `/config/server.cfg` and boot into **txAdmin mode** — configure the server (and license key) via the web UI. Do **not** set `LICENSE_KEY` in this mode. |
| `NO_LICENSE_KEY` / `NO_LICENCE_KEY` | Skip license-key handling entirely (e.g. if you set it yourself inside `server.cfg`).                                          |
| `DEBUG`                           | Enable shell trace (`set -x`) in the entrypoint for troubleshooting startup.                                                     |

Get a license key from the [Cfx.re keymaster](https://keymaster.fivem.net/).

## Quick start

### Option A — txAdmin (recommended for first-timers)

Boot into the txAdmin web UI and configure everything from your browser:

```bash
docker run -d \
  --name fivem \
  -p 30120:30120/tcp \
  -p 30120:30120/udp \
  -p 40120:40120/tcp \
  -e NO_DEFAULT_CONFIG=1 \
  -v fivem-config:/config \
  -v fivem-txdata:/txData \
  xbytez/fivem-enhanced:latest
```

Then open `http://<host>:40120`, complete the txAdmin setup wizard, add your license key there, and start the server.

### Option B — static server.cfg

Run a fixed config directly, passing the license key via env:

```bash
docker run -d \
  --name fivem \
  -p 30120:30120/tcp \
  -p 30120:30120/udp \
  -e LICENSE_KEY=cfxk_xxxxxxxxxxxxxxxxxxxx \
  -v /path/to/config:/config \
  xbytez/fivem-enhanced:latest
```

On first run with an empty `/config`, a default `server.cfg` is created and a random RCON password is printed to the logs. Edit `/config/server.cfg` and add your resources, then restart.

## docker-compose

```yaml
services:
  fivem:
    image: xbytez/fivem-enhanced:latest
    container_name: fivem
    restart: unless-stopped
    ports:
      - "30120:30120/tcp"
      - "30120:30120/udp"
      - "40120:40120/tcp"   # keep internal / behind VPN
    environment:
      - NO_DEFAULT_CONFIG=1   # txAdmin mode; omit and use LICENSE_KEY for a static server.cfg
    volumes:
      - fivem-config:/config
      - fivem-txdata:/txData

volumes:
  fivem-config:
  fivem-txdata:
```

## Credits

Based on [`spritsail/fivem`](https://hub.docker.com/r/spritsail/fivem) ([GitHub](https://github.com/spritsail/fivem)). This image adapts it for the GTA V Enhanced server artifact. Thanks to the spritsail maintainers.

## Notes

- FiveM / FXServer is a product of Cfx.re. This image packages it for containerized deployment and is not affiliated with or endorsed by Cfx.re or Rockstar Games.
- You *may* have to use seccomp profile `unconfined` and make sure you have `io_uring` enabled (sysctl: `kernel.io_uring_disabled`).