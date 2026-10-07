# Ophi: Tech Stack

## Backend

| Component | Technology | Purpose |
|-----------|------------|---------|
| Runtime | .NET 10 | Application framework |
| Web API | ASP.NET Core | REST API |
| ORM | Entity Framework Core | Database access |
| Database | PostgreSQL (Npgsql) | Primary data store; SQLite is test-only (in-memory unit tier) |
| Mediator / Messaging | Wolverine | Command handling + durable Postgres-backed transport (API ↔ Worker) |
| Validation | FluentValidation | Request validation |
| Background Jobs | `BackgroundService` + Wolverine | In-process schedulers (price checks, retention) + message handlers |
| HTML Parsing | AngleSharp | DOM parsing |
| Browser Scraping | Playwright | JS-heavy sites |
| Email | MailKit | SMTP delivery |

## Frontend

| Component | Technology | Purpose |
|-----------|------------|---------|
| Framework | Svelte 5 | UI components |
| App Framework | SvelteKit 2 | Routing, SSR |
| Package Manager | Bun | Fast builds |
| Styling | Tailwind CSS | Utility CSS |
| Charts | Chart.js | Price visualization |
| Icons | Lucide Svelte | Icon library |

## Testing

| Type | Framework |
|------|-----------|
| Backend Unit/Integration | xUnit, FluentAssertions (SQLite in-memory) |
| Backend provider-sensitive | xUnit + Testcontainers / external Postgres (`Ophi.Postgres.Tests`) |
| Frontend Unit | Vitest, Testing Library |
| E2E | Playwright |

## Infrastructure

| Component | Technology |
|-----------|------------|
| Containerization | Docker |
| Orchestration | Docker Compose |
| Reverse Proxy | Caddy |

All components support ARM64 (Raspberry Pi 4 compatible).
