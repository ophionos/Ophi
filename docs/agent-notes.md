# Agent Notes — durable implementation knowledge

Non-derivable "why" knowledge: constraints the code can't show, rejected alternatives, and bug classes
with their guards. [CLAUDE.md](../CLAUDE.md) holds the operational rules and names the owner of each
other kind of fact. Link to owners; don't restate them here.

## Messaging & alerts

- **`CheckAlertsHandler` stamps `alert.LastTriggeredAt = now` directly**, not via `Alert.Trigger()`;
  `TriggerCount++` happens later in `SendAlertNotificationHandler`, keeping "decided to fire" separate
  from "finished firing". This is the documented exception to "mutate via entity methods".
- **Cross-worker double-fire is closed by a Postgres `xmin` concurrency token on `Alert`**, wired only
  when `Database.IsNpgsql()` so the SQLite tier is unaffected. Npgsql 10 removed
  `UseXminAsConcurrencyToken()`; the replacement is a `uint` shadow property
  (`Property<uint>("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate()`), and its migration
  emits no DDL.
  - The `LastTriggeredAt` save in `CheckAlertsHandler` is the claim: on `DbUpdateConcurrencyException`
    it returns `[]` (the winner fires the whole eligible set). `SendAlertNotificationHandler` catches
    the same exception and must NOT rethrow — a retry would persist a duplicate notification.
    `LocalQueue("events").MaximumParallelMessages(1)` is only an in-process optimization.
  - A naive "two concurrent events → one notification" test passes via the cooldown without xmin
    engaging. The real proofs are `Ophi.Postgres.Tests/AlertConcurrencyTests` (deterministic
    stale-context approach).
- **Alerts are denominated: `Alert.Currency` is stamped from the product at creation, and a mismatch
  makes the alert dormant instead of firing.** `ProductPriceAggregator` can re-anchor a product onto
  another currency; without the guard a USD 80 target was compared against EUR prices and the email
  read "€75, below your €80 target".
  - `ShouldTrigger` takes `currentCurrency` as a **required** parameter so the compiler enumerates
    every caller. An optional "skip the check" default is how a call site silently opts out.
  - `PercentDrop` is exempt from the alert-vs-product guard (a percentage survives re-denomination),
    but `(old - new) / old` is meaningless across a mid-flight re-anchor. `PriceUpdatedEvent.OldCurrency`
    (captured before `ApplyAggregate`, in `CheckProductPriceHandler` and `ScrapeNewProductHandler`)
    closes that. Null means unknown and is treated as unchanged; it is declared last with a null
    default so queued messages still deserialize.
  - The dormancy rule has one definition, `Alert.IsCurrencyMismatch`. `GetAlerts` calls the static
    because that query projects columns. Don't re-implement the comparison per read path.
  - Migration `AddAlertCurrency` backfills from each alert's product; the scaffolded `""` default would
    have muted every existing alert. `AlertCurrencyBackfillTests` (Postgres tier) guards it.
  - **Recovery is `RedenominateAlert`, and it requires a new target.** Reusing the old number under a
    new currency is the silent re-denomination the rule exists to catch; auto-converting needs an FX
    rate and overrides a user decision. It refuses non-dormant alerts (422 `AlertNotDormant`) so it is
    not a back-door `UpdateAlert`, and it rejects a request whose shown currency is stale (422
    `CurrencyChanged`). Go through `Alert.Redenominate`.
  - The only other alert writer is `SetAlertActive` (the `active` flag). Resume re-checks
    `MaxAlertsPerUser` (the cap counts active alerts, so pause → create → resume would bypass it), and
    on a concurrency conflict with the checker's xmin claim it reloads and reapplies once, then 409s
    (`SetAlertActiveConcurrencyTests`).
