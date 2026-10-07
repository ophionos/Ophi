# Architecture

## System Overview

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│ Svelte SPA  │────▶│  .NET API   │────▶│ PostgreSQL  │
│ (Frontend)  │     │  (Backend)  │     │  (Database) │
└─────────────┘     └──────┬──────┘     └──────▲──────┘
                          │                    │
       ScrapeProductUrlCommand                 │ shared DB
       (durable Wolverine Postgres transport)  │
                          ▼                    │
                   ┌─────────────┐             │
                   │   Worker    │─────────────┘
                   │ (Scraping)  │
                   └─────────────┘
```

> **Database:** PostgreSQL in all real deployments. SQLite remains only for the fast unit/integration
> test tier (in-memory, `EnsureCreated`); a Postgres-backed Testcontainers tier covers provider-sensitive
> behaviour. Selected at runtime by `DB_PROVIDER` (default `postgres`).

## Directory Structure

```
src/
├── Ophi.Api/                    # .NET Web API
│   ├── Features/                # Vertical slices by domain
│   │   ├── Products/
│   │   │   ├── AddProduct.cs
│   │   │   └── ...
│   │   ├── Alerts/
│   │   ├── Auth/
│   │   ├── Stores/
│   │   ├── Comparisons/
│   │   ├── Tags/
│   │   ├── Webhooks/
│   │   ├── ApiKeys/
│   │   └── Notifications/
│   └── Common/                  # Shared startup, helpers, health checks
│
├── Ophi.Worker/                 # Background service (price checks, retention)
│   ├── Handlers/
│   ├── Services/
│   └── Settings/
│
├── Ophi.Domain/                 # Entities, services, messages, enums
│   ├── Entities/
│   ├── Services/                # Cross-entity domain invariants (e.g. ProductPriceAggregator)
│   ├── Messages/
│   │   ├── Commands/
│   │   └── Events/
│   └── Enums/
│
├── Ophi.Infrastructure/         # External concerns
│   ├── Persistence/
│   │   ├── Configurations/
│   │   └── Migrations/
│   ├── Scraping/                # AngleSharp + Playwright
│   ├── Metrics/                 # Prometheus collectors
│   ├── Settings/
│   └── Email/
│
└── Ophi.Web/                    # Svelte frontend
    ├── src/lib/
    │   ├── components/
    │   ├── stores/
    │   └── api/
    ├── src/routes/
    └── e2e/                     # Playwright tests

tests/
├── Ophi.Api.Tests/              # SQLite in-memory unit/integration tier
├── Ophi.Infrastructure.Tests/   # SQLite in-memory unit tier
├── Ophi.Postgres.Tests/         # Provider-sensitive tier on a real Postgres
└── Ophi.TestHelpers/            # Shared TestDbContextFactory + TestEntityFactory
```

## Entity Relationships

```
User (root)
├── Product              (UserId FK)
│   ├── ProductUrl          (ProductId FK; per-store price tracking)
│   ├── PricePoint          (ProductId FK; historical snapshots)
│   ├── Alert               (ProductId + UserId FK)
│   ├── ProductTag          (ProductId + TagId composite key)
│   ├── ScrapeLog           (ProductId FK; 30-day retention)
│   └── Notification        (ProductId? nullable FK)
├── Tag                  (UserId FK)
│   └── ProductTag          (join table)
├── ComparisonGroup      (UserId FK)
│   └── Product             (ComparisonGroupId? nullable FK)
├── StoreConfiguration   (UserId FK)
├── WebhookTarget        (UserId FK; outbound webhooks)
├── ApiKey               (UserId FK; SHA-256 hashed)
└── Notification         (UserId FK, ProductId? nullable)
```

## Design Patterns

### Backend: Vertical Slice + Wolverine

- Features organized by business capability under `Ophi.Api/Features/{Domain}/`.
- Each slice is typically a single file holding Command, Handler, Validator, and Endpoint together (e.g. `Features/Tags/CreateTag.cs`).
- Dispatch via the Wolverine message bus: `bus.InvokeAsync<TResponse>(command)`.
- Endpoint registration: extension methods `app.Map{Action}Endpoint()` aggregated by `MapOphiEndpoints()` in `Ophi.Api/Common/Startup/`.
- Persistence via EF Core (`OphiDbContext`); migrations live in `Ophi.Infrastructure/Persistence/Migrations`.
- Single-entity invariants live as methods on the entity (`Product.MarkActive()`, `Alert.Trigger()`, `ProductUrl.RecordSuccessfulScrape(...)`); cross-entity invariants live as static services in `Ophi.Domain/Services/` (e.g. `ProductPriceAggregator.ApplyAggregate(...)`).

### Messaging: API ↔ Worker

Wolverine config is centralized in `Ophi.Worker/Configuration/WolverineConfig.cs` across three deployment shapes (so the API and Worker can't drift):

- **Embedded** (`ENABLE_WORKER=true`, Pi / single container): API runs the worker in-process; `ScrapeProductUrlCommand` is handled locally via `[LocalQueue("scraping")]`.
- **Split** (Docker server): separate API + Worker processes. The API **routes** `ScrapeProductUrlCommand` over a **durable Wolverine PostgreSQL transport** (queue `scrape-requests`); the Worker **listens** on it. No broker — Postgres backs both message storage and the transport (`UsePostgresqlPersistenceAndTransport(...).AutoProvision()`).

The Postgres wiring is gated on a connection string being **present** (not on `DB_PROVIDER`), so the SQLite test tiers stay in-memory. **Reliability does not depend on the publish:** a new product is committed `Status=Pending` in the same transaction as the write, and the worker's `DispatchPendingProductsAsync` poll re-publishes for anything still Pending — the durable transport is the *instant trigger*, the poll is the *backstop*. The handler's `!Force && Status != Pending` guard makes duplicate delivery idempotent. The scheduled re-scrape (`DispatchDueProductUrlsAsync`) is an independent per-user scheduler, not a cross-process interaction.

### Frontend: SvelteKit

- Svelte 5 with runes (`$state`, `$derived`, `$props`, `$effect`).
- Page data loading via `+page.ts` loaders (`await parent()` for auth, `api.withFetch(fetch)` for SSR safety); pages read initial data from `data` prop.
- Shared modal shell at `src/lib/components/shared/Modal.svelte`; one instance per modal usage with `focusTrap`.
- Chart.js is lazy-loaded via `$lib/utils/chart.ts` (`loadChart()` dynamic import; ships in its own chunk).
- Page Object Model for Playwright E2E tests under `src/Ophi.Web/e2e/pages/`.

## Cross-Cutting Concerns

- **Auth:** Cookie-based sessions (ASP.NET Core Identity, PBKDF2) for the UI; bearer API keys (SHA-256 hashed in DB) for scripting. See [security.md](security.md).
- **Rate limiting:** Per-user sliding windows on product/alert/store/webhook creation; auth endpoints separately rate-limited. See [security.md](security.md).
- **Observability:** Structured logging via `ILogger<T>` in every handler. Prometheus `/metrics` endpoint gated by `MetricsToken` in production. Three health endpoints: `/health/live`, `/health/ready`, `/health` (legacy alias).
- **Scraping:** Hybrid AngleSharp (HTTP) + Playwright (JS-required sites) — `StoreConfig.RequiresJavaScript` is the single signal. JSON-LD parsing via `JsonPathExtractor` (recursive `$..availability` / `$..priceCurrency`).
