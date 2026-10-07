# Agent Notes — durable implementation knowledge

Non-derivable "why" knowledge for AI agents and contributors: constraints the code can't show,
decisions with rejected alternatives, and bug classes with their guards. [CLAUDE.md](../CLAUDE.md)
holds the short operational rules; this file holds the depth behind them.

**One owner per fact:** deploy topology is owned by `.claude/skills/redeploy/`, local run topology by
`.claude/skills/dev-stack/`, endpoint shapes by the live OpenAPI document. Link to owners; don't
restate their facts here or anywhere else — restating is how stale contradictions happen.

## Messaging & alerts

- **`CheckAlertsHandler` intentionally stamps `alert.LastTriggeredAt = now` directly** (not via
  `Alert.Trigger()`); `TriggerCount++` happens later in `SendAlertNotificationHandler.Trigger()` to keep
  "decided to fire" separate from "finished firing". This is the documented exception to the
  "mutate via entity methods" rule.
- **Cross-worker alert double-fire is closed by a Postgres `xmin` optimistic-concurrency token on
  `Alert`** (PR #27). Wired provider-conditionally in `OphiDbContext.OnModelCreating`
  (`if (Database.IsNpgsql())`) so the SQLite test tier is unaffected. Npgsql 10 removed
  `UseXminAsConcurrencyToken()` — the replacement is a `uint` shadow property:
  `Property<uint>("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate()`. The migration emits
  **no DDL** (xmin is a system column); the snapshot still records the token.
  - Handler semantics: the `LastTriggeredAt` save in `CheckAlertsHandler` is the claim — on
    `DbUpdateConcurrencyException` it returns `[]` (the winning event fires the whole eligible set; no
    per-alert retry). `SendAlertNotificationHandler` catches the same exception as defense-in-depth and
    must NOT rethrow (retry would persist a duplicate notification). `LocalQueue("events").
    MaximumParallelMessages(1)` remains as an in-process optimization, no longer the race-closer.
  - Testing note: a naive "two concurrent events → one notification" test passes via the cooldown path
    without xmin engaging. The real proofs live in `Ophi.Postgres.Tests/AlertConcurrencyTests`
    (deterministic stale-context approach, no timing races).
- **Alerts are denominated: `Alert.Currency` is stamped from the product at creation, and a
  mismatch makes the alert dormant rather than firing.** A price is a number *and* a denomination.
  `ProductPriceAggregator` re-anchors a product onto another currency when no URL matches its own, so
  a target set as USD 80 was afterwards compared against EUR prices: EUR 75 cleared it, and
  `AlertTriggeredEvent` stamps the *product's* currency, so the email read "€75, below your €80
  target" — a threshold the user never set. (Scrapes from a non-US egress are the usual trigger.)
  - `ShouldTrigger` takes `currentCurrency` as a **required** parameter, deliberately. An optional one
    defaulting to "skip the check" is how a call site silently opts out and reintroduces this; making
    it required means the compiler enumerates every caller. Don't relax it.
  - **`PercentDrop` is exempt from the alert-vs-product guard** — its target is a percentage, which
    survives a re-denomination — but it has its own hole: `(old - new) / old` is meaningless when the
    scrape re-anchored mid-flight. `PriceUpdatedEvent.OldCurrency` (captured with `oldProductPrice`,
    *before* `ApplyAggregate` runs, in both `CheckProductPriceHandler` and `ScrapeNewProductHandler`)
    closes it. Null means "unknown" — only in-flight durable messages across a deploy — and is treated
    as unchanged, since the headline guard already covers the persistent-flip case. It is declared
    last with a null default so queued messages still deserialize.
  - The dormancy rule has one definition, `Alert.IsCurrencyMismatch`; the instance method delegates to
    it and `GetAlerts` calls the static, because the rule has no SQL translation and that query
    projects columns rather than entities. Don't re-implement the comparison per read path.
  - Migration `AddAlertCurrency` **backfills from each alert's product**. The scaffolded `""` column
    default would leave every pre-existing alert denominated in nothing, matching no product — muting
    the entire existing alert set on deploy. `AlertCurrencyBackfillTests` (Postgres tier) covers this;
    a fresh-database migration test cannot, since it has no rows to backfill.
  - **Recovery is `RedenominateAlert`, and it requires a new target rather than carrying the old one
    over.** The old number is an amount in the old currency; reusing it under a new denomination is
    the exact silent re-denomination the dormancy rule exists to catch. Auto-converting is not the
    answer either — that needs an FX rate, and a target is a decision the user made, not a quantity
    to translate. The slice refuses any alert that is not dormant (422 `AlertNotDormant`), which is
    what keeps it from becoming a back-door `UpdateAlert`; there is still no general alert-edit
    endpoint. The one other alert writer is `SetAlertActive` (PATCH, the `active` flag only): resume
    re-checks `MaxAlertsPerUser` (the cap counts *active* alerts, so pause → create → resume would
    bypass it otherwise), and on `DbUpdateConcurrencyException` against the checker's xmin claim it
    reloads and reapplies once, then 409s — `Ophi.Postgres.Tests/SetAlertActiveConcurrencyTests`.
    Back on redenominate: the request carries the currency the user was *shown* and the handler rejects it if the
    product has re-anchored since (422 `CurrencyChanged`), so a target picked against a stale currency
    is never stamped with a denomination the user never saw. `Alert.TargetPrice` and `Alert.Currency`
    are settable only for this path — go through `Alert.Redenominate`.
- **`Alert.TargetPrice` is polymorphic and `AlertTargetFormatter` is its one owner.** The value is an
  amount for Below/Above but a *percentage* for PercentDrop — the type is carried by `Condition`, not
  by the number. The email and Discord templates rendered it as money unconditionally, so a 20%-drop
  alert went out as "Your Target: USD 20.00": a plausible-looking price the user never set. The
  in-app notification text had always got this right, which is exactly the drift that happens when
  three renderers each decide for themselves. Do not re-derive the amount-vs-percent choice inline in
  a template; call the formatter.
  - `Condition` is carried on `SendAlertEmailRequested` / `SendAlertDiscordRequested` (trailing,
    nullable, defaulted — durable-queue compatibility, same pattern as `PriceUpdatedEvent.OldCurrency`).
    Null means "unknown" and falls back to the amount rendering, i.e. the pre-fix behaviour, so a
    deploy doesn't change how in-flight messages read.
  - `SendAlertWebhookRequested` does not carry `TargetPrice` at all and is unaffected.
  - The Discord embed field is named "Target", not "Target Price" — it is not always a price.
  - No test asserted on either rendered target line before, which is how this survived; the guards are
    now in `EmailServiceTests`, `DiscordWebhookServiceTests` and `AlertTargetFormatterTests`.
- **Alert delivery cascade:** `SendAlertNotificationHandler` persists the in-app notification + alert
  state, then returns `Wolverine.OutgoingMessages` with one event per channel:
  `SendAlertEmailRequested` (always), `SendAlertDiscordRequested` (only when user-enabled + URL set —
  gating lives in the orchestrator), `SendAlertWebhookRequested` (always). Channel handlers retry
  independently on the `notifications` queue. `AlertCascadePipelineTests` is the regression guard.
- **The durable Postgres transport is gated on connection-string PRESENCE, not `DB_PROVIDER`** (in
  `Ophi.Worker/Configuration/WolverineConfig.cs`): `DB_PROVIDER` defaults to postgres, so gating on it
  would wire the transport in the Testing env and break integration tests. Reliability is
  `Status=Pending` + the 60s `DispatchPendingProductsAsync` poll backstop, NOT a transactional outbox
  (documented non-goal).
- **Wolverine 6:** core dropped the Roslyn runtime compiler — both hosts need the
  `WolverineFx.RuntimeCompilation` package. Static codegen is off because the API registers handlers
  conditionally on `enableWorker`. `opts.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed`
  is required on both hosts: the sole cause is EF Core's scoped `DbContextOptions<OphiDbContext>`
  lambda, which codegen can't inline — narrow opt-ins (`AlwaysUseServiceLocationFor<...>`) were tried
  and do NOT clear it. Don't restructure handlers to "fix" the warning; the only true escape is
  `IDbContextFactory` everywhere (not worth it).
  - **`WorkerHostBootTests` is the guard** (default tier, no Postgres needed). It boots the
    `SplitWorker` composition and calls `IWolverineRuntime.FindInvoker` per message, which forces
    codegen — bootstrapping alone doesn't, because Wolverine builds executors lazily. Verified by
    deleting the `ServiceLocationPolicy` line: 5 of 6 codegen cases fail while all 609 other
    Infrastructure tests still pass, which is exactly why unit tests never caught this class (handler
    methods are static and tests call them directly).
  - **`FindInvoker` returns a `NoHandlerExecutor` sentinel, not null, for an unhandled message.**
    Asserting "not null" is therefore vacuous — it passes with zero handlers registered. Assert the
    executor is not that sentinel; `WorkerHost_DoesNotHandleLiveUpdate_ItRoutesItToTheApi` is the
    negative control that keeps the theory honest.
- **Cancellation deliberately escapes the scrape handlers into the catch-all retry rule — do not
  give it a `Requeue()` policy.** `PlaywrightScrapingService` aborts on cancellation and rethrows, so
  a host shutdown surfaces as `OperationCanceledException` instead of a scrape failure (which would
  otherwise be budgeted against the URL's auto-pause count). Wolverine's error rules are fall-through,
  so an `OnException<OperationCanceledException>()` rule placed above the catch-all *would* take
  precedence — and would make things worse: `Requeue()` retries immediately, so during shutdown it
  burns every attempt against an already-cancelled token and dead-letters the price check. The
  existing `OnException<Exception>().RetryWithCooldown(30s, 5min)` is what saves it: the cooldown
  never elapses in a dying process, and `UseDurableLocalQueues` persists the envelope for recovery on
  restart. The only cost is an error-level log per in-flight scrape per restart. Accepted.
- **Native AOT was evaluated and rejected** for the Pi (EF Core AOT is experimental; dynamic
  `.Where(...)` composition in `GetProducts` unsupported; migrations not covered). The path for Pi
  resource wins is ReadyToRun + `dotnet ef dbcontext optimize`, and capping Playwright concurrency.

## Pricing & aggregation invariants

- **Exchange rates are display-only, and that is structural.** `ExchangeRate` rows (ECB euro
  reference rates, fetched by the worker's `ExchangeRateRefresher`) are read by exactly one
  backend path, `GET /api/v1/fx-rates`; the conversion itself lives only in the frontend
  (`$lib/utils/fx.ts`, rendered by `ConvertedPrice`). Do not inject rates into
  `ProductPriceAggregator`, alert checking, comparisons or any stored price: alert targets are
  denominated on purpose (see Messaging & alerts), and a converted MIN across currencies would pick a
  "cheapest" store off a mid-market rate the user never pays. The refresher only fetches while some
  user has `DisplayCurrency` set. A currency the ECB does not publish gets no conversion — never a
  guess.

- **A price must be > 0, enforced at the parser boundary.** `PriceParser`'s strip regex
  (`[^\d.,\s -]`) deliberately keeps the ASCII hyphen, so `-$15.00` from a savings/discount
  element parses cleanly to `-15.00`, and `$0.00` placeholders parse to zero. Nothing downstream
  validated this: a non-positive value won the cross-URL MIN, became the product's headline price,
  was written to `PricePoints` as permanent history, and fired every "below X" alert. All four
  `ScrapingResult.Price` assignments (HTTP + Playwright) originate from `PriceParser`, so the single
  `AsPrice` guard covers every route (CSS selector, JSON-LD JSONPath, regex fallback). Returning
  null makes callers fall through to the next selector, which is also why a discount element no
  longer wins over the real price. Do NOT relax this to `>= 0`.
  - The anomaly detector is not a backstop here: at `SuspiciousCount == 1` the handler warns but
    still persists the price.
- **`ProductPriceAggregator.ApplyAggregate` compares only within the product's currency.** Raw
  decimals aren't comparable across currencies — EUR 95 beats USD 100 numerically while costing
  more, and adopting it silently re-denominated the product on every scrape, which then flowed into
  alert comparisons (alerts now carry their own guard too — see *Alerts are denominated* above). An
  older test asserted the opposite behavior (min-priced URL wins on currency); it was rewritten, not
  deleted — don't reintroduce it.
  - When NO URL matches the product's currency, the product re-anchors rather than going priceless —
    but it picks the **dominant currency** (most contributing URLs, ties broken on the currency code)
    and takes the MIN *within* it. Taking the MIN over the whole mixed set is the same bug one level
    down: GBP 20 is not cheaper than EUR 30. Any fallback here must choose a currency before it
    compares two decimals.
- **Paused URLs never contribute to the product price.** `PriceCheckDispatcher` skips
  `Status == Paused`, so a paused URL's price is frozen and only grows staler; letting it define the
  MIN pinned the product to a price that could never update. Both aggregation sites filter it out:
  `CheckProductPriceHandler` and `RemoveProductUrl`.
- **`RemoveProductUrl` must go through the aggregator**, not assign `product.CurrentPrice` directly.
  The direct assignment left `Currency` pointing at the removed URL's currency (a USD listing
  rendering as "€50") and skipped the `PreviousPrice` capture the dashboard's "% change" reads.
  `ApplyAggregate` no-ops on an empty set (so a scrape finding nothing can't clobber a good price),
  so the "no live priced URL left" case is handled explicitly by the caller.
- **`ProductUrl.FailureNotified` latches the max-failures notification per failure streak.** The
  threshold check is `FailureCount >= maxFailures && !FailureNotified`. It used to be `==`, which
  silently skipped the notification/webhook forever once a user lowered `AutoPauseAfterFailures`
  below a URL's existing count (6, 7, 8… never equals 3). `>=` alone would re-notify every cycle —
  the latch is what preserves once-per-streak. Cleared by `RecordSuccessfulScrape`, `MarkOutOfStock`
  and `Resume`. The webhook dispatch reuses the same computed decision, since re-testing the count
  after the latch is set would miss.
  - **`MarkAsError` must NOT be gated on the latch.** Status is idempotent state, not a
    once-per-streak event. Gating it reintroduced the same hole from the other direction: a user who
    *raises* `AutoPauseAfterFailures` mid-streak has already tripped the latch, so the product stayed
    `Active` with every URL dead. Reconcile status on every at-threshold failing check; notify once.
- **`HasPriceAnomaly` is per-URL on `ProductUrl`; the `Product` flag is derived.** Assigning the
  product flag straight from the scrape made it last-writer-wins, so a clean scrape of one URL
  erased an anomaly still present on a sibling that was also feeding the price. Paused URLs are
  excluded from the derivation for the same reason as pricing.

## Scraping helpers: one owner, because duplicates drift

- **`ScrapeHelpers` owns anything both scraping services need.** The regex cache used to be
  duplicated on each service and the copies diverged: the HTTP one compiled patterns with a 5s match
  timeout, the Playwright one with none — so a user-authored `PriceRegexPatterns` entry with
  catastrophic backtracking could pin a worker thread forever on JS-required stores, and the
  `RegexMatchTimeoutException` handler that was supposed to catch it was unreachable dead code.
  `GetOrCreateRegex` now lives in `ScrapeHelpers`. Put shared scraping logic there, not a second copy.
- **`ScrapeHelpers.NormalizeHost` is the only host comparison.** `ScrapeHealthAnalyzer` compared raw
  `Uri.Host` values, so a canonical apex ↔ `www.` redirect read as "different domain" on every
  scrape; `SuspiciousCount` only resets on a clean scrape, so it climbed until the URL auto-paused
  and stopped being scraped or priced at all. Normalize both sides of any host comparison.
- **`AntiBotSignals` is the only definition of "this is a challenge page".** Challenges arrive as
  HTTP 200 with valid HTML, so status classification can't see them and extraction just finds no
  price — reported as `ParseError`, which blames our parser for the site refusing us. The signals
  lived inline in `PlaywrightScrapingService` as two Cloudflare titles, and the HTTP path (which is
  every store without `RequiresJavaScript`) had none at all. Matching is **exact title / whole URL
  path, never substring**: a miss degrades to the old parse error, but a false positive strands a
  working product behind "blocked by anti-bot protection". A signal must fire independently on each
  path — the URL check was briefly inert under Playwright because the resolution wait only watched
  titles, so a block URL under an unlisted title was reported as clear; re-check after waiting, and
  don't wait at all on a block redirect (it never un-redirects). **Every fetch-and-parse entry point
  must call `ScrapingService.DetectChallenge`** — the first version checked only in
  `ScrapeProductAsync`, leaving `ScrapeWithConfigAsync` (which backs `Features/Stores/TestStore`)
  telling users to fix selectors against a page that was never served. `WaitForAntiBot` covered both
  Playwright entry points for free because it already sat on the shared path; the HTTP side had no
  such chokepoint until this helper.
- **Geo gates are not anti-bot, and must not reuse `AntiBot`** — but Best Buy's splash is *not* the
  case to build that on. The country splash ("Best Buy International: Select your Country") is a
  different fact about a different problem, so labelling it `AntiBot` would tell the user something
  false. An earlier version of this note called for a dedicated category on that evidence; **that
  detector cannot fire.** `ScrapingService` issues exactly one request — the product URL it was
  handed — and nothing ever fetches a homepage. The splash was only observed on the homepage; the
  product pages died with dropped connections, which already classify as `NetworkError`. A geo
  category still makes sense if a signal is ever found *on a product page* — Amazon's
  `UnavailableTextPatterns` is exactly that shape — but check the signal is on a page the scraper
  actually requests before adding one.
- **A block that cannot clear from this host pauses the URL, not just the product.** `AntiBot` at the
  failure threshold calls `ProductUrl.Pause()`. Without it a blocked URL sharing a product with a
  healthy one was re-scraped every cycle forever: `product.MarkAsError()` only fires when *every*
  URL is at the threshold, and `PriceCheckDispatcher.BuildCandidateQuery` filters on
  `Product.Status == Active`, so a single-URL product self-limits while a multi-URL one does not.
  For a store with `RequiresJavaScript` that was a Chromium launch per cycle for a request that
  could not succeed. Strictly category-gated — `RateLimited`/`NetworkError`/`ServerError` may clear
  on their own and keep the counting-only behaviour; don't widen it to "any failure". The pause is
  guarded on `Status` so it is a one-time transition and **consumes the failure latch**
  (`MarkFailureNotified`) so the user gets the pause notification instead of the generic one. It
  cannot ride `reachedFailureThreshold`: a URL already past the threshold on another category has
  the latch spent, and would be paused in silence. Self-hosted egress is the operator's, not ours
  (issue #136) — `Resume()` is their way back.
- **JSON-LD matches must be scalars.** `JsonPathExtractor` used to stringify a structural match with
  `ToJsonString().Trim('"')`, so `"price": [10,20]` became `"[10,20]"` → `PriceParser` strips the
  brackets → the European-comma heuristic reads `10,20` as **10.20**, a fabricated price that is
  neither bound. `Extract` and `ExtractAll` now share `MatchesInBlock`, which skips non-`JsonValue`
  matches and scans every match in a block instead of only the first.

## Postgres-only failure classes (SQLite tests are false-green)

CLAUDE.md has the headline rule; these are the known concrete classes:

- **varchar overflow rolls back the whole save and poisons the message.** `Notification.Title` is
  `varchar(200)` but product names run to 500 chars; every title site must go through
  `Notification.BuildTitle(label, name)` (truncates the name portion, keeps the label). SQLite ignores
  varchar lengths, so only a pure-function unit test on the builder guards this. General lesson: any
  bounded column written from a longer source field is this class (PR #58).
- **Trigram search index must match EF's emitted cast.** EF's `lower(col)` over `varchar` becomes
  `lower(("Url")::text)`; an index on `lower("Url")` (no cast) is silently ignored. The only content
  index needed is `IX_ProductUrls_Url_trgm` (pg_trgm GIN) — all other search predicates are user-scoped
  and ride existing `UserId` composite indexes. The migration is raw SQL, kept OUT of the EF model
  (SQLite tier has no pg_trgm). Verify with the real EF SQL (`query.ToQueryString()`), never a
  hand-written predicate (PR #25).
- **EXPLAIN-based tests need steady-state GIN:** after bulk seeding, new GIN entries sit in the
  fastupdate pending list and the planner seq-scans. `VACUUM ANALYZE` each table (one at a time) after
  seeding, seed realistic volume across MANY users so `UserId` is selective, then EXPLAIN the exact EF
  SQL shape.
- **Search escaping:** `LikePattern.Contains(term)` (escapes `\ % _`, lowercases, wraps `%…%`) paired
  with 3-arg `EF.Functions.Like(col.ToLower(), pattern, LikePattern.EscapeChar)` — portable across
  SQLite + Npgsql (`ILIKE` is Npgsql-only and breaks the unit tier).
- **A C# property initializer is not a SQL column default.** `= true` on an entity property applies
  only to entities constructed in .NET, and every test seeds that way — so a migration scaffolded
  with `defaultValue: false` passes the whole suite while every pre-existing row takes the wrong
  value on deploy. Set it with `HasDefaultValue(...)` in the entity configuration (which makes EF
  emit the right `AddColumn`), then read the generated migration to confirm. Guarded for
  `User.EmailNotificationsEnabled` by a Postgres-tier test that inserts a row with raw SQL, omitting
  the column — the only shape that can tell the two defaults apart.
- **Open gap (low risk):** the two `ExecuteUpdate` paths (`ApiKeyAuthenticationHandler.
  TouchLastUsedAsync`, `MarkAllNotificationsRead`) have no Postgres-tier test and bypass
  `BaseEntity.UpdatedAt` stamping (by design for those fields).

## Auth & security invariants

- **An HttpClient whose URL path carries a secret must call `.RemoveAllLoggers()`.** The default
  `IHttpClientFactory` logging handlers write the full request URI at Information, and .NET redacts
  only the query string. Discord (webhook URL is the credential) and Telegram (`/bot{token}/…`) do
  this in `DependencyInjection`; Pushover sends its token in the form body, which is not logged.

- **SecurityStamp rules for new endpoints:** cookie tickets carry an `ophi:security_stamp` claim,
  validated per-request by `SecurityStampGuard` (60s IMemoryCache, no negative caching). Any
  credential/identity mutation must re-issue the cookie via `SignInUserAsync(id, email, name, stamp)`
  in the same request; after rotating the stamp call `stampGuard.Refresh(id, newStamp)`; after deleting
  a user call `stampGuard.Evict(id)`. Handler `Response` records carry the stamp with
  `[property: JsonIgnore]` so it never serializes. (`/auth/me` reads claims only.)
- **API key scopes are enforced by `ApiKeyScopeMiddleware`, not per-endpoint.** Any non-safe method
  (anything but GET/HEAD/OPTIONS/TRACE) from a principal carrying the `api_key_scopes` claim requires
  `write`. Cookie principals have no such claim and are never restricted; reads are allowed for any
  valid key. It is middleware for the same reason `CsrfMiddleware` is — a blanket rule can't be
  forgotten on a new slice, whereas ~70 `RequireAuthorization("write")` calls can. It must stay
  registered **after** `UseAuthentication()` (it reads the principal) **and after `UseHttpMetrics()` /
  `RequestLoggingMiddleware`** — it short-circuits, so registering it above them made its 403s absent
  from both the request log and Prometheus, and a silently-rejected key is the worst kind to debug.
  Unlike CSRF/rate limiting it is deliberately NOT skipped in the Testing environment, because the
  integration tests assert it. Before this, scopes were validated, stored and shown in the UI but
  never checked — a "read-only" key had full write access, which is worse than no control at all.
  - Consequence of a method-based rule: non-mutating `POST` endpoints (`/stores/detect` most
    notably) are refused for read-only keys. Documented in `docs/security.md`; accepted.
- **Cookie policy:** `CookieSecurePolicy.Always` is gated on `IsProduction()` — NOT `IsDevelopment()`,
  which would break the Testing environment.
- **`Login` hashes a decoy password when the email is unknown.** Both branches already returned the
  same message, but only the known-email path ran PBKDF2, so response time leaked account existence.
  Don't "optimize" the decoy verify away. The decoy hash comes from the **injected**
  `IPasswordHasher<User>`, not `new PasswordHasher<User>()`: PBKDF2 verification reads its iteration
  count out of the hash, so a decoy built with default options would keep verifying at the default
  cost after `PasswordHasherOptions` is tuned — reopening the gap while looking correct.
- **CSRF:** `CsrfMiddleware` requires `X-Requested-With` on every non-GET. A 403
  "Missing required request header" from curl means you forgot it, not that auth is broken.
- **Rate limits:** in Development both the global and `auth` policies are effectively unlimited
  (e2e suites register many users from one IP); Production/Testing keep 120/10.
- **Trusting `X-Forwarded-For` is only safe while every path from a trusted address overwrites it.**
  The API's `UseForwardedHeaders` (first in the pipeline) trusts `ForwardedHeaders__KnownIPNetworks`.
  A published API port counts as such a path: docker-proxy connections come from the bridge gateway,
  which is why full compose leaves the setting empty (`:5000` is on all interfaces) and only the Pi
  (port bound to `127.0.0.1`, Caddy in front) sets it. If the hook ever passes client headers through again, any client
  picks its own rate-limit partition and the `auth` limit stops limiting — worse than the old
  single shared bucket. `handle.test.ts` guards the overwrite; `ForwardedHeadersTests` guards the
  API side. Program.cs skips `UseRateLimiter` in Testing, so those tests rebuild the
  forwarded-headers + rate-limit registration on a bare TestServer instead of the app factory.

## API & backend patterns

- **Settings API:** nullable int fields use sentinel `0` = "clear to null", enforced with a `When`
  guard + `Must()` validation. Check-interval cascade:
  `product.CheckIntervalMinutes ?? user.DefaultCheckIntervalMinutes ?? 60`.
- **Metric cardinality:** price gauges (`ophi_product_price_current`, `ophi_product_price_lowest_alltime`)
  carry only `product_id` + `store`. Product names live on the info-metric
  `ophi_product_info{product_id, product_name}` (always 1); Grafana joins via
  `* on(product_id) group_left(product_name) ophi_product_info`. Never add `product_name` back to the
  gauges.
- **Enum → API strings:** domain enums serialize via `Enum.ToApiString()` (lowercases the first letter
  only: `PercentDrop → "percentDrop"`).
- **CSV import** (`Features/Products/ImportProducts.cs`): CsvHelper with `PrepareHeaderForMatch`
  trim+lowercase and silenced `HeaderValidated`/`MissingFieldFound`/`BadDataFound` so optional columns
  are forgiving. The endpoint parses the stream to `ImportRow` records, then dispatches the command.
- **Scraping config order:** store config is loaded BEFORE `FetchPageAsync` so
  `storeConfig.CustomUserAgent` reaches the request. `IPlaywrightBrowserManager.NewPageAsync(string?
  userAgent = null)` — null means a random `BrowserProfiles` profile; `BrowserProfile.IsChromium`
  differentiates Chrome/Edge (Sec-Ch-Ua headers) from Firefox/Safari.
- **API client 2xx handling:** empty-body `202`/`204` responses must not be treated as errors —
  `client.ts` special-cases them (a `Results.Accepted()` with no body once surfaced as a false
  "Failed to retry scrape" toast).
- **TimeProvider:** production code resolves time via injected `TimeProvider.GetUtcNow().UtcDateTime`
  (static Wolverine handlers take it as a method parameter). `OphiDbContext.SaveChangesAsync`
  (BaseEntity stamps) and the `CreateApiKey` validator stay on `DateTime.UtcNow` by design.
- **InternalsVisibleTo map:** `Ophi.Infrastructure` → `Ophi.Infrastructure.Tests`/`Ophi.Api.Tests`/
  `Ophi.Worker`; `Ophi.Worker` → `Ophi.Infrastructure.Tests`; `Ophi.Api` → `Ophi.Api.Tests`.
  (`Ophi.Worker` was added so `ScrapeHealthAnalyzer` can use `ScrapeHelpers.NormalizeHost` rather
  than keep a third copy of host normalization — see the scraping-helper note above.)

## Build & analyzers

- **Every C# warning is a build error** (`Directory.Build.props`: `AnalysisLevel=latest-recommended` +
  `TreatWarningsAsErrors=true`), locally and in CI. Fix order for a CAxxxx: (1) fix the code; (2) if
  it's an EF expression-tree false positive (ToLower/Any/string comparisons inside `Where`/`Select`
  lambdas — the invariant variants don't translate to SQL, and `lower()` must match the pg_trgm index),
  add/extend the documented downgrade in the root `.editorconfig` — never a `#pragma`. Test-convention
  rules (CA1707 etc.) are already suppressed under `[tests/**.cs]`.
- **Roslyn CS8602 false-positive class:** `config?.Member` inside an expression that also contains an
  `await` can poison nullable flow analysis and flag a *later*, provably non-null dereference. Fix by
  removing the misleading `?.` (use `config.X ?? fallback` when only the property is nullable), not by
  null-forgiving the later line.

## Frontend (Svelte 5) gotchas

- **Never read back a `$state`/store in the same `$effect` that writes it** — Svelte proxies assigned
  objects, so `current (proxy) !== newValue (raw)` re-fires the write forever
  (`effect_update_depth_exceeded`). The loop can be conditional on a rarely-taken code path (e.g. only
  when a query param is present), so it looks fine in most flows. For one-shot loader logic use
  `onMount` + the raw `data` prop.
- **`$derived.by()`** for function-based derived values (not `$derived(() => {...})`); no colons or
  slashes in `class:` directives — use string interpolation.
- **`liveRefresh()`** (`$lib/utils/liveRefresh.ts`) is THE pattern for SSE + poll-fallback surfaces;
  the SSE URL is `${API_BASE}/events` — `API_BASE` already contains `/api/v1`, and
  `liveUpdates.test.ts` guards against re-introducing the doubled prefix.
- **CommandPalette must keep its `if (!isOpen) return` keydown guard** — without it Enter/arrows are
  hijacked app-wide.
- Shared building blocks: `shared/Modal.svelte` shell; `menuKeyNav` action for dropdown arrow-key
  a11y; `QuickAlert` class per-row via `Map<productId, QuickAlert>`; lazy Chart.js via
  `$lib/utils/chart.ts` (`loadChart()` keeps Chart.js in its own chunk); prices via `formatPrice()`
  in `$lib/format.ts` (Intl, pinned en-US — tests assert `"$99.99"`).

## Testing recipes

- **Never wait for `networkidle` in e2e** — the app-wide SSE connection keeps the network
  permanently busy on every signed-in page, so it times out on every authenticated `goto` (this
  silently broke most of the pre-#77 suite; fixed in PR #95). Wait for the hydration marker instead:
  the root layout sets `body[data-hydrated]` in `onMount` (`BasePage.waitForPageLoad()` uses it), and
  interactions that trigger fetches wait on the concrete response (`page.waitForResponse`). The same
  marker is the guard against pre-hydration fills/clicks hitting native form/anchor behavior.
- **Settings-area e2e recipe** (standard across all `/settings/*` + `/scrape-health` specs):
  1. Clicks that open modals/toggles wrap in `expect(...).toPass()` (handler hydration).
  2. Text/number fills on fields re-synced by an on-mount `$effect` use the `fillStable` pattern
     (`e2e/pages/account-settings.page.ts`): inside `toPass` — fill → `waitForTimeout(300)` (let the
     hydration clobber fire) → assert the value held. A plain `toHaveValue` retry is NOT enough; it can
     pass in the pre-hydration window and get clobbered after.
  3. Seed and clean up via the API (`TestApi` helpers), and assert durable state (reload, API GET) —
     never transient toasts (auto-dismiss at 3500ms is racy under load).
  4. Non-destructive specs share TEST_USER with settings snapshot-restore in `afterEach` and run
     serial (`test.describe.configure({ mode: 'default' })`) because `fullyParallel` raced the shared
     state; destructive account tests register throwaway users via `context.request.post('/auth/register')`.
- **Wolverine-handler smoke** (deploy verification, non-mutating): bad-credentials login → **401**
  proves codegen + scoped-DbContext service location work in-container; 500 = codegen problem; 403 =
  missing `X-Requested-With`.
- **In-worker scrape probe** — the only trustworthy view of what the scraper sees, because it has
  the deployment's real egress and environment (a desktop browser gets captchas the worker doesn't,
  and vice versa). Pipe a script that requires `/app/.playwright/package` into the worker's bundled
  node: `docker exec -i -e PLAYWRIGHT_BROWSERS_PATH=/app/.playwright-browsers docker-worker-1
  /app/.playwright/node/linux-<x64|arm64>/node -`. A US product suddenly scraping a tiny price from a
  non-US egress is usually a no-buybox page (only accessory/financing prices) until proven otherwise.
- **Internal types in theories:** `TheoryData<T>` with an internal `T` trips CS0050/CS0051 — use
  `IEnumerable<object[]>` and cast inside the test body.
- **FluentValidation:** use `TestValidate()` + `ShouldHaveValidationErrorFor(x => x.Property)`;
  `RuleForEach` type inference needs the `Expression.Convert(body, typeof(IEnumerable<T>))` workaround.
- **Replacing the DbContext in `OphiWebApplicationFactory`** must also strip
  `IDbContextOptionsConfiguration<OphiDbContext>` (EF 10 registers the options lambda there) — else
  `AddInfrastructure`'s postgres lambda still runs under the test host.
- Playwright `Category=Integration` tests (real Chromium) are excluded in CI — run locally, via
  `dotnet test tests/Ophi.Infrastructure.Tests --filter-trait "Category=Integration"`.
- **The runner is Microsoft.Testing.Platform, not VSTest** (xunit.v3 4.0.0 deleted the VSTest bridge,
  and the .NET 10 SDK refuses it: *"Testing with VSTest target is no longer supported"*). Three
  pieces have to stay in sync, and each is easy to half-remove:
  1. `global.json` → `"test": {"runner": "Microsoft.Testing.Platform"}`. This is the .NET 10 opt-in;
     `<TestingPlatformDotnetTestSupport>` is the SDK 8/9 one and layers MTP *on top of* VSTest, i.e.
     it re-enters the exact path the SDK now rejects. Deleting this file silently reverts every test
     project to VSTest. Keep the file free of an `sdk` section — that would pin the SDK version too.
  2. `Directory.Build.props` → `UseMicrosoftTestingPlatformRunner`.
  3. Each test csproj → `<OutputType>Exe</OutputType>`. `Microsoft.NET.Test.Sdk` used to supply this;
     dropping that package without adding it is a build error, not a silent break.
- **The VSTest flags mostly changed spelling; check `--help` on the built test exe, not the docs.**
  `--logger trx` → `--report-xunit-trx` and `--logger console;verbosity=normal` → nothing (MTP's
  `--output` already defaults to `Normal`); `--logger` and `--collect` no longer exist at all, so
  `--collect "XPlat Code Coverage"` went with `coverlet.collector` (`Directory.Packages.props` notes
  what to add back if backend coverage is ever wanted). `--filter`, however, **does**
  survive in xunit.v3 4.0.0 and still takes VSTest syntax — Microsoft's migration guide says
  otherwise, but it predates 4.0.0, and `--filter "Category!=Integration"` demonstrably filters
  here. It is kept only for compatibility: xunit sorts filters into three mutually
  exclusive kinds (VSTest `--filter`, simple `--filter-*`, query `--filter-query`) and you cannot
  mix them, so write new filters in the native `--filter-not-trait` / `--filter-trait` form.
- **A filter that matches nothing is the failure mode to watch.** `--filter-not-trait` correctly
  keeps tests in assemblies that declare the trait nowhere (Ophi.Api.Tests has no `Category` trait
  and still yields all of its tests) — but prove any new filter with `--list-tests`, which prints an
  explicit `Discovered N tests.` per assembly, before trusting a green run. Compare against
  `--list-tests` on `main`, not a number written down here — counts written into docs go stale.
- MTP exits **8** when an assembly runs zero tests, where VSTest exited 0. A filter that empties one
  project therefore fails CI rather than passing quietly — that is the desired behaviour, so don't
  reach for `--ignore-exit-code 8` to silence it.

## CI gates and the bug classes behind them

Each gate exists because its class actually merged green once. Removing one re-opens the class.
Each job runs only when a PR touches paths it can catch (the `changes` job in `ci.yml`); a weekly
scheduled run executes everything. Narrowing a job's paths has the same cost as removing its gate for
those paths.

| Gate (ci.yml) | Bug class it guards |
|---|---|
| Frontend `bun run check` | Valid-JS type errors: calling a non-existent API-client method compiles and 500s at runtime (`getApiKeys` vs `listApiKeys`). Also enforced locally by the svelte-check Stop hook. |
| Frontend `bun run build` (with `VITE_API_URL=/api/v1`) | SvelteKit build-only failures: an illegal non-`_` named export from `+page.ts` passes svelte-check AND vitest but 500s the route and fails the adapter `analyse` step (PR #64 shipped this; #73 added the gate). `client.ts` throws at import time if `VITE_API_URL` is unset. |
| Frontend `bun run lint --max-warnings 0` | ESLint regressions, warnings included. Blocking since #178 cleared the baseline to 0 errors / 0 warnings; before that it ran `continue-on-error`, and earlier still docs claimed lint was a gate when CI never ran it at all. Intentional exceptions take a targeted `eslint-disable` with a reason, not a relaxed gate. |
| `docker` matrix job (build stages; runs only for Dockerfile, root build-config, or `.csproj` changes) | Container-only build breaks: new root build-config files (`Directory.*.props`, `.editorconfig`, `global.json`, `nuget.config`) must be `COPY`d before `dotnet restore` in `docker/Dockerfile.{api,worker,pi}`, and `.editorconfig` globs must be prefix-independent (`**/…`) because the container flattens `src/` (PRs #65/#74/#75). |
| `docker` matrix arm64 entries (worker + Pi runtime, native arm64 runners) | Arch-hardcoded paths in runtime stages: the worker hardcoded `.playwright/node/linux-x64/node`, but `publish -a arm64` only ships `linux-arm64`, so the Pi 5 / Oracle ARM worker image failed to build while amd64 CI stayed green. |
| Backend test job (`--filter-not-trait "Category=Integration"`, Postgres service) | Provider-sensitive behavior — see the Postgres-only classes above. Also **Windows-dev vs Linux-prod divergence**: CI is the only place the suite runs on Linux. `Uri.TryCreate("/blocked", UriKind.Absolute, …)` returns false on Windows but **true** on Linux (a bare POSIX path parses as `file:///blocked`), so a URL matcher without a scheme check goes green locally and wrong in production. A backend test that passes locally and fails only here is this class before it is a flake. |
| Backend vulnerable-package scan (`dotnet list package --vulnerable --include-transitive`) | Known-CVE NuGet packages, transitive included. Fails on any hit. |
| Nightly e2e workflow (skips when `main` has no commits in 25 h) | Runtime-only route breakage the unit/build gates can't see (the #64 dashboard 500 was only caught by a human loading the page). |

Coverage targets (80% backend / 70% frontend) are advisory, measured via `test:coverage` — they are
NOT CI-enforced. Don't claim otherwise in docs.

## Deployment gotchas (why the Docker config looks the way it does)

- **The web image builds/runs adapter-node on Node, not bun** — bun's `node:http` resets proxied POST
  request bodies. The `hooks.server.ts` `/api/*` proxy strips `Content-Encoding`/`Content-Length`/
  `Transfer-Encoding` + hop-by-hop headers, or the browser tries to gunzip an already-decompressed body.
- **Data Protection keys persist to the `ophi-data` volume** (`AuthSetup.cs`, skipped in Testing env);
  without it every `api` recreate logs everyone out. The data dir must be writable by the non-root
  `app` user.
- **`MetricsToken` is required in Production** — the API throws at startup without it.
- Rebuild one service without cycling its dependencies: `docker compose up -d --build --no-deps <svc>`.
- Worker image rebuilds re-download Playwright Chromium (the install layer sits after the publish
  COPY); `NODE_OPTIONS=--dns-result-order=ipv4first` is scoped to that RUN line because IPv6 to the
  Playwright CDN hangs on some hosts.
- Path/privilege specifics of the WSL2 stack are owned by `.claude/skills/redeploy/`.
