# Technical Improvement Pass (2026-05-15 → 2026-05-17)

> Source: high-level audit covering backend, frontend, dependencies/infra, and test suite. Focused on accumulated debt, drift from current best practices, and gaps surfaced after a couple of months of inactivity. Items were sized to be tackled phase-by-phase; each phase shipped independently.

## Confirmed Bugs (fixed first)

- [x] **`scrape-health` page never loads** — fixed in 2026-05-15: `auth.user` → `auth.current` at `src/Ophi.Web/src/routes/scrape-health/+page.svelte:30`.
- [x] **`PageFetchDelaySeconds = 0` ambiguity** — fixed in 2026-05-15: validators for `PageFetchDelaySeconds` and `ScrapeCacheTtlMinutes` now use the same `Value != 0` guard pattern as `DefaultCheckIntervalMinutes`, requiring `>= 1` in range and treating `0` as the reset sentinel.

---

## Phase 1 — Foundation & Safety (2026-05-15)

**Goal:** Stop shipping blind. Patch known CVEs. Make the metrics endpoint safe by default.

- [x] **Add CI** — `.github/workflows/ci.yml` runs backend (build/test/vulnerable-package scan) + frontend (`bun run test:run`) on push/PR. `.github/dependabot.yml` covers nuget/npm/github-actions on a weekly cadence with grouping for Microsoft framework, Wolverine, SvelteKit, etc.
- [x] **Patch vulnerable dependencies:**
  - `MailKit 4.15.1` → `4.16.0` in `Ophi.Infrastructure`.
  - Dropped unused `Microsoft.CodeAnalysis.Workspaces.MSBuild 5.0.0` from `Ophi.Worker`. The remaining transitive `System.Security.Cryptography.Xml 9.0.0` was overridden by direct refs to `10.0.8` in `Ophi.Worker`, `Ophi.Api.Tests`, and `Ophi.Infrastructure.Tests`. The full Wolverine bump (Phase 2) later eliminated the override.
- [x] **Fail-closed `MetricsToken`** — extracted `Ophi.Api.Common.MetricsAuthGuard.EnsureMetricsTokenConfigured(env, token)`; throws `InvalidOperationException` at startup when `IsProduction()` AND token is null/whitespace. Logs a warning otherwise. Covered by 7 unit tests.
- [x] **Remove dead `JWT_SECRET`** — JWT_SECRET was never in `.env.example`; the stale references lived in `README.md:79` and `docs/prd/07-security.md:26`. Both removed.
- [x] **Adopt Central Package Management** — added `Directory.Packages.props` at repo root listing all `PackageVersion` entries. All six csproj files now use version-less `<PackageReference Include="..." />`. Resolved the prior `Microsoft.Extensions.Hosting.Abstractions` 10.0.3/10.0.5 drift by pinning to 10.0.5 centrally.

## Phase 2 — Dependency Currency (2026-05-15)

**Goal:** Catch up on framework + library drift before it compounds.

- [x] **WolverineFx** `5.16.2 → 5.39.1` (23 minor versions). No code changes required. Bump also eliminated the pre-existing NU1608 Roslyn-version warning.
- [x] **EF Core / ASP.NET Core** `10.0.3 → 10.0.8` (and `Microsoft.Extensions.Hosting.Abstractions` realigned from 10.0.5 → 10.0.8). All 1318 backend tests passed.
- [x] **Test/dev tooling**: `coverlet.collector 8.0.0 → 10.0.0`, `Microsoft.NET.Test.Sdk 18.0.1 → 18.5.1`, `Scalar.AspNetCore 2.13.8 → 2.14.14`, `Microsoft.Playwright 1.58 → 1.59`.
- [x] **Frontend minor**: `@sveltejs/kit ^2.49.1 → ^2.60.1`. All 926 frontend tests passed.
- [x] **Docker base image pinning** — `Dockerfile.{api,web,worker,pi}` now pin .NET SDK + ASP.NET to `10.0-bookworm-slim` and bun to `1.2.6` / `1.2.6-slim`. Added `docker` ecosystem to `.github/dependabot.yml` so updates are auto-tracked.
- [x] **HEALTHCHECK directives** — `Dockerfile.web` uses `bun --eval` to fetch `localhost:3000`. `Dockerfile.worker` reads a heartbeat file (`$WORKER_HEARTBEAT_PATH=/tmp/ophi-worker.heartbeat`) that `PriceCheckDispatcher` writes on each loop iteration; `find -mmin -3` detects a stuck/hung process with one cycle of slack for long retention cleanups.
- [x] **System.Security.Cryptography.Xml override eliminated** — the Wolverine 5.39 bump pulled in updated EF Core Design / Microsoft.Build.Tasks.Core transitives, so the direct override was removed and the central `PackageVersion` deleted. Zero vulnerabilities.

