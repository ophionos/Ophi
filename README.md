# Ophi

Self-hosted price tracking — add products by URL, watch price history, and get alerted when prices drop.

## Highlights

- Add products via URL with automatic price/title/image extraction (Playwright fallback for JS sites)
- Per-URL price tracking, history charts, and a best-price rollup across stores
- Price-drop alerts (`below` / `above` / `percentDrop`) over email, in-app, Discord, and outbound webhooks
- Comparison groups, tags, search/sort/filter, and a scrape-health dashboard
- Scripting surface: bearer API keys, CSV import/export, Prometheus `/metrics`, live updates over SSE

For the full feature list and scope, see [docs/README.md](docs/README.md).

## Tech Stack

- **Backend:** .NET 10, ASP.NET Core, EF Core, **PostgreSQL** (Npgsql), Wolverine (message bus + durable Postgres transport), FluentValidation
- **Frontend:** Svelte 5, SvelteKit 2, Tailwind CSS, Chart.js, Lucide Svelte
- **Scraping:** AngleSharp (HTTP), Playwright (JS sites)
- **Background work:** in-process `BackgroundService` schedulers + Wolverine handlers (no Hangfire)
- **Email:** MailKit · **Infra:** Docker, Docker Compose, Caddy · **Package manager:** Bun

> SQLite is **test-only** (fast in-memory unit/handler tier). All real deployments run PostgreSQL,
> selected by `DB_PROVIDER` (default `postgres`). See [docs/architecture.md](docs/architecture.md).

## Architecture

```
SvelteKit (SPA/SSR) ──▶ .NET API ──▶ PostgreSQL
                          │             ▲
   ScrapeProductUrlCommand │             │ shared DB
   (durable Wolverine       ▼             │
    Postgres transport)   Worker ─────────┘
                          (scraping / schedulers)
```

Backend is Vertical Slice + Wolverine; the API and Worker talk over a durable Postgres-backed
Wolverine transport (with a polling backstop). See [docs/architecture.md](docs/architecture.md).

## Documentation

Project docs live under [`docs/`](docs/README.md):

- [Architecture](docs/architecture.md) — system design, vertical slices, entity graph
- [Features](docs/features.md) · [API](docs/api.md) · [Security](docs/security.md) · [Testing](docs/testing.md) · [Tech stack](docs/tech-stack.md)
- [Raspberry Pi deployment](docs/deployment/raspberry-pi.md)
- [Future work](docs/future.md) · [Implementation history](docs/history/README.md)

Agent-facing instructions are in [`CLAUDE.md`](CLAUDE.md).

## Requirements

- .NET 10 SDK
- Bun
- Node.js (for Playwright)
- A PostgreSQL instance (or use the Docker stack below, which brings its own)

## Configuration

Backend config comes from environment variables (see [`src/Ophi.Api/.env.example`](src/Ophi.Api/.env.example)):

```env
# Database — PostgreSQL
DB_PROVIDER=postgres
ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=ophi;Username=ophi;Password=ophi

# Application URL (used for CORS + links in alert emails)
APP_URL=http://localhost:3000

# Metrics endpoint protection — REQUIRED in Production (the API refuses to start without it)
MetricsToken=

# Email (SMTP, optional)
SMTP_HOST=smtp.example.com
SMTP_PORT=587
SMTP_USER=notifications@ophi.app
SMTP_PASS=secret
SMTP_FROM=Ophi <notifications@ophi.app>

# Discord price-alert webhook (optional)
DISCORD_WEBHOOK_URL=

# Telegram / Pushover (optional) — one operator bot / app; each user saves only their chat id / user key.
TELEGRAM_BOT_TOKEN=
TELEGRAM_BOT_USERNAME=
PUSHOVER_APP_TOKEN=
```

## Running Locally

The backend needs a reachable PostgreSQL (set `ConnectionStrings__Postgres`). The quickest source is a
throwaway container: `docker run -d -p 5432:5432 -e POSTGRES_USER=ophi -e POSTGRES_PASSWORD=ophi -e POSTGRES_DB=ophi postgres:16-alpine`.

Backend + worker:

```bash
dotnet build
dotnet run --project src/Ophi.Api
dotnet run --project src/Ophi.Worker
```

Frontend:

```bash
cd src/Ophi.Web
bun install
bun run dev
```

## Running With Docker

The compose stack includes PostgreSQL, the API, the worker, and the SvelteKit web container:

```bash
docker compose -f docker/docker-compose.yml up --build
```

Development override (sets `ASPNETCORE_ENVIRONMENT=Development`):

```bash
docker compose -f docker/docker-compose.yml -f docker/docker-compose.dev.yml up --build
```

Default ports — Web: `http://localhost:3000`, API: `http://localhost:5000`.

## Testing

```bash
dotnet test                              # backend (xUnit; SQLite in-memory unit tier)
cd src/Ophi.Web && bun run test:run      # frontend (Vitest)
cd src/Ophi.Web && bun run test:e2e      # E2E (Playwright)
```

The provider-sensitive `Ophi.Postgres.Tests` tier needs a real Postgres (`POSTGRES_TEST_CONNECTION`
or a reachable Docker daemon for Testcontainers). See [docs/testing.md](docs/testing.md).

## Contributing

- TDD is mandatory: write a failing test first.
- Coverage targets: Backend 80%+, Frontend 70%+.
- Use Conventional Commits: `<type>(<scope>): <description>`.
- `bun run build` does **not** type-check — run `bun run check` before considering frontend work done.

## License

[AGPL-3.0-only](LICENSE). If you run a modified Ophi as a network service, you must offer its source to the users of that service.
