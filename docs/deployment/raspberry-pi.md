# Raspberry Pi Deployment Guide

## Choosing a deployment path

There are two supported ways to run Ophi on a Pi. Pick based on your hardware and which sites you need to track.

| | **Lightweight** (`docker-compose.pi.yml`) | **Full** (`docker-compose.yml`) |
|---|---|---|
| Containers | 2 (Postgres + API/Worker embedded) | 4 (db, api, worker, web) |
| Frontend | static files served by host Caddy | Node SvelteKit server container |
| JavaScript scraping | **disabled** (`DISABLE_PLAYWRIGHT=true`) | enabled (Chromium in worker) |
| Memory footprint | ~640 MB cap (app 384 MB + Postgres 256 MB) | ~1.5 GB+ (Chromium spikes 200–400 MB) |
| Good for | Pi 4 (4 GB) | Pi 5 (8 GB) |

> **Database:** both paths run **PostgreSQL** in its own container (the `db` service, `postgres:16-alpine`,
> data in the `ophi-db` volume). SQLite is no longer a deployment target — it remains only for the unit
> test tier. The single-container Dockerfile still runs the API + Worker together; it just talks to the
> sidecar Postgres.

**The JavaScript trade-off matters.** The lightweight path cannot scrape sites that require a real browser. Of the two tuned adapters, **Amazon requires JavaScript** and will fail to scrape on the lightweight path (the scrape is recorded as an error, nothing crashes); eBay does not. Everything else scrapes through the generic selector set over plain HTTP and works on both paths, unless you add a custom store with `RequiresJavaScript` set. If you primarily track Amazon, use the full path on an 8 GB Pi.

