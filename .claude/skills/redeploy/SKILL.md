---
name: redeploy
description: Redeploy the latest committed build into the WSL2 Docker stack and verify endpoint health. Use when asked to deploy, redeploy, or update the running stack.
# Runs as a background Sonnet subagent: the 10-45 min build log never enters the main context.
# The fork does not see the conversation, so this file must stay self-contained.
context: fork
model: sonnet
---

# Redeploy to the WSL2 Docker Stack

Validated end-to-end runbook. **Prereq:** the changes must be committed (ideally merged to main) in `E:\Projects\Ophi` — `<stack-dir>` is a `git archive HEAD` export, NOT a clone; uncommitted edits won't transfer.

`<distro>`, `<repo-wsl>`, and `<stack-dir>` are machine-local values from the untracked `CLAUDE.local.md` (see `/dev-stack`).

**Path + privileges (important):** the stack lives at **`<stack-dir>`** (NOT `~/ophi`), and `docker` requires **`sudo`** (the default WSL user isn't in the docker group). `<stack-dir>` is root-owned, so the default user can't even `cd` into it — a bare `cd <stack-dir>` fails with Permission denied. Run docker/compose steps as root via `sudo bash -c '...'`, and pipe the archive sync to `sudo tar` (existing files are root-owned).

Hold WSL warm first (see `/dev-stack` step 0): `wsl -d <distro> -- sleep 7200` as a background task.

## 1. Sync committed tree → `<stack-dir>`

```
wsl -d <distro> -- bash -lc 'git config --global --add safe.directory <repo-wsl>; cd <repo-wsl> && git archive HEAD | sudo tar -x -C <stack-dir>'
```

- `safe.directory` is required (git flags dubious ownership over the Windows mount). The `git archive` runs as the default user; the pipe to `sudo tar` writes the root-owned tree.
- Extract-over preserves the untracked `.env` and the Docker volumes. It does NOT prune files deleted since the last export — `sudo rm -rf` the tracked dirs first if a clean slate matters.

## 2. Build first, swap second (running stack survives a build failure)

Run the whole thing as root (single `sudo bash -c` so the `cd <stack-dir>` succeeds):

```
wsl -d <distro> -- sudo bash -c 'cd <stack-dir> && docker compose -f docker/docker-compose.yml --env-file .env build'   # run as a background task — see timing below
wsl -d <distro> -- sudo bash -c 'cd <stack-dir> && docker compose -f docker/docker-compose.yml --env-file .env up -d'   # api waits Healthy, then worker, then web
```

**Build timing — slow is not stuck.** ~10 min when the worker's Playwright layer is cached. **~30-45 min** when any `Ophi.Domain`/`Ophi.Infrastructure` change invalidates the publish layer, because the Chromium install layer sits after it and re-downloads both `chromium-headless-shell` and `chromium` at ~450 KB/s over the WSL2 NAT. Two progressing builds were once killed as "stalled" for not knowing this. The authoritative completion signal is the background build task's own exit; a killed `... | tail` pipeline also reports exit 0, so confirm with `docker images | grep docker-worker` freshness.

`up -d` keeps the `ophi-data` volume (DB + Data Protection keys), so sessions and data survive recreates.

## 3. Verify

Run inside ONE root session:

```
wsl -d <distro> -- sudo bash -c 'cd <stack-dir> && \
  docker compose -f docker/docker-compose.yml ps --format "{{.Service}} {{.Status}}" && \
  curl -s -o /dev/null -w "web:%{http_code}\n" http://localhost:3000/ && \
  curl -s http://localhost:5000/health && echo && \
  curl -s -w "\nHTTP %{http_code}\n" -X POST http://localhost:5000/api/v1/auth/login \
    -H "Content-Type: application/json" -H "Origin: http://localhost:3000" -H "X-Requested-With: XMLHttpRequest" \
    -d "{\"email\":\"x@example.com\",\"password\":\"WrongPass123\"}"'
```

- `ps` → all `Up (healthy)`
- web `:3000` → **200**
- `:5000/health` → **200** with `database` / `wolverine` / `scraping` all `"healthy"`
- **Wolverine-handler smoke** (non-mutating; proves codegen + scoped DbContext resolve in-container): bad-creds login → **401** = handler ran (good). **500** = Wolverine codegen/service-location problem. **403 "Missing required request header"** = you omitted `X-Requested-With` (CsrfMiddleware requires it on every non-GET).

## Gotchas

- **Build-config files must be in the image context.** The Dockerfiles copy `Directory.Packages.props Directory.Build.props .editorconfig` before `dotnet restore`; any new root-level build-config file (`global.json`, `nuget.config`, another `Directory.*.props`) must be added there too, or restore/publish fails (NETSDK1013 / analyzer errors under TreatWarningsAsErrors). The container flattens `src/Ophi.X/` → `/src/Ophi.X/`, so keep `.editorconfig` path globs prefix-independent (`**/…`, never `src/…`). CI builds the images (the `docker` job) only when a PR touches a Dockerfile, a root build-config file, or a `.csproj` — a new root-level file is caught only if its PR also edits the Dockerfiles or one of those files, so add the `COPY` in the same PR.
- Worker image rebuilds re-download Playwright Chromium; broken IPv6 to the CDN can hang the build (Node `ipv4first` fix already applied — if it recurs, watch `/proc/<pid>/io` write_bytes inside the buildkit netns, not host eth0).
- Verify inside ONE `wsl` session — separate `wsl` invocations can land on a freshly-restarted distro and report a half-up stack.

## Report

Return only: the commit deployed (`git rev-parse --short HEAD`), build duration, each check above with its observed value, and on failure the first error lines verbatim. Do not change code or config to make a check pass — a failed build leaves the old stack running; report and stop.
