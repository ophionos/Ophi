# Phase 0 Spike — Postgres + Durable Wolverine (findings)

**Date:** 2026-05-31 · **Branch:** `spike/postgres-wolverine` (throwaway) · **Result: PASS — go for Phase 1**

Companion to [2026-05-postgres-migration.md](2026-05-postgres-migration.md). This records the
de-risking spike the plan's Phase 0 calls for. The spike code was the throwaway
`spike/PostgresWolverineSpike/` console harness — **removed in Phase 1** (the exact verified config is
captured below; the `spike/postgres-wolverine` branch still holds the runnable code if needed).

## What was proven

A standalone console harness (`spike/PostgresWolverineSpike/Program.cs`) stood up **two separate
Wolverine `IHost`s** sharing one Postgres database and demonstrated, in order:

1. **Transactional outbox (success path)** — the publisher enqueued `ScrapeProductUrlCommand` *inside*
   a DbContext transaction via `IDbContextOutbox<T>.SaveChangesAndFlushMessagesAsync()`. Entity row +
   outgoing envelope committed together.
2. **Atomicity (failure path — the property that distinguishes outbox from bare transport)** — a second
   publish was made in a transaction forced to **roll back** (duplicate-PK `DbUpdateException`). The
   queue count stayed unchanged (`1 → 1`, atomicity **HELD**) and the consumer later received the
   *committed* command id, **not** the rolled-back one. This is the property the plan's "Postgres over a
   broker" thesis depends on: a failed DB write does not leak a message (no commit-succeeds/publish-fails
   gap). A non-transactional `PublishAsync` would have leaked here — so the success path alone would
   *not* have proven this.
3. **Durability with no live consumer** — the committed message sits in the Postgres-backed transport
   queue table `wolverine_queues.wolverine_queue_scraping` (verified count = 1) while **nothing is
   listening**.
4. **Cross-process delivery after publisher death** — the publisher host is fully disposed (simulating
   the API process going away), *then* a separate consumer host starts, listens to the same queue, and
   drains the message. Handler fired with the exact committed command id. → `RESULT: PASS`.

This is the whole point of the migration: instant, durable, atomic cross-process command delivery —
replacing the ~60s DB poll — without a separate broker.

## Faithfulness to the real codebase (why the spike is not a false positive)

The harness deliberately mirrors the app's Wolverine idioms so the green result reflects *our*
integration, not a clean-room hello-world:

- `opts.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;` + dynamic codegen
- a **scoped** DbContext on `UseNpgsql`, integrated through `AddDbContextWithWolverineIntegration<T>`
- `opts.UseEntityFrameworkCoreTransactions();`
- publish enrolled in the DbContext transaction (commit-and-enqueue atomically)

Scope kept minimal per plan: **no real schema / 58 migrations ported** — one trivial `Records` entity
only, just enough to exercise the transaction. The real schema swap is Phase 1.

## Exact Wolverine 6.2.2 Postgres config (verified — compiles & runs)

Packages (versions that resolved against our existing Wolverine 6.2.2 / EF Core 10):

```xml
<PackageReference Include="WolverineFx.Postgresql" Version="6.2.2" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.2" />
```

DbContext registration (both API and Worker):

```csharp
builder.Services.AddDbContextWithWolverineIntegration<OphiDbContext>(o =>
    o.UseNpgsql(connectionString));
```

Wolverine options (both processes):

```csharp
opts.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;
opts.UsePostgresqlPersistenceAndTransport(connectionString).AutoProvision();
opts.UseEntityFrameworkCoreTransactions();
```

Routing — **publisher (API)**:

```csharp
opts.PublishMessage<ScrapeProductUrlCommand>().ToPostgresqlQueue("scraping");
```

Routing — **consumer (Worker)**:

```csharp
opts.ListenToPostgresqlQueue("scraping");
```

Transactional publish from a handler/endpoint (the outbox call that guarantees atomicity):

```csharp
var outbox = sp.GetRequiredService<IDbContextOutbox<OphiDbContext>>();
// ... mutate outbox.DbContext ...
await outbox.PublishAsync(new ScrapeProductUrlCommand(id));
await outbox.SaveChangesAndFlushMessagesAsync();
```

> Note: in our real handlers, `UseEntityFrameworkCoreTransactions()` + a handler that takes both the
> scoped `OphiDbContext` and `IMessageBus` already enrolls outgoing messages in the EF transaction
> automatically — the explicit `IDbContextOutbox<T>` form above is the manual equivalent used by the
> spike (which publishes outside a handler).

## Tables Wolverine auto-provisions (`AutoProvision()`)

- schema `wolverine`: `wolverine_incoming_envelopes`, `wolverine_outgoing_envelopes`,
  `wolverine_dead_letters`, `wolverine_nodes`, `wolverine_node_assignments`,
  `wolverine_node_records`, `wolverine_agent_restrictions`, `wolverine_control_queue`
- schema `wolverine_queues`: `wolverine_queue_scraping`, `wolverine_queue_scraping_scheduled`
  (one pair per named Postgres queue)

These tables **do not exist today** (SQLite has no durable Wolverine storage). Phase 2 wiring creates
them. For Phase 1 (DB swap only) consider pinning the schema name / disabling `AutoProvision` in
favour of explicit migration, TBD.

### ⚠️ Phase 2 gotcha — queue name collision
The app already declares a **local** queue named `"scraping"` (`opts.LocalQueue("scraping")` in both
`Program.cs` files). The spike routes to a **Postgres transport** queue *also* named `"scraping"`. In
**embedded** mode (`ENABLE_WORKER=true`) both registrations would coexist in one process. Resolve the
naming before cutover (e.g. give the Postgres transport queue a distinct name like
`"scraping-cross-process"`, or converge the local queue onto the durable transport). Flagging here
since config is being captured now; not a Phase 0/1 concern.

## Environment notes (WSL2)

- Docker is **native inside the `archlinux` WSL2 distro** (v29.5.1), not Docker Desktop.
- WSL2 mirrored networking forwards the container's published port to Windows `localhost:5433`, so the
  **Windows-side `dotnet` (10.0.204) connects directly** — no need to run the spike inside WSL2.
- **Gotcha:** WSL2 auto-shuts-down the distro when its last process exits, which kills the Postgres
  container. Hold the VM alive (e.g. a background `wsl -d archlinux -- sleep N`) for the duration, and
  give the container `--restart unless-stopped`.

Throwaway Postgres used for the spike:

```bash
docker run -d --name spike-pg --restart unless-stopped \
  -e POSTGRES_PASSWORD=spike -e POSTGRES_DB=spike -p 5433:5432 postgres:16
```

## Go/No-Go for Phase 1

**GO.** The transport + transactional-outbox surface works with our idioms on Wolverine 6.2.2 +
Npgsql EF Core 10. The remaining risk is squarely the **Postgres DB migration itself** (the plan's
stated #1 risk — GUID/uuid, Npgsql UTC strictness, jsonb, `LIKE`→`ILIKE`, decimal precision), not the
messaging. Phase 1 (DB swap, isolated, no messaging change) is the next step.

## Teardown

```bash
docker rm -f spike-pg            # remove throwaway Postgres
```
The `spike/` folder was deleted in Phase 1 (config captured above); the `spike/postgres-wolverine`
branch can be deleted once Phase 2 no longer needs the runnable reference.
