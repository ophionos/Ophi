# Migration Plan: PostgreSQL + Durable Wolverine Messaging

**Status:** ✅ Phases 0–4 COMPLETE (2026-05-31) on branch `feat/postgres-migration` · **Decision:** Path B (keep split deployment, migrate to Postgres, durable cross-process messaging)
**Author:** drafted 2026-05-31 · Phase 5 (Marten/event-sourcing) intentionally not done — out of scope.

## Goal

Replace database-polling-as-transport for the **one** cross-process interaction between the API and the Worker — `ScrapeProductUrlCommand` ("scrape this URL now") — with **durable Wolverine messaging** over a **PostgreSQL-backed transport + transactional outbox**. Keep the scheduled re-scrape as a poll. No behaviour change to the in-process alert→notification cascade.

## Why this shape (read first)

- **Today's polling is a transactional outbox in disguise.** `AddProduct` commits `Status=Pending` in the *same transaction* as the product; the worker reads committed state. Intent-to-scrape is already durably and atomically tied to the write. So this migration buys **latency (~60s → instant), not reliability.**
- **Postgres is required to do it *properly*.** Wolverine's durable inbox/outbox supports **PostgreSQL / SQL Server / RavenDb — not SQLite** ([docs](https://wolverinefx.net/guide/durability/)). A broker on SQLite (RabbitMQ/TCP) would *regress* consistency by reopening the "commit succeeds, publish fails" gap that polling doesn't have. Postgres also provides a **DB-backed transport**, so no separate broker (RabbitMQ) is needed.
- **This is not event sourcing.** What we need is reliable cross-process *command transport*. Marten/event-store-as-truth is a separate optional feature; out of scope here (Postgres unlocks it later if wanted).
- **Scope is small.** One message type. `AddProduct`/`AddProductUrl`/`ImportProducts` already `PublishAsync(ScrapeProductUrlCommand)`; in embedded mode it already works. The change is transport/routing config in the two `Program.cs` files, plus the DB swap. `DispatchDueProductUrlsAsync` (scheduled re-scrape) stays a poll — it's a per-user dynamic-interval scheduler, not an interaction.

## Current state (facts)

- **Two deployment modes:** *embedded* (`ENABLE_WORKER=true`, Pi/single-container — API runs the worker in-process, messaging already works) and *split* (Docker server — separate api + worker, messaging is dropped with "No routes can be determined", DB-polling bridges them). See [[api-worker-no-message-transport]] memory.
- **Cross-process interactions:** exactly one — `ScrapeProductUrlCommand`, published by `AddProduct`, `AddProductUrl`, `ImportProducts`, and (currently via a DB flag) `RetryScrapeProductUrl`.
- **Worker-internal (no change):** scheduled re-scrape (`PriceCheckDispatcher`), and the `PriceUpdatedEvent` → alert checks → notification → email/discord/webhook cascade.
- **DB:** SQLite everywhere (`UseSqlite`), 58 migrations, ~4 JSON/owned columns. Tests use SQLite in-memory (`TestDbContextFactory`).

## Phases

Each phase is independently shippable and testable. **The DB migration (Phase 1) is the real risk — it is isolated from and shipped before any messaging change.**

### Phase 0 — Spike & de-risk (throwaway branch) — ✅ DONE (2026-05-31)
- Stand up Postgres via a compose override.
- Prove Wolverine Postgres transport + EF Core outbox end-to-end in the **split** topology: API publishes `ScrapeProductUrlCommand` → Worker consumes it durably.
- Validate the payoff: stop the worker, publish, restart the worker → message is delivered (durability). This is the whole point; confirm it before investing.
- Output: confidence + the exact Wolverine config. Do not integrate yet.

> **Result: PASS — go for Phase 1.** Two-host harness proved transactional outbox + durable delivery
> after the publisher process was disposed. Exact verified Wolverine 6.2.2 / Npgsql EF Core 10 config,
> provisioned tables, and the WSL2 run notes are in
> [2026-05-postgres-spike-phase0.md](2026-05-postgres-spike-phase0.md). Spike code is
> the throwaway `spike/PostgresWolverineSpike/` on branch `spike/postgres-wolverine`.

### Phase 1 — PostgreSQL migration (DB swap only, zero messaging change) — ✅ DONE (2026-05-31)
The big, risky phase — isolated so a problem here can't be confused with messaging.

