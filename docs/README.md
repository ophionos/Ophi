# Ophi

Self-hosted price tracking — .NET 10 + SvelteKit + PostgreSQL.

**Status:** All MVP, parity, ecosystem-integration, technical-improvement, and PostgreSQL-migration
phases complete. Comprehensive test suite (xUnit backend + Vitest frontend + Playwright E2E) with a
provider-sensitive Postgres tier; coverage targets 80% backend / 70% frontend.

## What Ophi Does

A user can:

1. Create an account and log in.
2. Add a product by URL (one or more URLs per product). Ophi auto-extracts price, title, image — falling back to Playwright for JS-heavy sites.
3. Watch price history charts, per-URL trends, and a "best price" rollup.
4. Set alerts on `below`, `above`, or `percentDrop` conditions and receive email, in-app, Discord, Telegram, Pushover, or outbound-webhook notifications.
5. Compare prices across stores via comparison groups.
6. Organize products with tags, favourite the important ones, and search/filter/sort the dashboard.
7. Configure custom store selectors when auto-detection isn't enough; export/import store configs as JSON.
8. Script against the data via API keys (Authorization: Bearer); export/import products as CSV; scrape Prometheus metrics at `/metrics`.

## Core Principles

1. **Flexibility first** — track prices from any user-provided URL, not just preset retailers.
2. **Consumer-centric** — simple UX for non-technical users.
3. **Actionable insights** — trends + timely alerts, not just raw data.
4. **Privacy-respecting** — minimal data collection; user data stays under their control.
5. **Reliability** — accurate extraction and dependable alert delivery.
6. **Test-driven** — no production code without a failing test first.

## Documentation

| Doc | Purpose |
|---|---|
| [Architecture](architecture.md) | System design, vertical slices, entity graph, design patterns |
| [Features](features.md) | What Ophi does, feature-by-feature |
| [Tech Stack](tech-stack.md) | Libraries + versions |
| [Testing](testing.md) | TDD discipline, naming, coverage targets, the Postgres test tier |
| [Security](security.md) | Auth, API keys, CSP, rate limits, secrets |
| [API](api.md) | Cross-cutting API rules (auth, CSRF, errors); endpoints live in the Development OpenAPI spec |
| [Deployment — Raspberry Pi](deployment/raspberry-pi.md) | Docker on ARM64 |
| [Design — live updates post-scrape](design/live-updates-post-scrape.md) | Decision record: polling vs SSE for real-time post-scrape refresh (SSE shipped) |
| [Design — icon vocabulary](design/icon-vocabulary.md) | Shared concept → lucide icon map for sections and actions |
| [Agent Notes](agent-notes.md) | Durable non-derivable implementation knowledge — invariants, rejected alternatives, bug classes behind each CI gate |
| [Future](future.md) | Post-MVP backlog, deferred work |
| [History](history/README.md) | Completed phases — archived narratives (incl. the Postgres migration) |

For agent-facing guidance (commands, conventions, gotchas), see [../CLAUDE.md](../CLAUDE.md).

## Scope

### In Scope (shipped)

- User registration and authentication (cookie sessions + API keys)
- Add products via URL with automatic extraction
- Multi-URL per product, per-URL price tracking
- Manual selector configuration for complex pages
- Auto-create store configs for unknown domains
- Dashboard with grid/list/feed views, search, sort, filter, tags
- Historical price charts + comparison groups
- Price drop alerts (below/above/percentDrop) with cooldowns
- Email, in-app, Discord, Telegram, Pushover, and outbound-webhook notifications
- Prometheus metrics + Grafana dashboard
- Scrape health page with per-domain success rate, anomaly detection, auto-pause
- PWA support; CSV/JSON product import/export; full account backup/restore (JSON)
- PostgreSQL with durable Wolverine messaging between API and Worker
- Real-time updates via Server-Sent Events (app-wide, with polling fallback)
- Password reset flow; user-agent rotation across browser profiles
- Account self-service (change password with session invalidation, update profile, delete account)

### Out of Scope (future, not shipped)

- Browser extension
- Mobile apps
- AI price prediction
- Multi-currency conversion
- Push notifications (Pushover, Gotify, Apprise — webhooks cover most)
- CAPTCHA solving / proxy rotation

See [future.md](future.md) for the full backlog.
