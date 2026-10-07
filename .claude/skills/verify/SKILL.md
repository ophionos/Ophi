---
name: verify
description: Prove a change actually works by driving the running app end-to-end — not just tests/type-check. Use before committing nontrivial changes, or when asked to confirm behavior. Picks the cheapest surface that exercises the change for real.
---

# Verify a Change End-to-End

Tests passing is necessary, not sufficient — this repo's worst escapes were all green-but-broken
(`/dashboard` 500 from an illegal `+page.ts` export; container-only build breaks; SQLite-green
Postgres failures). Pick the cheapest surface below that actually exercises the change, drive it,
and **observe the behavior** (response codes, rendered UI, DB state), not proxies for it.

## 0. Always first

Green per CLAUDE.md § Testing Conventions (suites + type-check, plus the Postgres tier for DB behavior
and `bun run build` for routes/loaders). That's the entry price, not the verification — pick a surface
below and drive it.

## 1. Backend behavior → native API + curl

Start the native stack per `/dev-stack` Mode A (keepalive → `ophi-pg-demo` Postgres → API on :5041,
Development env). Then hit the changed endpoint with curl:

- Auth'd endpoints: register/login first and reuse the cookie jar (`curl -c jar -b jar`), or create an
  API key and use `Authorization: Bearer ophi_...`.
- Every non-GET needs `X-Requested-With: XMLHttpRequest` (CSRF) and `Content-Type: application/json`.
- **Wolverine-handler smoke** (proves codegen + scoped-DbContext service location): bad-creds login →
  401 = good; 500 = codegen/service-location problem; 403 = you forgot `X-Requested-With`.
- Verify persisted state with a follow-up GET (or psql into `ophi-pg-demo`), not just the mutation's
  status code.

## 2. Frontend/UI behavior → native stack + Playwright MCP

`/dev-stack` Mode A (vite on :5173), then drive the real browser with the Playwright MCP tools
(`browser_navigate`, `browser_snapshot`, `browser_take_screenshot`) against :5173. Seed data via
`scripts/seed-demo.sql` if the flow needs products. Load the actual changed route and interact —
don't stop at "the component test passes". Screenshots go in `shots/` (gitignored).

## 3. The Playwright e2e suite against your uncommitted changes

`bun run test:e2e` by default reuses whatever owns :3000 — on this machine that's the compose stack
(committed code, Production config), so your changes would NOT be exercised. **The tell:** `curl :5041`
shows your change behaving one way while test traffic behaves another. Don't `docker stop docker-web-1`
to free :3000 — it's the shared compose stack. Target the dev stack
instead: run vite on **:5173** (`bun run dev -- --port 5173 --strictPort`) with the native API on
:5041, then `PLAYWRIGHT_BASE_URL=http://localhost:5173 bun run test:e2e`. First run is cold
(vite compiles on first hit); a second run is warm. E2E recipe details: `docs/agent-notes.md`
§ Testing recipes.

## 4. Deployment-level changes → compose stack

Anything touching Dockerfiles, compose, hosting, env wiring, or Wolverine transport config can only
be proven in the real containerized deployment: commit, then `/redeploy` (it ends with the full
health + Wolverine-smoke checklist), and browser-test against :3000.

## Report

State what surface you drove, what you observed (actual codes/renders/rows), and anything you could
NOT verify (e.g. worker-path behavior with `DISABLE_PLAYWRIGHT=true`) — never imply coverage you
didn't exercise.
