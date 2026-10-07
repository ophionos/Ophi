# Ophi: Testing

## TDD Workflow (Mandatory)

1. **Red** - Write failing test defining expected behavior
2. **Green** - Write minimum code to pass
3. **Refactor** - Improve while keeping tests green

**Rule:** No production code without a failing test first.

## Coverage Requirements

| Layer | Target |
|-------|--------|
| Backend | 80%+ |
| Frontend | 70%+ |

Advisory, measured via `test:coverage` — no threshold fails the build.

## Test Organization

### Backend (.NET)
```
tests/
├── Ophi.Api.Tests/             # Handler + API endpoint tests (SQLite in-memory)
│   ├── Unit/
│   └── Integration/
├── Ophi.Infrastructure.Tests/  # Scraping, persistence, services (SQLite in-memory)
├── Ophi.Postgres.Tests/        # Provider-sensitive tier on a REAL Postgres
└── Ophi.TestHelpers/           # Shared TestDbContextFactory + TestEntityFactory
```

**SQLite is test-only and diverges from production Postgres.** The fast unit/handler tiers
(`Ophi.Api.Tests`, `Ophi.Infrastructure.Tests`) run on in-memory SQLite via `TestDbContextFactory`.
Anything that depends on provider-specific behaviour — `jsonb`, `uuid`/GUID case-sensitivity, `LIKE`
vs `ILIKE`, decimal/`numeric` precision, index/`EXPLAIN` shape, the durable Wolverine transport —
belongs in `Ophi.Postgres.Tests`, which runs against a real Postgres. A passing SQLite test is **not**
evidence that DB-specific behaviour is correct.

**Running the Postgres tier:** set `POSTGRES_TEST_CONNECTION` to an admin connection (it creates and
drops an isolated `ophi_test_{guid}` DB per run), or leave it unset to let Testcontainers start
`postgres:16` (needs a Docker daemon reachable from the test runner). With neither available the tier
**hard-fails** by design, so a missing Postgres can't silently drop provider coverage. CI provides it
via a `services: postgres` container.

### Frontend (Svelte)
```
src/Ophi.Web/
├── src/lib/**/*.test.ts    # Component/unit tests
└── e2e/                    # Playwright E2E tests
    └── pages/              # Page Object Model
```

## Naming Conventions

```csharp
// Backend: [Method]_[Scenario]_[ExpectedResult]
AddProduct_WithValidUrl_ReturnsCreatedProduct
ExtractPrice_FromAmazonPage_ReturnsCorrectPrice
```

```typescript
// Frontend: "should [behavior] when [condition]"
it('should display product name when loaded')
it('should show error message when price extraction fails')
```

## Test Patterns

### Backend
- Use constructor syntax for positional records
- Seed User entities before Products/Alerts (FK constraints)
- In-memory SQLite via `TestDbContextFactory`
- Mock HTTP with `MockHttpMessageHandler`

### Frontend
- Mock `$app/environment`, `window.matchMedia`, Chart.js
- Use Testing Library queries (`getByRole`, `getByTestId`)
- Page Object Model for E2E (`e2e/pages/`)

## CI Gates

What CI enforces, and the bug class behind each gate, is owned by the table in
[agent-notes.md § CI gates](agent-notes.md#ci-gates-and-the-bug-classes-behind-them). Real-Chromium
`Category=Integration` tests run locally only.