## Phase 2.5 — Frontend Major Bumps (2026-05-15)

**Goal:** Land the deferred frontend major-version bumps. Each is breaking; each landed as its own commit so a regression bisects to a single dep. Order: low → high blast radius (tests → lint → compiler → build system).

Gate per bump: `bun run test:run` (CI's gate) + `bun run build` (smoke). `bun run check` and `bun run lint` had pre-existing baselines; each bump was required to not *add* errors.

- [x] **`jsdom 27 → 29`** — test environment only. No code changes; 926 tests pass, build clean.
- [x] **`lucide-svelte 0.562 → 1.0.1`** — v1 release. All `from 'lucide-svelte'` imports across ~30 files still resolve unchanged.
- [x] **`eslint 9 → 10`** (incl. `@eslint/js`) — flat-config preserved cleanly.
- [x] **`typescript 5 → 6`** — TS 6.0.3 installed; `svelte-check 4.3` accepts the new compiler. No new check errors.
- [x] **`vite 7 → 8` + `@sveltejs/vite-plugin-svelte 6 → 7`** — bumped together. Vite 8 ships rolldown as the bundler; its postcss-import couldn't resolve `@import 'tailwindcss'` through the legacy `@tailwindcss/postcss` plugin. Migrated to Tailwind 4's `@tailwindcss/vite` plugin: replaced the dep, deleted `postcss.config.js`, registered `tailwindcss()` before `sveltekit()` in `vite.config.ts`. Build time dropped from ~20s to ~9s.

**Carried forward:** Pre-existing baselines remain (`svelte-check` 9 errors in test fixtures; `eslint` 19 problems). Tracked separately; predate every bump in this phase.

## Phase 3 — Backend Performance (2026-05-15)

**Goal:** Fix the hottest hot paths before traffic exposes them.

- [x] **`GetProducts` rewrite** — combined the prior two count queries (`Total` + `atLowestCount`) into one round-trip; dropped `Include(Alerts)` in favor of a targeted alert-summary subquery that returns only the data needed (count + Below-condition target prices for `ComputeDealScore`). Full projection-to-DTO was scoped down because `CustomFields` is an EF Core JSON-owned collection that can't be projected via `.Select()` on SQLite without breaking; the entity load is kept for that property only.
- [x] **`AsNoTracking` + `AsSplitQuery` on read paths** — applied uniformly across `GetProducts`, `GetProduct`, `GetComparisonGroup`, `GetNotifications`, `GetAlerts`, `GetScrapeLog`, `GetScrapeHealth`. `GetStores` uses `IStoreConfigProvider` (not direct DbContext) so it's excluded. `AsSplitQuery` is used on the three handlers with multiple `Include` chains to avoid cartesian fanout.
- [x] **`PriceCheckDispatcher` cascade query** — split into two stages. Stage 1 (SQL) filters by `Status == Active && Status != Paused && (LastCheckedAt == null || LastCheckedAt < now − 15min)` — uses the existing `LastCheckedAt` index cleanly. Stage 2 (in-memory) applies the per-URL `CheckIntervalMinutes ?? DefaultCheckIntervalMinutes ?? 60` cascade plus the optional `ScrapeCacheTtlMinutes` floor. Over-fetch multiplier (5×) keeps the candidate window large enough for any mix of intervals. Picked scope-the-scan over column denormalization because the dataset is small and denormalization would have required invalidation hooks on every settings-change path. Added regression tests for both per-product-override directions.
- [x] **`ApiKeyAuthenticationHandler` write-on-read** — replaced the per-request `SaveChangesAsync` with a per-process `ConcurrentDictionary<Guid, DateTime>` keyed by `ApiKey.Id`. Writes throttled to once per minute. Uses `Context.RequestAborted` instead of `CancellationToken.None`. Multi-instance deployments each maintain their own cache; one extra write per instance per minute is well within the goal. Switched to `ExecuteUpdateAsync` to skip change tracking entirely. Added integration test asserting two requests within the throttle window produce one DB write.
- [x] **Affiliate URL helper duplication** — extracted `AffiliateUrlResolver` in `Ophi.Api.Common.Helpers`. Single `LoadAsync(db, userId, ct)` factory handles the user-setting + map fetch. `Resolve(url, storeId)` returns `null` when affiliates are disabled, the URL has no store, or the store has no affiliate config. Replaced the three duplicate blocks in `GetProducts`, `GetProduct`, and `GetComparisonGroup`.

Backend test count: 1318 → 1321 (+3 regression tests: 2 dispatcher, 1 ApiKey throttle). Frontend unchanged at 926.

## Phase 4 — Frontend Modernization (2026-05-16)

**Goal:** Move data loading to the right layer. Stop hand-rolling shells. Split mega-files.

- [x] **Add `+page.ts` loaders** — added to dashboard, products/[id], stores, comparisons, comparisons/[id], tags, scrape-health. Loaders `await parent()` for auth, use `api.withFetch(fetch)` so they work in SSR. Replaced `$effect(() => { if (auth.current && loading) loadData() })` with `data` props throughout.
- [x] **Extract `<Modal>` shell + apply `focusTrap` everywhere** — new `shared/Modal.svelte` (size/position/closeOnBackdrop/closeOnEscape/closeDisabled/titleId props). Migrated 12 modals (the 11 listed plus `ProductEditModal`). 13 new shell tests cover backdrop/escape/title/labelledby/size/position.
- [x] **Extract `QuickAlert`** — new `$lib/utils/quickAlert.svelte.ts` class. `ProductCard` holds one instance; `ProductTable` keeps a `Map<productId, QuickAlert>` and uses `{@const qa = quickAlertFor(product.id)}` per row. 11 new tests cover toggle/submit/timer/error paths.
- [x] **Chart factory + lazy-loaded Chart.js** — new `$lib/utils/chart.ts`. `loadChart()` dynamic-imports `chart.js` and registers controllers once. `getChartThemeColors(isDark)` + `formatChartDate()` shared. Chart.js now ships in its own 203 KB chunk (was bundled into route entries).
- [x] **Dashboard filter $effect → explicit handlers + AbortController** — every filter handler now calls `refreshProducts()`. `refreshProducts` aborts the previous in-flight `AbortController` so the latest filter wins. The old `$effect(() => filterParams)` is gone.
- [x] **Split `routes/stores/+page.svelte`** (1,131 → 982 LoC) — extracted `WebhookList.svelte` and `ApiKeyList.svelte`.
- [x] **Split `routes/dashboard/+page.svelte`** (464 → 377 LoC) — extracted `DashboardStatsBar.svelte`, `ViewModeToggle.svelte`, `DashboardPagination.svelte`. 11 new tests.
- [x] **Type drift** — added `openapi-typescript` dev dep + `bun run gen:types` script. Committed `schema.generated.ts`. Fixed the underlying camelCase mismatch in `Enum.ToApiString()` (previously `PercentDrop → "percentdrop"`, now → `"percentDrop"`; same for `PriceAlert`, `ScrapeError`, `UrlHealth`, `OutOfStock`, `BackInStock`).
- [x] **`$app/stores` → `$app/state`** — `routes/stores/+page.svelte`.
- [x] **Cleanup auth store on logout** — `handleLogout()` in `+layout.svelte` now calls `products.clear()`, `tags.clear()`, `comparisons.clear()`, `stores.clear()` alongside `notifications.clear()`. Added `clear()` to tags store (others already had it).

Test count: 2237 → 2292 (1321 backend + 971 frontend; +55 net new).

## Phase 5 — Scraping Subsystem Coherence (2026-05-17)

**Goal:** One implementation per concept. Today the subsystem had parallel pipelines that diverged across feature work.

- [x] **Single JS-required signal** — Dropped the hardcoded `HybridScrapingService.JsRequiredStores = { "amazon" }` hashset; verified `AmazonConfig.RequiresJavaScript = true` covers it. Both `ScrapeProductAsync` and `ScrapeWithConfigAsync` now branch solely on `storeConfig?.RequiresJavaScript`.
- [x] **Replace JSON-LD regex parsing** — Added `JsonPathExtractor.ExtractAll(...)`, `ExtractAvailability(...)` (returns `IEnumerable<string>`), and `ExtractCurrencyCode(...)` helpers. Recursive descent (`$..availability` / `$..priceCurrency`) matches the old regex breadth; `ExtractAll` scans every match across every JSON-LD block — preserving the regex-era "any OOS value in any script tag flags OOS" semantics that single-`Matches[0]` extraction would have broken. The `JsonLdAvailabilityRegex` / `JsonLdCurrencyRegex` partials and the `partial` modifier on `ScrapingService` are gone. `PlaywrightScrapingService` also drops its inline `page.EvaluateAsync<bool>(...)` and `page.EvaluateAsync<string?>(...)` JS scripts for JSON-LD OOS/currency, calling the same C# helpers against the already-fetched `GetJsonLdContentsAsync` array. Added 10 unit tests.
- [x] **Dedupe extractors (scoped)** — Pulled the currency-metadata selector list to `CommonSelectors.CurrencySelectors` (with `|content` suffix) so both services use the same list and the same `ScrapeHelpers.ParseSelectorSpec` parse. Threaded `jsonLdContents` through `DetectOutOfStock` / `ExtractCurrencyAsync` so each scrape parses JSON-LD once instead of three times.
- [x] **Reconcile `ScrapeProductAsync`/`ScrapeWithConfigAsync` overloads** — Documented the deliberate asymmetry on `ScrapingResult.FetchedHtml`: only populated by the HTTP `ScrapingService` on the generic-adapter path (so the worker can feed it to `IAutoCreateStoreService`); intentionally `null` from Playwright to avoid an extra `page.ContentAsync()` round-trip per scrape.
- [x] **`ScrapeOutcomeWriter` helper** — Extracted `BuildSuccessLog(...)` and `BuildFailureLog(...)` to `src/Ophi.Worker/Handlers/ScrapeOutcomeWriter.cs`. Replaced the 7 inline `new ScrapeLog { ... }` blocks across `ScrapeNewProductHandler` (3) and `CheckProductPriceHandler` (4). Kept scope to log-row construction — the branch-specific state machinery in `CheckProductPriceHandler` stays in the handlers where it belongs.
- [x] **Wide swallow-all `catch (Exception)`** — Tightened Playwright's regex-loop catches in `ExtractPriceAsync` / `ExtractImageUrlAsync` to `RegexParseException` + `RegexMatchTimeoutException` to match the HTTP service.

Backend test count: 1321 → 1329 (+8 net: +10 new `JsonPathExtractor` tests, −2 deleted `JsRequiredStores_*` tests). Frontend unchanged at 971.

## Phase 6 — Test Suite Hardening (2026-05-17)

**Goal:** Plug critical-path gaps; remove fragility; cut fat.

- [x] **TestEntityFactory + fluent builder** — replaced 15+ duplicated `new Product { Id = Guid.NewGuid(), UserId = ..., Name = ..., Currency = "USD", Status = Active }` blocks with fluent builders (one per entity: `Product`, `Alert`, `ProductUrl`, `User`). Mirror copies in both `tests/Ophi.Api.Tests/Helpers/` and `tests/Ophi.Infrastructure.Tests/Helpers/`; the duplication later collapsed when Phase 7's shared test-helpers project landed. Migrated 11 representative API-test call sites to validate the design; remaining ~10 sites stay on the old shape.
- [x] **`@external` e2e tag** — `custom-store-scraping.spec.ts` hits real `globaldata.pt` with 2× retries; tagged `@external` and rewired `package.json`: `test:e2e` now runs `--grep-invert @external`, `test:e2e:external` runs just those, `test:e2e:all` runs everything.
- [x] **Migration smoke test** — new `tests/Ophi.Infrastructure.Tests/Persistence/MigrationSmokeTests.cs` runs `Database.MigrateAsync()` against an empty in-memory SQLite DB and asserts the full migration chain applies. The 8 existing `EnsureCreated()` call sites bypass migrations entirely, so a broken script would ship green without this guard.
- [x] **`GetProductsHandlerTests.cs` partial collapse** — 1604 → 1550 LoC (-54). Collapsed three obvious clusters into `[Theory]`: sort-by-name (asc/desc), invalid-page clamping (page=0/-5), status filter (active/paused).
- [x] **`TimeProvider` scaffolding** — registered `TimeProvider.System` as singleton in both `Ophi.Api/Program.cs` and `Ophi.Worker/Program.cs`. Added `Microsoft.Extensions.TimeProvider.Testing` (9.10.0). Converted two high-impact services as proof-of-concept: `DataRetentionService` and `ApiKeyAuthenticationHandler`. The remaining ~23 production files still use `DateTime.UtcNow` directly and will migrate incidentally.
- [x] **Scraping resilience tests** — new `ScrapingServiceResilienceTests.cs` (11 tests). Covers HTTP error classification (404/410/403/429/5xx → correct `ScrapeErrorCategory`), network exception → `NetworkError`, empty response body → `ParseError`, malformed HTML survival, and cancellation behavior.
- [x] **Discord webhook failure modes** — `DiscordWebhookServiceTests.cs` 11 → 21 tests (+10). New theory-driven coverage for 4xx (BadRequest/Unauthorized/Forbidden/NotFound), 5xx (BadGateway/ServiceUnavailable/GatewayTimeout), network exception, timeout (`TaskCanceledException`).
- [x] **Negative-path `MockScrapingService` integration** — added `FailingScrapeFactory` (subclass of `OphiWebApplicationFactory`) that swaps in `FailingScrapingService` returning `ParseError`. `FailingScrapeEndpointTests` asserts the factory wiring is correct.
- [x] **Per-test DB isolation — partial** — added `OphiWebApplicationFactory.ResetDatabaseAsync()` so individual tests can opt into a clean slate when needed.
- [x] **Concurrency tests — deferred** — the alert-cooldown sequential case is already covered by `CheckAlertsHandlerTests` at line 480.

**Phase 6 deferrals (Phase 6.Xb):**
- 6.1b — migrate the remaining ~10 inline `new Product { ... }` sites to `TestEntityFactory` (incidental).
- 6.4b — push `GetProductsHandlerTests.cs` from 1550 → ~400 LoC by extracting `SeedProducts(...)` helpers.
- 6.5b — migrate the remaining ~23 production files from `DateTime.UtcNow` to injected `TimeProvider`.
- 6.9b — switch `OphiWebApplicationFactory` from `IClassFixture` sharing to per-test factory or per-test connection scoping.
- 6.10b — write parallel handler invocation tests for the alert-cooldown race with isolated `DbContext` instances.

Backend test count: 1331 → 1354 (+23). Frontend unchanged at 971.

### Phase 6.Xb — Deferred Items (2026-05-18)

All five Phase 6 deferrals shipped, with revised scope on two of them after orientation:

- [x] **6.10b** — three concurrent-handler tests added to `CheckAlertsHandlerTests`: sequential-with-separate-contexts (regression guard for the "stamp before emit" property), parallel-with-separate-contexts (documents the per-context race; production protection is `LocalQueue.MaximumParallelMessages(1)`), and fresh-context-after-cooldown-expires. `TestDbContextFactory` gained an `Attach(SqliteConnection)` overload so tests can simulate separate change-trackers hitting the same in-memory DB.
- [x] **6.1b** (scoped down) — only `CascadeDeleteTests` (7 sites) migrated. `ProductTests` and `ProductPriceAggregatorTests` were intentionally kept on inline construction — they test the entity in isolation, so the factory adds indirection without value. UserBuilder gained a default `PasswordHash = "hash"` so it can stand alone.
- [x] **6.5b** — 22 production files migrated. Static Wolverine handlers take `TimeProvider` as a method parameter; class-based handlers via primary ctor; `HealthCheckResponseWriter` resolves it from `HttpContext.RequestServices` (its delegate signature is fixed by ASP.NET). Two files intentionally skipped: `OphiDbContext.SaveChangesAsync` (3 recording-timestamp sites; injecting TimeProvider into the DbContext ctor would force ~20 test files to change for diagnostic value) and `CreateApiKey.cs` validator (refactoring the FluentValidation Validator ctor isn't worth it for a wall-clock rule). Dead `CreatedAt = UtcNow, UpdatedAt = UtcNow` inline writes in `CreateStore.cs` and `ImportStore.cs` were deleted — the DbContext override already stamps these for `BaseEntity` adds.
- [x] **6.4b** (scoped to one cluster) — `CreatePricePoint(product, price, daysAgo)` helper + `CreateProduct(...)` extended with `currentPrice`/`status` params. Sparkline + deal-score cluster migrated; two symmetric deal-score `[Fact]`s collapsed into one `[Theory]`. File: 1550 → 1491 LoC (-59, -3.8%). The PRD's aspirational ~400 LoC target wasn't realistic — the file is large because it tests 60+ distinct scenarios, not because of repetition. Stopped per the "stop when the diff stops obviously improving" rule.
- [x] **6.9b** — `IsolatedIntegrationTest` base class added. Calls `Factory.ResetDatabaseAsync()` in `InitializeAsync()` so each test sees a clean schema while the (expensive) `WebApplicationFactory<Program>` stays shared via `IClassFixture`. Nine integration test classes migrated. **Measured cost: 15s → 13s wall time** (essentially zero) — the original PRD's 2× slowdown warning was calibrated to a per-test-factory approach that nobody actually wanted. Per-test DB reset on a shared factory is ~10ms × 102 tests = ~1s total.

Backend test count: 1408 → 1411 (+3 from 6.10b; no net change from the other items). Frontend unchanged at 971. **Total: 2379 → 2382.**

## Phase 7 — Polish & Hygiene (2026-05-17)

**Goal:** Cleanup that's not urgent but makes the next pass easier.

- [x] **`ImportProducts.cs` vertical-slice violation** — extracted the 250-line endpoint-lambda body into a Wolverine `Command/Handler` pair (`Features/Products/ImportProducts.cs`). Endpoint parses the CSV stream into `ImportRow` records and invokes the bus; handler does dedupe + persistence + scrape-job publishing. Now unit-testable through `bus.InvokeAsync<ImportResponse>(...)`.
- [x] **Delete dead `IApplicationDbContext`** — removed the interface, the `IApplicationDbContext` declaration on `OphiDbContext`, the DI registration in `Ophi.Infrastructure/DependencyInjection.cs`, and the two corresponding references in `OphiWebApplicationFactory`.
- [x] **`WorkerSettings.DispatchIntervalSeconds` and `BatchSize` are static** — converted to `{ get; init; }` instance properties with the existing defaults (60s / 50); `PriceCheckDispatcher` now injects `IOptions<WorkerSettings>` and reads `_settings.DispatchIntervalSeconds` / `_settings.BatchSize`. `appsettings.json` keys already existed and now actually bind. Both `Ophi.Worker/Program.cs` (always) and `Ophi.Api/Program.cs` (`ENABLE_WORKER=true` only) register `Configure<WorkerSettings>` against the `Worker` section.
- [x] **Slim `Program.cs`** — 380 → 169 LoC. Extracted `AddOphiAuth(env)`, `AddOphiRateLimiting(env)`, `MapOphiEndpoints()`, and `MapHealthAndMetrics()` into `src/Ophi.Api/Common/Startup/` extension methods. Wolverine config, CORS, OpenAPI, and middleware ordering stay in `Program.cs` because they're tightly coupled to the builder lifecycle.
- [x] **Wolverine pattern mix in `SendAlertNotificationHandler`** — split into 3 cascading events. New events in `Ophi.Domain.Messages.Events/`: `SendAlertEmailRequested`, `SendAlertDiscordRequested`, `SendAlertWebhookRequested`. Orchestrator (`SendAlertNotificationHandler`) now returns `OutgoingMessages`; one channel handler each on the `notifications` queue. **Behavior change:** each channel retries independently under Wolverine's retry policy instead of email being critical-path-throw and Discord/webhook being best-effort try/catch. Discord cascade only emitted when the user has it enabled + a URL configured.
- [x] **CSV parser** — replaced hand-rolled `ParseCsvLine` with CsvHelper 33.1.0. Header matching is case-insensitive via `PrepareHeaderForMatch`; quoting/escape rules are now the library's.
- [x] **Metric cardinality** — **BREAKING change to `/metrics`.** Dropped `product_name` from `ophi_product_price_current` and `ophi_product_price_lowest_alltime` (label set: 3 → 2; now just `product_id` + `store`). Added new `ophi_product_info{product_id, product_name}` info-metric (always set to 1) carrying the name as a label. `PriceMetricsCollector` refreshes all three gauges per cycle. **Grafana dashboard updated** to join via `ophi_product_price_current * on(product_id) group_left(product_name) ophi_product_info`. External scrapers / dashboards that pulled `ophi_product_price_current{product_name="..."}` must switch to the join. Dashboard version bumped 1 → 2.
- [x] **Wire `ophi_product_price_lowest_alltime` into Grafana** — new "All-Time Lowest Price (per Product)" table panel + "Current vs All-Time Low (% above floor)" timeseries panel, both using the `group_left(product_name)` join.
- [x] **Two parallel `TestDbContextFactory.cs` files** — extracted shared `tests/Ophi.TestHelpers/` project (new csproj, namespace `Ophi.TestHelpers`). Moved `TestDbContextFactory` and `TestEntityFactory` into it; deleted the duplicates from the two test helper folders. Both test projects reference the new helpers project. `HandlerTestBase` stays in `Ophi.Infrastructure.Tests/Helpers/` (xUnit-tied) but now `using Ophi.TestHelpers;` for the factories.

**Deferrals (Phase 7.Xb):**
- 7.1b — **Anemic domain entities** (originally Phase 7 item 9). Deferred to Phase 8 per "worth a focused discussion before a wholesale refactor."

Backend test count: 1354 → 1366 (+12). Frontend unchanged at 971.

## Phase 8 — Domain Model Refactor (2026-05-17)

**Goal:** Move single-entity invariants onto the entities they belong to (hybrid pattern); move the multi-entity MIN-across-URLs aggregation to a domain service. Stop having handlers reach into entity internals.

**Scope:** ProductUrl + Alert + Product (selective). User skipped. Style: hybrid — entity methods own single-entity state, services own cross-entity invariants. Property setters loosened from `init` to `set` only where required by mutation methods; rest stay `init`.

- [x] **Alert** — new methods on `Ophi.Domain.Entities.Alert`:
  - `Trigger(DateTime now)` — stamps `LastTriggeredAt` + increments `TriggerCount`.
  - `IsInCooldown(TimeSpan, DateTime now)` — strict-less-than against the window.
  - `ShouldTrigger(decimal currentPrice, decimal? oldPrice = null)` — evaluates Below/Above/PercentDrop. Returns false for inactive alerts and for percent-drop with null/zero reference.
  - `CheckAlertsHandler` now stamps `LastTriggeredAt` directly (cooldown bookkeeping; deliberate — `TriggerCount` bumps once in `SendAlertNotificationHandler` via `Trigger()` so concurrent price events don't double-count). `SendAlertNotificationHandler` switched to `alert.Trigger(now)`.
- [x] **ProductUrl** — new methods on `Ophi.Domain.Entities.ProductUrl`:
  - `RecordSuccessfulScrape(price, currency, now)` — sets price/currency, stamps `LastCheckedAt`, clears `LastError`/`FailureCount`/`IsOutOfStock`. Deliberately does NOT touch suspicious state — handlers run health analysis separately and call `MarkSuspicious`/`ClearSuspicious` explicitly.
  - `RecordFailure(error, now)` vs `RecordTransientFailure(error, now)` — the latter skips the `FailureCount++` bump so rate-limit responses don't drag the URL toward auto-pause.
  - `MarkOutOfStock(now)` — returns `bool wasInStock` so callers can publish a one-shot transition notification.
  - `MarkSuspicious(reason)` / `ClearSuspicious()` — pair; clear is a no-op on paused URLs (paused stays paused until explicit `Resume()`).
  - `Pause()` / `Resume()` — `Resume()` also zeroes `FailureCount` + `LastError`.
- [x] **Product** — single-entity status methods only:
  - `MarkActive()` (Pending → Active and Paused → Active), `MarkAsError()`, `Pause()`, `Resume()`.
  - Cross-entity `Product.CurrentPrice = MIN across URLs` invariant **stays out of the entity** because it depends on sibling URLs the entity doesn't load — moved to `ProductPriceAggregator` instead.
- [x] **`Ophi.Domain.Services.ProductPriceAggregator`** — new static class with `ApplyAggregate(Product, IReadOnlyCollection<UrlPrice>)`. Owns the MIN-across-URLs rule: picks the min-priced URL, shifts `Product.CurrentPrice` into `PreviousPrice`, adopts the min URL's currency. Lives in `Ophi.Domain/Services/` because the rule is a domain invariant, not a persistence concern. `UrlPrice` is a `readonly record struct` payload so callers can pass EF projections without materializing full `ProductUrl` entities.
- [x] **Handler migration** — `CheckProductPriceHandler` (the biggest consumer) rewritten to use entity methods + the aggregator. `ScrapeNewProductHandler` uses `MarkActive`/`MarkAsError`/`RecordSuccessfulScrape`/`MarkOutOfStock`/`RecordFailure`. `ResumeProductUrl` (API) now just calls `productUrl.Resume()`.
- [x] **API surface** — `UpdateProduct` switches over the requested status string and calls `product.Pause()` / `product.Resume()` instead of assigning the enum directly. Transition validation preserved verbatim.

**Decisions called out:**

- **Setters stayed `public set` rather than tightening to `private set`.** Test seeding (`new Product { CurrentPrice = 80m }`) and EF Core change tracking both work without ceremony. The discipline of "thou shalt only mutate via methods" is its own future phase if wanted.
- **`CheckAlertsHandler` doesn't use `Alert.Trigger()`.** It stamps `LastTriggeredAt` directly so the cooldown window closes immediately, but defers `TriggerCount++` to `SendAlertNotificationHandler` where it commits atomically with the in-app notification. This preserves the race-window invariant: two concurrent `PriceUpdatedEvent`s can't both observe `TriggerCount == N` and both increment to `N+1`.

**Remaining handler logic (NOT moved onto entities):**

- Cross-URL "all URLs failing → mark product as error" decision in `CheckProductPriceHandler.HandleFailure` — needs a DB query against siblings.
- Suspicious-count → auto-pause threshold decision — needs settings.
- Notification rows — orchestration concern.

Test count: 1366 → 1408 backend (+42: AlertTests 11, ProductUrlTests 14, ProductTests 5, ProductPriceAggregatorTests 6, +1 Resume-budget regression, +5 net across rewired handler tests). Frontend unchanged at 971. **Total: 2337 → 2379.**

---

## Recommended Sequencing (historical)

1. **Confirmed Bugs** + **Phase 1** ✅
2. **Phase 2** (dependency currency) ✅
3. **Phase 2.5** (frontend major bumps) ✅
4. **Phase 3** (backend performance) ✅
5. **Phase 4** (frontend modernization) ✅
6. **Phase 5** (scraping coherence) ✅
7. **Phase 6** (test suite hardening) ✅ (10 items shipped, 5 deferred as Phase 6.Xb)
8. **Phase 7** (polish & hygiene) ✅ (9 items shipped, 1 deferred → Phase 8)
9. **Phase 8** (domain model refactor) ✅
