# Architecture

```
SvelteKit (adapter-node) ──/api proxy──▶ .NET API ──▶ PostgreSQL
                                           │  ▲          ▲
             ScrapeProductUrlCommand        │  │ LiveUpdate (SSE fan-out)
             (durable Wolverine Postgres    ▼  │          │ shared DB
              transport)                   Worker ───────┘
                                     (scraping, schedulers, alerts)
```

PostgreSQL is the only runtime database. SQLite exists only in the fast test tiers.

## Layout

```
src/
├── Ophi.Api/              # Features/{Domain}/{Action}.cs slices; Common/ (startup, auth, middleware, health)
├── Ophi.Worker/           # Handlers/, Services/ (dispatchers, retention, FX refresh), Configuration/WolverineConfig.cs
├── Ophi.Domain/           # Entities/, Services/ (cross-entity invariants), Messages/{Commands,Events}/, Enums/
├── Ophi.Infrastructure/   # Persistence/ (DbContext, Configurations/), Migrations/, Scraping/, Email/, Discord/,
│                          # Push/ (Telegram, Pushover), Webhooks/, Fx/, Metrics/, Formatting/
└── Ophi.Web/              # SvelteKit app; e2e/ holds Playwright specs with page objects in e2e/pages/

tests/
├── Ophi.Api.Tests/             # SQLite in-memory
├── Ophi.Infrastructure.Tests/  # SQLite in-memory; also covers Worker handlers
├── Ophi.Postgres.Tests/        # Provider-sensitive tier on a real Postgres
└── Ophi.TestHelpers/           # TestDbContextFactory, TestEntityFactory
```

Endpoints register through `app.Map{Action}Endpoint()` extension methods, aggregated by
`MapOphiEndpoints()` in `Ophi.Api/Common/Startup/EndpointRouting.cs`.

## Entities

```
User
├── Product                (UserId; ComparisonGroupId?)
│   ├── ProductUrl            (per-store URL, price, scrape state)
│   ├── PricePoint            (ProductId; ProductUrlId?)
│   ├── Alert                 (ProductId + UserId)
│   ├── ProductTag            (ProductId + TagId)
│   ├── ScrapeLog             (ProductId; ProductUrlId?)
│   └── CustomField           (owned collection)
├── Tag
├── ComparisonGroup
├── StoreConfiguration
├── WebhookTarget
├── ApiKey                 (SHA-256 hashed)
└── Notification           (ProductId?)

ExchangeRate               (global, display-only — see agent-notes § Pricing & aggregation invariants)
```

Deleting a user cascades over everything they own.

## API ↔ Worker messaging

`WolverineConfig` is the one place messaging is configured, for three modes:

- **Embedded** (`ENABLE_WORKER=true`): the API hosts the worker in-process; `ScrapeProductUrlCommand`
  runs on the local `scraping` queue.
- **SplitApi / SplitWorker** (the compose stack): the API publishes `ScrapeProductUrlCommand` to the
  `scrape-requests` Postgres queue, and the worker publishes `LiveUpdate` back on
  `scrape-notifications` for the API's SSE stream. No broker; Postgres holds messages and transport.

The durable transport is the instant trigger, not the reliability mechanism. A new product is
committed `Status=Pending`, and the worker's 60 s `DispatchPendingProductsAsync` poll re-publishes
anything still pending. Scheduled re-scrapes are a separate per-user dispatcher. Why it is gated the way
it is: [agent-notes § Messaging & alerts](agent-notes.md#messaging--alerts).