- **Most users on a 4 GB Pi → [Option A: Lightweight](#option-a-lightweight-single-container--caddy).**
- **Need Amazon / JS-heavy sites and have 8 GB → [Option B: Full multi-container](#option-b-full-multi-container).**

## Prerequisites

- **Hardware**: Raspberry Pi 4 or 5. 4 GB is enough for the lightweight path; 8 GB recommended for the full path (Playwright/Chromium).
- **OS**: Raspberry Pi OS 64-bit (Debian Bookworm) or Ubuntu Server 24.04 ARM64
- **Software**: Docker Engine + Docker Compose v2
- **Network**: Static IP or DHCP reservation recommended

## Install Docker

```bash
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
# Log out and back in for group change to take effect
sudo systemctl enable docker   # start Docker on boot
```

## Project Setup (both paths)

Clone the repository and create your environment file:

```bash
git clone <your-repo-url> ~/ophi
cd ~/ophi

cp src/Ophi.Api/.env.example .env
```

Edit `.env`:

```bash
# Database — Postgres password for the bundled `db` container. The compose file builds the
# connection string from it (Host=db, user/db both "ophi"); you only set the password here.
POSTGRES_PASSWORD=change-me

# How you actually reach the app. Used for CORS and links in alert emails.
# Lightweight path (Caddy): your domain or http://<pi-ip>
# Full path:                http://<pi-ip>:3000
APP_URL=http://<pi-ip>

# Full path only — must match the browser origin (see .env.example notes).
ORIGIN=http://<pi-ip>:3000

# Email (optional)
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_USER=your-email@gmail.com
SMTP_PASS=your-app-password
SMTP_FROM=your-email@gmail.com

# Discord (optional — lightweight alternative to email)
DISCORD_WEBHOOK_URL=https://discord.com/api/webhooks/YOUR_ID/YOUR_TOKEN

# Telegram / Pushover (optional) — one operator bot / app; each user saves only their chat id / user key.
TELEGRAM_BOT_TOKEN=
TELEGRAM_BOT_USERNAME=
PUSHOVER_APP_TOKEN=
```

### Discord Webhook Setup

1. Open Discord, go to the channel where you want alerts
2. Click the gear icon (Edit Channel) → Integrations → Webhooks
3. Click "New Webhook", name it "Ophi Price Tracker"
4. Copy the Webhook URL and paste it into your `.env` file

---

## Option A: Lightweight (single container + Caddy)

One .NET container runs the API and Worker together; the SvelteKit frontend is built to static files and served by Caddy on the host, which also reverse-proxies `/api/*` to the container. Playwright is disabled to keep memory under ~384 MB.

### 1. Build the image

Build on the Pi:

```bash
cd ~/ophi
docker compose -f docker/docker-compose.pi.yml build
```

Or cross-compile from a faster machine and copy it over (see [Cross-compiling](#cross-compiling-from-a-desktop-faster)).

### 2. Extract the static frontend for Caddy

The frontend is baked into the image at `/app/static`. Copy it out to a host directory once:

```bash
cd ~/ophi
docker compose -f docker/docker-compose.pi.yml run --rm --entrypoint "" ophi \
  cp -r /app/static /tmp/static
# Or, without compose:
#   CID=$(docker create ophi:pi) && docker cp $CID:/app/static ~/ophi/static && docker rm $CID
```

Move the files to where Caddy will serve them (the example Caddyfile uses `~/ophi/static`).

### 3. Install and configure Caddy (on the host)

```bash
sudo apt install -y caddy        # or follow https://caddyserver.com/docs/install
sudo cp docker/Caddyfile.pi /etc/caddy/Caddyfile
sudo nano /etc/caddy/Caddyfile   # set your domain (or :80) and the `root` path
sudo systemctl restart caddy
```

`Caddyfile.pi` serves the static SPA, proxies `/api/*`, `/health` and `/openapi/*` to `localhost:5000`, and (with a real domain) gets automatic HTTPS via Let's Encrypt. For LAN-only use, replace `ophi.example.com {` with `:80 {`.

### 4. Run

```bash
cd ~/ophi
docker compose -f docker/docker-compose.pi.yml --env-file .env up -d
docker compose -f docker/docker-compose.pi.yml ps
docker compose -f docker/docker-compose.pi.yml logs -f
```

The container binds to `127.0.0.1:5000` only — Caddy is the public entry point. Visit your domain (or `http://<pi-ip>`).

---

## Option B: Full multi-container

Runs `api`, `worker` (with Chromium for JS sites), and a Node SvelteKit `web` container. Heavier, but tracks JS-rendered sites like Amazon.

### 1. Build and run

```bash
cd ~/ophi
docker compose -f docker/docker-compose.yml build
docker compose -f docker/docker-compose.yml --env-file .env up -d
docker compose -f docker/docker-compose.yml ps
docker compose -f docker/docker-compose.yml logs -f
```

The app is available at `http://<pi-ip>:3000`. Make sure `APP_URL` and `ORIGIN` in `.env` point at `http://<pi-ip>:3000` (the compose file reads them from `.env`).

### 2. Cap memory (recommended on a Pi)

Add resource limits so a Chromium spike can't take the Pi down:

```yaml
services:
  worker:
    deploy:
      resources:
        limits:
          memory: 1.5G
  api:
    deploy:
      resources:
        limits:
          memory: 512M
  web:
    deploy:
      resources:
        limits:
          memory: 256M
```

---

## Cross-compiling from a desktop (faster)

Building .NET on ARM can take 10–20 minutes on first build. From a dev machine:

```bash
docker buildx create --name ophi-builder --use

# Lightweight image
docker buildx build --platform linux/arm64 \
  -f docker/Dockerfile.pi -t ophi:pi --load .

# Or the full set
docker buildx build --platform linux/arm64 -f docker/Dockerfile.api    -t ophi-api:latest    --load .
docker buildx build --platform linux/arm64 -f docker/Dockerfile.worker -t ophi-worker:latest --load .
docker buildx build --platform linux/arm64 -f docker/Dockerfile.web    -t ophi-web:latest    --load .
```

Transfer to the Pi:

```bash
docker save ophi:pi | ssh pi@<pi-ip> 'docker load'
```

---

## Operations (both paths)

In the commands below, substitute the compose file you used (`docker/docker-compose.pi.yml` or `docker/docker-compose.yml`). For the lightweight path the service name is `ophi`; for the full path it's `api`.

### Backup & restore

Postgres data lives in the `ophi-db` Docker volume; back it up with `pg_dump` (the `db` service name
is the same on both paths).

```bash
# Back up (consistent online dump — no need to stop the app)
docker compose -f docker/docker-compose.pi.yml exec -T db \
  pg_dump -U ophi -d ophi -Fc > ophi-backup.dump

# Restore (into a fresh/empty DB)
docker compose -f docker/docker-compose.pi.yml up -d db
docker compose -f docker/docker-compose.pi.yml exec -T db \
  pg_restore -U ophi -d ophi --clean --if-exists < ophi-backup.dump
docker compose -f docker/docker-compose.pi.yml up -d
```

`pg_dump -Fc` is a consistent snapshot taken while the app runs. Keep the dump off the Pi's SD card.

### Adding swap (recommended on a 4 GB Pi)

```bash
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

### Auto-start on boot

`restart: unless-stopped` (already set in both compose files) restarts containers after a reboot, as long as Docker itself starts on boot (`sudo systemctl enable docker`, done in the install step).

### Health check & monitoring

```bash
# API health (both paths)
curl http://localhost:5000/health

docker compose -f docker/docker-compose.pi.yml ps
docker compose -f docker/docker-compose.pi.yml logs -f
```

Set `Logging__LogLevel__Default=Debug` in the container environment for verbose troubleshooting output.

### Updating

```bash
cd ~/ophi
git pull
docker compose -f docker/docker-compose.pi.yml build
docker compose -f docker/docker-compose.pi.yml up -d
# Lightweight path: re-extract static files (step A.2) if the frontend changed.
```

The `ophi-db` volume persists across rebuilds. Migrations run automatically on startup.

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Amazon (or other JS site) never gets a price on the lightweight path | Expected — Playwright is disabled. Use the full path (Option B) for JS-rendered sites. |
| Worker OOM killed (full path) | Add swap, add the memory limits above, or switch to the lightweight path |
| Chromium fails to launch (full path) | Ensure 64-bit OS; check `docker logs` for missing deps |
| API won't start / DB connection refused | Confirm the `db` container is healthy (`docker compose ... ps`); the API waits on `service_healthy`. Check `POSTGRES_PASSWORD` matches what the volume was created with |
| Caddy 502 / blank page (lightweight path) | Confirm the container is up on `127.0.0.1:5000`, the `root` path points at the extracted static files, and you re-extracted them after an update |
| Slow builds on Pi | Cross-compile (see above) |
| Discord not sending | Check `DISCORD_WEBHOOK_URL` is set; check `docker logs` for warnings |