> **Shipped.** `DB_PROVIDER` switch (default **postgres**; SQLite is now test-only), UTC convention,
> portable `lower()+LIKE` search fixes, a single regenerated Npgsql baseline migration, a Postgres
> Testcontainers/env test tier (4 provider-sensitive tests), and Docker + CI wiring. Audit findings are
> in [2026-05-postgres-phase1-audit.md](2026-05-postgres-phase1-audit.md). Verified: **1417 backend tests green**
> (503 Infra + 910 API + 4 Postgres tier) and a live API boot applied the migration + `/health` healthy
> on Postgres. Polling still bridges API↔Worker — no messaging change yet (that's Phase 2).
- Add `Npgsql.EntityFrameworkCore.PostgreSQL`; switch `UseSqlite` → `UseNpgsql`, driven by a `DB_PROVIDER` config switch.
- **Regenerate migrations:** SQLite migrations can't be reused (provider-specific SQL). Create a fresh Postgres baseline (squash the 58).
- **SQLite-ism audit (the actual work):**
  - **GUIDs** — SQLite stores TEXT (uppercase); Postgres has native `uuid`. EF maps `Guid`→`uuid`, but any raw-SQL/string GUID comparison breaks (the retry verification hit exactly this case-sensitivity). Audit for raw GUID string usage.
  - **DateTime** — Npgsql is strict: maps to `timestamp with time zone` and requires `DateTimeKind.Utc`. Audit `BaseEntity` timestamps, `LastCheckedAt`, alert times, etc.
  - **JSON columns** — SQLite TEXT → Postgres `jsonb`. EF JSON mapping differs; the known "can't project owned JSON in `.Select()` on SQLite" limitation may *improve* on `jsonb` — verify.
  - **Search / `LIKE`** — SQLite `LIKE` is ASCII case-insensitive by default; Postgres `LIKE` is case-sensitive. Product/tag/store search needs `ILIKE` or a citext/collation. Audit all search queries.
  - **Decimal precision** — ensure price `numeric` precision/scale is pinned.
- **Tests:** keep fast unit/handler tests on SQLite in-memory **and** add a Postgres-backed integration tier (Testcontainers) for the provider-sensitive bits (migrations, JSON, search). (Sub-decision below.)
- **Data:** one-time SQLite→Postgres copy script, or accept a fresh start for the personal WSL2/Pi instances. (Sub-decision below.)
- **Docker:** add a `postgres` service + volume; api & worker get `ConnectionStrings__Postgres`.
- **Ship & verify:** full stack on Postgres, all tests green, manual smoke. Polling still bridges API↔Worker — no messaging change yet.

### Phase 2 — Wolverine durability + Postgres transport (wiring, no cutover) — ✅ DONE (2026-05-31)
- Add `WolverineFx.Postgresql`; `UsePostgresqlPersistenceAndTransport(...).AutoProvision()` + `UseEntityFrameworkCoreTransactions()` → durable message storage + DB-backed transport; the `wolverine_*` tables now exist.
- Configure routing: Worker **listens** on the Postgres transport queue (`scrape-requests`, distinct from the in-process `LocalQueue("scraping")` to avoid a name clash); API **routes** `ScrapeProductUrlCommand` to it. Both `Program.cs` updated.
- Verify durable envelope flow API→Worker. Keep the Pending-poll running in parallel as a safety net.

> **Shipped.** Extracted `Ophi.Worker/Configuration/WolverineConfig.cs` (one mode-parameterized helper: `Embedded | SplitApi | SplitWorker`), killing the API/Worker `Program.cs` duplication and reconciling the `ENABLE_WORKER` divergence. **Postgres wiring gated on connection-string PRESENCE, not `DB_PROVIDER`** (default postgres would otherwise wire it in the Testing env). Proven by `tests/Ophi.Postgres.Tests/CrossProcessDeliveryTests` (real `ScrapeNewProductHandler`, exactly-once) — answered the `[LocalQueue]`-vs-listener routing question (coexist, clean startup).

### Phase 3 — Cut interactions over to messaging — ✅ DONE (2026-05-31, mostly a no-op on this branch)
- `AddProduct`/`AddProductUrl`/`ImportProducts`: existing `PublishAsync(ScrapeProductUrlCommand)` now delivers durably + instantly via the transport (Phase 2 did this). `Status=Pending` kept; no longer the trigger.
- **Retry:** already publishes `PublishAsync(ScrapeProductUrlCommand(Force:true))` on this branch (the `RequestImmediateRescrape` DB-flag was only on the unmerged `fix/amazon-price-extraction`, never on `main`). Phase 2 made it deliver in split mode — no change needed.
- **Pending-poll (`DispatchPendingProductsAsync`):** kept at 60s as the reliability **backstop** (reliability comes from `Status=Pending` + poll, not from a transactional publish). Comments updated to frame poll=backfill, publish=instant trigger. Idempotent via the handler's `!Force && Status != Pending` guard.
- **`DispatchDueProductUrlsAsync` (scheduled re-scrape):** unchanged — it's the scheduler.
- **Non-goal (documented):** turning the three `AddX` publishes into an explicit transactional outbox. Reliability already comes from the poll; a transactional publish is a separate future pass with its own rollback test.

### Phase 4 — Cleanup & verify — ✅ DONE (2026-05-31)
- Embedded vs split config reconciled into the one helper; no dead paths.
- Cross-process durable-delivery tests: delivery (real handler), embedded in-process handling, and **worker-down → restart → delivered** — all in `Ophi.Postgres.Tests` (8 tests total). Embedded-on-Postgres also boot-smoked (Pi config: clean start, EF `Migrate()` + Wolverine `AutoProvision` coexist, `/health` green).
- Updated `docs/architecture.md`; updated the `[[api-worker-no-message-transport]]` memory.

### Phase 5 — (optional, separate) Marten / event sourcing
Only if event-as-truth is wanted as a feature. Out of scope of "replace polling".

## Risks
- **#1 = the Postgres DB migration (Phase 1)**, not the messaging. Mitigated by isolating and shipping it before any messaging change.
- Npgsql UTC strictness, JSON `jsonb` mapping, `LIKE`→`ILIKE` case-sensitivity — concrete gotchas; audit list above.
- Postgres on the Pi is heavier than SQLite (sub-decision).
- Added CI cost for a Postgres (Testcontainers) integration tier.

## Sub-decisions — RESOLVED (2026-05-31)
1. **Pi deployment** → **Postgres everywhere.** Standardize on a single provider; accept Postgres being heavier than SQLite on the Pi rather than pay dual-provider maintenance. (Implication: the `DB_PROVIDER` switch in Phase 1 still lands, but there is no long-lived SQLite production target — SQLite stays only for fast unit tests.)
2. **Data** → **Fresh start acceptable.** No SQLite→Postgres copy script; the personal WSL2/Pi instances start clean on Postgres.
3. **Tests** → **SQLite in-memory unit tier + Postgres (Testcontainers) integration tier.** Keep fast handler/unit tests on SQLite; add a Postgres-backed tier for the provider-sensitive bits (migrations, jsonb, search). Accept the added CI cost.
