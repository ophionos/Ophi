---
name: dev-stack
description: Run Ophi locally on Windows for UI / Playwright / manual-verification work. Use when asked to run, demo, screenshot, or manually verify the app. Two modes — fast native (API + vite + dockerized Postgres) or the full WSL2 compose stack.
---

# Run the App Locally (Windows)

`<distro>`, `<repo-wsl>`, and `<stack-dir>` are machine-local values from the untracked `CLAUDE.local.md`; substitute them before running.

Two verified modes. Use **native** for fast iteration on uncommitted code; use the **compose stack** to verify the real containerized deployment (it works end-to-end — see `/redeploy`).

## Step 0 — hold WSL warm (both modes, ALWAYS first)

WSL idle-shutdown cycles containers mid-session (ports flap, DB container IP shifts, API crash-loops). Before anything else, start a keepalive as a background task:

```
wsl -d <distro> -- sleep 7200
```

Symptoms of forgetting this: intermittent `ERR_CONNECTION_REFUSED` / 500 / `fetch failed`, all containers showing `Up Xs` simultaneously.

## Mode A — native (fast iteration, runs the working tree)

Topology: dockerized Postgres in WSL (host-published) + native API on :5041 + vite on :5173 (proxies `/api` → :5041).

1. **Postgres** (additive — do NOT touch the compose stack's `docker-db-1`, it's internal-only):
   ```
   docker run -d --restart unless-stopped --name ophi-pg-demo -p 5432:5432 -e POSTGRES_DB=ophi -e POSTGRES_USER=ophi -e POSTGRES_PASSWORD=ophi postgres:16-alpine
   ```
2. **Loopback gotcha:** IPv4 loopback to WSL-published ports may not forward; IPv6 does. With the keepalive running, `127.0.0.1:5432` normally works; if Npgsql still gets "actively refused", use `Host=::1`. Don't trust a single curl as proof of reachability.
3. **API** (from repo root) — env `DB_PROVIDER=postgres`, `ConnectionStrings__Postgres=Host=127.0.0.1;Port=5432;Database=ophi;Username=ophi;Password=ophi`, `ASPNETCORE_ENVIRONMENT=Development`, `DISABLE_PLAYWRIGHT=true`, then:
   ```
   dotnet run --project src/Ophi.Api --no-launch-profile --urls http://localhost:5041
   ```
   The API always runs `Migrate()` outside the Testing env and migrations are Postgres-only — running natively against SQLite is NOT viable.
4. **Web** (from `src/Ophi.Web`): `bun run dev -- --port 5173 --strictPort`. Must be **5173** — `appsettings.Development.json` allows 5173 + 3000 for CORS/CSRF, and 3000 belongs to the compose stack's web container.
5. **Demo data:** don't rely on the scraper (no worker, Playwright disabled). Register an account through the real UI, take its `Users.Id`, then seed via `scripts/seed-demo.sql` (edit the `uid`):
   ```
   wsl -d <distro> -- bash -lc 'cat <repo-wsl>/scripts/seed-demo.sql | docker exec -i ophi-pg-demo psql -U ophi -d ophi'
   ```
   Pipe SQL files instead of `psql -c` (nested PowerShell→bash→docker quoting mangles).

**Teardown:** stop the keepalive + dotnet/bun background tasks; `docker rm -f ophi-pg-demo`. The compose stack stays untouched.

## Mode B — full compose stack (verifies the real deployment, runs committed code)

The WSL2 stack at `<stack-dir>` (root-owned; all docker/compose commands need `sudo` — see `/redeploy` for path + privilege details) serves http://localhost:3000 end-to-end (web proxies `/api/*` → `http://api:5000` via `hooks.server.ts`). It runs a `git archive HEAD` export, **not** the working tree — use `/redeploy` to sync and rebuild. Browser-test directly against :3000.

## Verifying UI work

Use the Playwright MCP tools (`browser_navigate`, `browser_snapshot`, `browser_take_screenshot`, …) against :5173 (native) or :3000 (compose). Screenshots go in `shots/` (gitignored).