- **Alert delivery cascade:** `SendAlertNotificationHandler` persists the in-app notification and
  alert state, then returns one message per channel: email (if the user's email switch is on),
  Discord / Telegram / Pushover (switch on **and** recipient set), webhook (always), plus a
  `LiveUpdate` for the bell. Channel handlers retry independently. `AlertCascadePipelineTests` guards it.
- **The durable Postgres transport is gated on connection-string PRESENCE, not `DB_PROVIDER`**
  (`WolverineConfig`). `DB_PROVIDER` defaults to postgres, so gating on it would wire the transport
  into the Testing env. Reliability is `Status=Pending` + the `DispatchPendingProductsAsync` poll, not a
  transactional outbox (a deliberate non-goal).
- **SSE is the live-update transport; the polls stay as the `!connected` fallback.** The
  `scrape-notifications` hop is competing-consumer, which is only correct with one API replica.
  Scaling the API horizontally needs Postgres LISTEN/NOTIFY there (`WolverineConfig.ScrapeNotificationQueue`).
- **Wolverine 6 codegen:** both hosts need `WolverineFx.RuntimeCompilation`; static codegen is off
  because the API registers handlers conditionally on `enableWorker`. `ServiceLocationPolicy.AlwaysAllowed`
  is required on both hosts because EF Core's scoped `DbContextOptions<OphiDbContext>` lambda can't be
  inlined. Narrow opt-ins (`AlwaysUseServiceLocationFor<...>`) do NOT clear it; the only real escape is
  `IDbContextFactory` everywhere (not worth it).
  - **`WorkerHostBootTests` is the guard.** It boots `SplitWorker` and calls `FindInvoker` per message,
    which forces codegen (bootstrapping alone doesn't). Unit tests can never catch this class: handlers
    are static methods that tests call directly.
  - `FindInvoker` returns a `NoHandlerExecutor` sentinel, not null, for an unhandled message. Assert
    against the sentinel; "not null" is vacuous.
- **Cancellation deliberately escapes the scrape handlers into the catch-all retry rule — do not give
  it a `Requeue()` policy.** A host shutdown surfaces as `OperationCanceledException`, not a scrape
  failure (which would count toward auto-pause). `Requeue()` retries immediately, burning every attempt
  against a cancelled token and dead-lettering the check. The catch-all `RetryWithCooldown` never
  elapses in a dying process, and `UseDurableLocalQueues` recovers the envelope on restart. Cost: one
  error log per in-flight scrape per restart. Accepted.
- **Native AOT was evaluated and rejected** (EF Core AOT is experimental; the dynamic `.Where(...)`
  composition in `GetProducts` and migrations are unsupported).

## Pricing & aggregation invariants

- **Exchange rates are display-only, and that is structural.** `ExchangeRate` rows (ECB, fetched by
  `ExchangeRateRefresher` only while some user has `DisplayCurrency` set) are read by one backend path,
  `GET /api/v1/fx-rates`; conversion happens only in the frontend (`$lib/utils/fx.ts`, `ConvertedPrice`).
  Never feed rates into `ProductPriceAggregator`, alert checks, comparisons or stored prices: targets
  are denominated on purpose, and a converted MIN would pick a "cheapest" store off a mid-market rate
  the user never pays. A currency the ECB doesn't publish gets no conversion, never a guess.
- **A price must be > 0, enforced at the parser boundary.** `PriceParser` keeps the ASCII hyphen, so
  `-$15.00` (a discount element) parses to `-15.00` and `$0.00` placeholders to zero; downstream, a
  non-positive value won the MIN, became history, and fired every "below" alert. Every
  `ScrapingResult.Price` originates in `PriceParser`, so the single `AsPrice` guard covers all routes,
  and returning null makes callers try the next selector. Do NOT relax to `>= 0`. The anomaly
  detector is no backstop: at `SuspiciousCount == 1` it warns but persists.
- **`ProductPriceAggregator.ApplyAggregate` compares only within the product's currency.** EUR 95
  beats USD 100 numerically while costing more. When no URL matches, the product re-anchors onto the
  **dominant** currency (most URLs, ties by code) and takes the MIN within it. Any fallback must
  choose a currency before comparing two decimals.
- **Paused URLs never contribute to the product price** — their price is frozen. Both aggregation
  sites (`CheckProductPriceHandler`, `RemoveProductUrl`) filter them out.
- **`RemoveProductUrl` must go through the aggregator.** Direct assignment left `Currency` pointing at
  the removed URL and skipped the `PreviousPrice` capture. `ApplyAggregate` no-ops on an empty set, so
  the caller handles "no live priced URL left" explicitly.
- **`ProductUrl.FailureNotified` latches the max-failures notification per streak.** The check is
  `FailureCount >= maxFailures && !FailureNotified`; `==` skipped the notification forever once a user
  lowered `AutoPauseAfterFailures` below a URL's count. Cleared by `RecordSuccessfulScrape`,
  `MarkOutOfStock` and `Resume`. The webhook dispatch reuses the same computed decision.
  - **`MarkAsError` must NOT be gated on the latch.** Status is idempotent state: reconcile it on every
    at-threshold failing check; notify once.
- **`HasPriceAnomaly` is per-URL; the `Product` flag is derived** (excluding paused URLs). Assigning
  the product flag from one scrape erased a sibling URL's anomaly.

## One owner, because duplicates drift

- **`ScrapeHelpers` owns logic both scraping services need.** Duplicated regex caches diverged (only
  one had the 5 s match timeout), so a backtracking user regex could pin a worker thread on
  JS-required stores. `ScrapeHealthAnalyzer` in `Ophi.Worker` reaches `ScrapeHelpers` through
  `InternalsVisibleTo` rather than keep its own copy.
- **`ScrapeHelpers.NormalizeHost` is the only host comparison.** A raw `Uri.Host` comparison read an
  apex ↔ `www.` redirect as a domain change, climbed `SuspiciousCount`, and auto-paused healthy URLs.
- **`AntiBotSignals` is the only definition of a challenge page.** Challenges arrive as HTTP 200, so
  without it they surface as `ParseError` and blame our parser. Match **exact title / whole URL path,
  never substring** — a false positive strands a working product. Each signal must fire independently
  on both the HTTP and Playwright paths, and **every fetch-and-parse entry point must call
  `ScrapingService.DetectChallenge`** (including `ScrapeWithConfigAsync`, behind `TestStore`).
- **Geo gates are not anti-bot; don't reuse `AntiBot` for them.** Add a geo category only for a signal
  observed on the product page itself — the scraper requests only that URL, never a homepage.
- **An `AntiBot` failure at the threshold pauses the URL, not just the product.** `MarkAsError` fires
  only when every URL is at the threshold, and the dispatcher filters on product status, so a blocked
  URL beside a healthy sibling was re-scraped (for `RequiresJavaScript`, a Chromium launch) every
  cycle. Strictly category-gated — `RateLimited`/`NetworkError`/`ServerError` can clear on their own.
  The pause consumes the failure latch so the user gets the pause notification, and `Resume()` is
  the way back.
- **JSON-LD matches must be scalars.** Stringifying `"price": [10,20]` produced `10,20`, which the
  European-comma heuristic read as 10.20. `JsonPathExtractor.MatchesInBlock` skips non-`JsonValue`
  matches.
- **`AlertTargetFormatter` owns the amount-vs-percent rendering of `Alert.TargetPrice`.** The value
  is a percentage for `PercentDrop`; templates that decided for themselves sent "Your Target: USD 20.00"
  for a 20 % drop. `Condition` rides the `SendAlert*Requested` messages as a trailing nullable (null →
  amount, the old rendering, for in-flight messages). The Discord field is "Target", not "Target Price".

## Postgres-only failure classes (SQLite tests are false-green)

`Ophi.Postgres.Tests` uses `POSTGRES_TEST_CONNECTION` (an admin connection; it creates and drops its
own `ophi_test_{guid}` DB) or else Testcontainers. With neither it fails rather than skips, so a
missing Postgres can't silently drop provider coverage. Known classes:

- **varchar overflow rolls back the save and poisons the message.** `Notification.Title` is
  `varchar(200)` but product names run longer; every title goes through `Notification.BuildTitle`.
  Any bounded column written from a longer source field is this class.
- **The trigram index must match EF's emitted cast.** EF emits `lower(("Url")::text)`; an index on
  `lower("Url")` is ignored. `IX_ProductUrls_Url_trgm` is raw SQL kept out of the EF model (SQLite has
  no pg_trgm). Verify with `query.ToQueryString()`, never a hand-written predicate.
- **EXPLAIN-based tests need steady-state GIN:** `VACUUM ANALYZE` each table after seeding, seed many
  users so `UserId` is selective, and EXPLAIN the exact EF SQL.
- **Search escaping:** `LikePattern.Contains(term)` with 3-arg `EF.Functions.Like(..., LikePattern.EscapeChar)`
  is portable; `ILIKE` is Npgsql-only and breaks the unit tier.
- **A C# property initializer is not a SQL column default.** Tests construct entities in .NET, so a
  migration with the wrong `defaultValue` passes while existing rows get the wrong value. Use
  `HasDefaultValue(...)` and read the generated migration. `User.EmailNotificationsEnabled` is guarded
  by a raw-SQL insert test.
- `ExecuteUpdate` paths (`ApiKeyAuthenticationHandler.TouchLastUsedAsync`, `MarkAllNotificationsRead`,
  `PriceCheckDispatcher`) bypass `BaseEntity.UpdatedAt` stamping by design.

## Auth & security invariants

Behavior and trust model: [security.md](security.md). The invariants a change can break:

- **An HttpClient whose URL path carries a secret must call `.RemoveAllLoggers()`** — default logging
  writes the full URI and redacts only the query. Discord and Telegram do this in `DependencyInjection`.
- **SecurityStamp rules for new endpoints:** any credential/identity mutation re-issues the cookie via
  `SignInUserAsync(id, email, name, stamp)` in the same request; after rotating call
  `stampGuard.Refresh(id, newStamp)`; after deleting a user call `stampGuard.Evict(id)`. Response records
  carry the stamp with `[property: JsonIgnore]`.
- **`ApiKeyScopeMiddleware` is middleware, not per-endpoint authorization**, so it can't be forgotten
  on a new slice. It must run after `UseAuthentication()` and after `UseHttpMetrics()` /
  `RequestLoggingMiddleware` (its 403s must be logged and counted). Unlike CSRF and rate limiting it is
  NOT skipped in Testing.
- **Cookie `Secure` policy is gated on `IsProduction()`**, not `!IsDevelopment()`, which would break Testing.
- **`Login` verifies a decoy hash for an unknown email** so response time doesn't reveal accounts. The
  decoy comes from the injected `IPasswordHasher<User>` so it tracks tuned iteration counts.
- **CSRF:** a 403 "Missing required request header" from curl means no `X-Requested-With`.
- **Rate limits** are effectively unlimited in Development (e2e registers many users from one IP).
  Testing skips `UseRateLimiter`, so `ForwardedHeadersTests` rebuild the forwarded-headers + rate-limit
  registration on a bare TestServer.
- **Trusting `X-Forwarded-For` is safe only while every path from a trusted address overwrites it.**
  If the SvelteKit hook ever passes client headers through, any client picks its own partition and the
  `auth` limit stops limiting. `handle.test.ts` and `ForwardedHeadersTests` guard both sides.

## API & backend patterns

- **Settings API:** nullable int fields use sentinel `0` = "clear to null" (a `When` guard + `Must()`).
  Check-interval cascade: `product.CheckIntervalMinutes ?? user.DefaultCheckIntervalMinutes ?? 60`.
- **Metric cardinality:** price gauges carry only `product_id` + `store`; names live on
  `ophi_product_info{product_id, product_name}`, joined in Grafana with
  `* on(product_id) group_left(product_name) ophi_product_info`. Never add `product_name` to the gauges.
- **Enum → API strings:** `Enum.ToApiString()` lowercases the first letter only (`PercentDrop → "percentDrop"`).
- **CSV import:** CsvHelper with trim+lowercase header matching and silenced
  `HeaderValidated`/`MissingFieldFound`/`BadDataFound`, so optional columns are forgiving.
- **Scraping config order:** the store config loads BEFORE `FetchPageAsync` so `CustomUserAgent` reaches
  the request. `NewPageAsync(userAgent: null)` picks a random `BrowserProfiles` profile.
- **Empty 2xx:** `client.ts` special-cases body-less `202`/`204`; treating them as errors once showed a
  false "Failed to retry scrape" toast.
- **TimeProvider:** production code uses injected `TimeProvider` (static Wolverine handlers take it as a
  parameter). `OphiDbContext.SaveChangesAsync` and the `CreateApiKey` validator use `DateTime.UtcNow`
  by design.

## Build & analyzers

- **Every C# warning is an error** (`Directory.Build.props`). For a CAxxxx: fix the code; if it is an
  EF expression-tree false positive (culture-invariant string APIs don't translate, and `lower()` must
  match the trigram index), extend the documented downgrade in `.editorconfig` — never a `#pragma`.
- **Roslyn CS8602 false positive:** `config?.Member` in an expression with an `await` can poison flow
  analysis for a later non-null dereference. Remove the misleading `?.`; don't null-forgive the later line.

## Frontend (Svelte 5) gotchas

- **Never read back a `$state`/store in the same `$effect` that writes it** — proxies make
  `current !== newValue` re-fire forever (`effect_update_depth_exceeded`), often only on a rare path.
  For one-shot loader logic use `onMount` + the raw `data` prop.
- **`$derived.by()`** for function-based derived values; no colons or slashes in `class:` directives.
- **`liveRefresh()`** (`$lib/utils/liveRefresh.ts`) is THE pattern for SSE + poll-fallback surfaces.
  The SSE URL is `${API_BASE}/events` — `API_BASE` already contains `/api/v1` (`liveUpdates.test.ts`
  guards the doubled prefix).
- **CommandPalette must keep its `if (!isOpen) return` keydown guard**, or Enter/arrows are hijacked app-wide.
- Shared building blocks: `shared/Modal.svelte`; `shared/SectionCard.svelte` (pass the lucide icon via
  its `icon` prop, and reuse the icon the app already uses for that concept); `menuKeyNav` for dropdown
  a11y; one `QuickAlert` per row in a `Map<productId, QuickAlert>`; `loadChart()` keeps Chart.js in its
  own chunk; `formatPrice()` in `$lib/format.ts` is pinned to en-US (tests assert `"$99.99"`).

## Testing recipes

- **Never wait for `networkidle` in e2e** — the app-wide SSE connection keeps the network busy on every
  signed-in page. Wait for `body[data-hydrated]` (set in the root layout's `onMount`;
  `BasePage.waitForPageLoad()` uses it) and for concrete responses (`page.waitForResponse`). The marker
  also stops pre-hydration clicks hitting native form behavior.
- **Settings-area e2e recipe** (`/settings/*`, `/scrape-health`):
  1. Clicks that open modals/toggles go inside `expect(...).toPass()`.
  2. Fills on fields re-synced by an on-mount `$effect` use `fillStable`
     (`e2e/pages/account-settings.page.ts`): fill → `waitForTimeout(300)` → assert the value held, all
     inside `toPass`. A plain `toHaveValue` retry can pass before hydration clobbers it.
  3. Seed and clean up via `TestApi`; assert durable state (reload, API GET), never toasts.
  4. Non-destructive specs share TEST_USER with settings snapshot-restore in `afterEach` and run serial;
     destructive account tests register throwaway users.
- **Wolverine-handler smoke** (non-mutating): a bad-credentials login → **401** proves codegen and
  scoped-DbContext service location work in the container; 500 = codegen; 403 = missing `X-Requested-With`.
- **In-worker scrape probe** — the only trustworthy view of what the scraper sees, since a desktop
  browser has a different network and fingerprint. Pipe a script that requires `/app/.playwright/package`
  into the worker's bundled node: `docker exec -i -e PLAYWRIGHT_BROWSERS_PATH=/app/.playwright-browsers
  <worker-container> /app/.playwright/node/linux-<x64|arm64>/node -`. A suspiciously small price is
  usually a page without a buy box (only accessory/financing prices).
- **Internal types in theories:** `TheoryData<T>` with an internal `T` trips CS0050/CS0051 — use
  `IEnumerable<object[]>` and cast.
- **FluentValidation:** `TestValidate()` + `ShouldHaveValidationErrorFor`; `RuleForEach` inference needs
  the `Expression.Convert(body, typeof(IEnumerable<T>))` workaround.
- **Replacing the DbContext in `OphiWebApplicationFactory`** must also remove
  `IDbContextOptionsConfiguration<OphiDbContext>`, or the postgres options lambda still runs.
- **The runner is Microsoft.Testing.Platform** (xunit.v3 4 dropped the VSTest bridge, and the .NET 10
  SDK rejects VSTest). Three pieces must stay in sync: `global.json` `"test": {"runner": ...}` (keep it
  free of an `sdk` section), `UseMicrosoftTestingPlatformRunner` in `Directory.Build.props`, and
  `<OutputType>Exe</OutputType>` in each test csproj. Flag spellings are documented in the `ci.yml`
  Test step; check `--help` on the built test exe, not online guides.
- **A filter that matches nothing is the failure mode to watch.** Prove a new filter with
  `--list-tests` (`Discovered N tests.`) against `main`, not a number written in a doc. MTP exits 8
  when an assembly runs zero tests; don't silence it with `--ignore-exit-code 8`.

## CI gates and the bug classes behind them

Each gate exists because its class once merged green. Each job runs only when a PR touches paths it can
catch (the `changes` job in `ci.yml`); a weekly run executes everything. Narrowing a job's paths removes
its gate for those paths.

| Gate (ci.yml) | Bug class it guards |
|---|---|
| Frontend `bun run check` | Valid-JS type errors: a non-existent API-client method compiles and 500s at runtime (`getApiKeys` vs `listApiKeys`). Also run locally by the svelte-check Stop hook. |
| Frontend `bun run build` (`VITE_API_URL=/api/v1`) | Build-only failures: an illegal non-`_` named export from `+page.ts` passes svelte-check and vitest but 500s the route and fails the adapter `analyse` step. `client.ts` throws at import if `VITE_API_URL` is unset. |
| Frontend `bun run lint --max-warnings 0` | ESLint regressions, warnings included. Exceptions take a targeted `eslint-disable` with a reason, not a relaxed gate. |
| `docker` matrix (Dockerfile, root build-config or `.csproj` changes) | Container-only breaks: root build-config files (`Directory.*.props`, `.editorconfig`, `global.json`, `nuget.config`) must be `COPY`d before `dotnet restore` in every `docker/Dockerfile.*`, and `.editorconfig` globs must be prefix-independent (`**/…`) because the container flattens `src/`. |
| Backend tests (`--filter-not-trait "Category=Integration"`, Postgres service) | Provider-sensitive behavior (above), and **Windows-dev vs Linux-prod divergence**: `Uri.TryCreate("/blocked", UriKind.Absolute, …)` is false on Windows but true on Linux (`file:///blocked`). A backend test that fails only in CI is this class before it is a flake. |
| Backend vulnerable-package scan | Known-CVE NuGet packages, transitive included. |
| Nightly e2e (`e2e.yml`; skips when `main` has no commits in 25 h) | Runtime-only route breakage the unit and build gates can't see. |

Coverage targets are advisory, not CI-enforced.

## Deployment gotchas (why the Docker config looks the way it does)

- **The web image runs adapter-node on Node, not bun** — bun's `node:http` resets proxied POST bodies.
  The `/api/*` proxy in `$lib/server/handle.ts` strips `Content-Encoding`/`Content-Length`/
  `Transfer-Encoding` and hop-by-hop headers, or the browser gunzips an already-decompressed body.
- **Data Protection keys persist to the `ophi-data` volume** (`AuthSetup.cs`, skipped in Testing);
  without it every `api` recreate logs everyone out. The data dir must be writable by the non-root user.
- **Runtime stages must not hardcode an arch.** The worker's Playwright node path follows `TARGETARCH`
  (`linux-x64` does not exist in an arm64 publish). CI builds amd64 only, so nothing guards this.
- Rebuild one service without cycling its dependencies: `docker compose up -d --build --no-deps <svc>`.
- Worker image rebuilds re-download Playwright Chromium (the install layer follows the publish COPY).
  `NODE_OPTIONS=--dns-result-order=ipv4first` is scoped to that RUN line because IPv6 to the Playwright
  CDN hangs on some networks.
