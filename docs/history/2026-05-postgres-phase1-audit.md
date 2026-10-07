# Phase 1 — SQLite-ism Audit (findings)

**Date:** 2026-05-31 · **Branch:** `feat/postgres-migration` · Companion to
[2026-05-postgres-migration.md](2026-05-postgres-migration.md). *(Archived — the migration shipped; see [README.md](README.md).)*

Read-only audit done **before** flipping the provider, so any problem found here is understood before
it can hide behind a provider change. Bottom line: **the codebase is in good shape for the swap** —
production code is UTC-clean, decimals are pinned, raw SQL is absent, and the only behavioural change
needed is `LIKE`→`ILIKE` for two search queries.

## Summary table

| Risk area | Verdict | Action needed |
|---|---|---|
| Raw SQL / GUID string comparison | ✅ Clean | none |
| DateTime / Npgsql UTC strictness | 🟡 Low | add a UTC-normalizing convention (belt & suspenders) |
| JSON / owned types | ✅ Low | none (jsonb is a no-op upgrade; projections are client-side) |
| Decimal precision | ✅ Clean | none |
| `LIKE` case-sensitivity | 🔴 Behavioural | switch 2 queries to `EF.Functions.ILike` |
| Provider plumbing | — | `DB_PROVIDER` switch, design-time factory, fresh migration baseline |

## 1. Raw SQL / GUID — CLEAN

No `FromSql*` / `ExecuteSql*` anywhere. The only `Database.*` calls are `Migrate()` (×2) and
`CanConnectAsync()` (health check). No string-GUID comparisons in production code. EF Core maps
`Guid` ↔ Postgres native `uuid` automatically, so the SQLite "GUIDs stored as uppercase TEXT" footgun
(which bit the retry verification as a *manual SQL* check, not in app code) does not affect the app.

**Unique-index case-sensitivity (LIKE's sibling) — also CLEAN.** Postgres unique indexes are
case-sensitive; SQLite's default comparison is looser. The one user-facing case the matters is
`User.Email` (`HasIndex(Email).IsUnique()`, no `COLLATE NOCASE`). Safe on Postgres because the app
**normalizes email to lowercase before both store and lookup**: `Register.cs:57`
`Email = request.Email.ToLowerInvariant()`, and `Login.cs:35` / `ForgotPassword.cs:33` query with
`request.Email.ToLowerInvariant()`. No functional/`lower()` unique index or `citext` needed.

## 2. DateTime / Npgsql UTC strictness — LOW RISK

Npgsql maps `DateTime` → `timestamp with time zone` and throws on write unless `Kind == Utc`.

- **Production code is clean:** zero `new DateTime(...)`, `DateTime.Parse`, `DateTime.Now`,
  `DateTimeKind`, or `DateTimeOffset` in `src/`. Every timestamp originates from `DateTime.UtcNow`
  (`OphiDbContext.SaveChangesAsync`, `CreateApiKey`) or `TimeProvider.GetUtcNow().UtcDateTime`
  (everywhere else) — all `Kind=Utc`. Reads from `timestamptz` come back `Kind=Utc`. Round-trips are safe.
- **Tests already UTC-aware:** every `new DateTime(...)` in `tests/` passes `DateTimeKind.Utc`
  explicitly. No fixes needed for the Postgres test tier.
- **Recommendation (belt & suspenders):** add a model convention so *future* code and any
  `Unspecified`-kind value can't break a write:
  ```csharp
  // OphiDbContext.ConfigureConventions
  configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
  configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
  ```
  where the converter does `DateTime.SpecifyKind(v, DateTimeKind.Utc)` on read and normalizes to UTC on
  write. Keeps `timestamptz`; removes the foot-gun class entirely. (Alternative — map to
  `timestamp without time zone` / `EnableLegacyTimestampBehavior` — rejected: loses tz-awareness for no
  benefit given we're already UTC-clean.)

## 3. JSON / owned types — LOW RISK

- **Only owned-JSON mapping** is `Product.CustomFields` (`OwnsMany(...).ToJson()`,
  `ProductConfiguration.cs:53`). SQLite stores it as TEXT; Postgres will store `jsonb` — a strict upgrade.
- **The known SQLite limitation is NOT relied upon.** "Can't project owned JSON in `.Select()`" only
  bites server-side projections. Both `CustomFields` projections (`GetProducts.cs:369`,
  `GetComparisonGroup.cs:87/116`) run over **already-materialized** entities (client-side `.Select`
  after the query executes), so they neither break today nor regress on Postgres. jsonb may *enable*
  server-side projection later, but nothing needs to change now.
- **The two `HasConversion` value converters** (`ApiKey.Scopes`, `WebhookTarget.Events`) convert
  `List<string>` → comma-joined **TEXT**, not JSON — fully provider-agnostic.

## 4. Decimal precision — CLEAN

Every money column pins `HasPrecision(18, 2)`: `Product.CurrentPrice`/`PreviousPrice`,
`ProductUrl`, `PricePoint`, `Alert`, `ScrapeLog`. Postgres will use `numeric(18,2)`. No action.

## 5. `LIKE` case-sensitivity — BEHAVIOURAL CHANGE NEEDED 🔴

SQLite `LIKE` is ASCII case-insensitive by default; Postgres `LIKE` is **case-sensitive**. Two
**server-side** search queries explicitly rely on the SQLite behaviour (one even comments
"use LIKE for SQLite index-friendly case-insensitive search"):

- `GetProducts.cs:188-189` — `EF.Functions.Like(p.Name, search)` / `EF.Functions.Like(pu.Url, search)`
- `GetTags.cs:40` — `EF.Functions.Like(t.Name, $"%{request.Search}%")`

**Fix (portable — works on BOTH tiers):** lower both sides and keep `Like` (which is in
EFCore.Relational and translates on SQLite *and* Npgsql). **Do NOT use `EF.Functions.ILike`** — it is
Npgsql-only and throws "untranslatable" on the SQLite unit tier, which has live coverage:
`GetProductsHandlerTests.cs:294` runs `Search: "SONY"` on SQLite (in-memory) and asserts
case-insensitive matching.

```csharp
var search = $"%{request.Search.Trim().ToLower()}%";
query = query.Where(p =>
    EF.Functions.Like(p.Name.ToLower(), search) ||
    p.ProductUrls.Any(pu => EF.Functions.Like(pu.Url.ToLower(), search)));
```

Same results on SQLite (already case-insensitive) and the case-insensitivity we need on Postgres
(`lower(col) LIKE lower(pattern)`). Drop the misleading "index-friendly" comment — `%x%` can't use a
btree index on either provider; do **not** add a trgm index (over-engineering at this data size).

> Not affected: `GetStores.cs:66` `c.Name.Contains(..., OrdinalIgnoreCase)` runs **client-side**
> (over the materialized store-config provider list), not in SQL. All other `Contains`/`StartsWith`/
> `EndsWith` hits are in-memory string/list ops or `IN (...)` translations — none are `LIKE`.

## 6. Provider plumbing (mechanics, not risk)

- `DependencyInjection.cs:25-36` hardcodes `UseSqlite(...)` + a WAL-mode `PRAGMA`. Needs a
  `DB_PROVIDER` switch (`sqlite` | `postgres`) selecting `UseNpgsql(...)`; the WAL pragma is
  SQLite-only and must be gated.
- `DesignTimeDbContextFactory.cs:20` hardcodes `UseSqlite` — EF tooling needs it to use Npgsql so the
  fresh baseline migration generates Postgres SQL.
- **29 source migrations** under `src/Ophi.Infrastructure/Migrations/` (SQLite SQL). Per the
  fresh-start decision: delete them and generate **one** Npgsql baseline.
- `Directory.Packages.props`: add `Npgsql.EntityFrameworkCore.PostgreSQL` `10.0.2` (verified in the
  Phase 0 spike against EF Core 10 / Wolverine 6.2.2).
- Tests: `TestDbContextFactory` (SQLite in-memory) stays for the fast unit tier; a Postgres
  Testcontainers tier gets added for provider-sensitive checks (migrations, jsonb, `ILIKE`).

## Recommended Phase 1 execution order

1. Package + `DB_PROVIDER` switch in `DependencyInjection` (default still `sqlite` so unit tests are untouched) + design-time factory.
2. UTC convention (#2) and `ILike` fixes (#5) — small, provider-aware, behaviour-preserving.
3. Delete SQLite migrations; generate the Npgsql baseline; verify `dotnet ef` against a throwaway Postgres.
4. Docker: add `postgres` service + volume; wire `ConnectionStrings`/`DB_PROVIDER` for api + worker.
5. Postgres Testcontainers tier for the provider-sensitive bits.
6. Full stack on Postgres, all tests green, manual smoke. **No messaging change yet** (that's Phase 2).

## Running the Postgres tier locally

`Ophi.Postgres.Tests` needs a Postgres. It resolves one in this order:
- **`POSTGRES_TEST_CONNECTION` set** → uses it as the admin connection (creates an isolated
  `ophi_test_{guid}` DB per run, drops it after). On Windows + WSL2 Docker, point it at the throwaway
  Postgres, e.g. `Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=spike`.
- **unset** → Testcontainers starts `postgres:16` (needs a Docker daemon reachable from the test
  runner — fine on Linux/CI, but **not** from Windows `dotnet` when Docker lives only inside WSL2).

⚠️ On Windows with neither the env var nor a reachable Docker daemon, this tier **hard-fails** (not
skips) — by design, so a missing Postgres can't silently drop provider coverage. Set
`POSTGRES_TEST_CONNECTION` for a local full-suite run. CI sets it via a `services: postgres` container
(see `.github/workflows/ci.yml`).
