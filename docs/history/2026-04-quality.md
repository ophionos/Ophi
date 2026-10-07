# Quality & Performance Pass

**Goal:** Harden the codebase for production reliability. Fix query performance issues, add observability, extend rate limiting, and improve frontend resilience. Ordered by operational impact.

## Phase 1 — Logging & Observability

**Rationale:** Feature handlers had zero logging. Production debugging is impossible without request-level tracing. Single highest-ROI improvement.

### Backend

- [x] Inject `ILogger<T>` into all Wolverine handlers in `Features/` (58 handlers)
- [x] Log at entry/exit of expensive operations (DB queries, external calls)
- [x] Log warnings on user errors (validation failures, not found)
- [x] Log errors on exceptions with correlation context (userId, productId)
- [x] Add structured logging fields for filtering (e.g., `{ProductId}`, `{UserId}`)
- **Modified:** All `Features/{Domain}/{Action}.cs` handler files (Auth, Products, Tags, Stores, Alerts, Comparisons, Notifications, Webhooks, Settings), 47 test files updated with `NullLogger<T>.Instance`

## Phase 2 — Query Performance & Indexes

**Rationale:** N+1 queries in `GetAlerts`, in-memory sorting in `GetComparisonGroup`, and missing indexes will cause visible slowdowns as data grows.

### Backend

- [x] **GetAlerts N+1**: Verified — EF Core translates `.Select()` navigation property access (`a.Product.Name`, `a.Product.CurrentPrice`) to SQL JOINs; no `.Include()` needed
- [x] **GetComparisonGroup**: Added `.OrderBy(p => p.CurrentPrice == null).ThenBy(p => (double?)p.CurrentPrice)` in EF query — products now sorted by price ascending (nulls last) at DB level
- [x] **PriceCheckDispatcher**: Verified — `.Select(pu => pu.Id)` already placed before `.ToListAsync()` in both `DispatchPendingProductsAsync` and `BuildDueProductUrlQuery`
- [x] **GetProducts sparkline**: Verified — sparkline/price-point loading already conditional on `IncludeSparkline == true`
- [x] **DB indexes verified**: All indexes already present via FK conventions and explicit configurations:
  - `Alerts`: `ProductId` (FK auto-index), `{UserId, ProductId, IsActive}` (explicit composite)
  - `Notifications`: `{UserId, IsRead}` (explicit composite), `ProductId` (explicit), `CreatedAt` (explicit)
  - `WebhookTargets`: `UserId` (explicit)
- [x] No migration needed — no index gaps found
- **Modified:** `GetComparisonGroup.cs`, `GetComparisonGroupHandlerTests.cs` (2 new sorting tests)

## Phase 3 — Rate Limiting & Security Hardening

**Rationale:** Only auth endpoints were rate-limited. Product creation, scraping triggers, and webhook registration had no per-user limits, enabling abuse.

### Backend

- [x] **Per-user rate limits**: product creation (50/hour), alert creation (100/hour), store creation (20/hour), webhook creation (10/hour) — sliding window with 6 segments, partitioned by authenticated user ID (fallback to IP)
- [x] **Tighten CSP**: Removed `'unsafe-inline'` from `style-src`, restricted `img-src` to `'self' data:` (no `https:` wildcard)
- [x] **SameSite cookie**: Tightened from `Lax` to `Strict` — safe because SvelteKit proxies all API calls same-site, no cross-origin OAuth/payment flows
- [x] Log rate limit violations with user context (user ID, IP, path) via `ILoggerFactory`
- **New files:** `RateLimitPolicies.cs` (policy constants, partition key logic, rejection handler)
- **Test files:** `SecurityHeadersMiddlewareTests.cs` (9 tests), `RateLimitPolicyTests.cs` (19 tests)
- **Modified:** `Program.cs` (4 named rate limit policies, enhanced OnRejected, SameSite Strict), `SecurityHeadersMiddleware.cs` (CSP tightening), `AddProduct.cs`, `CreateProduct.cs`, `CreateAlert.cs`, `CreateStore.cs`, `CreateWebhookTarget.cs` (`.RequireRateLimiting()`)

## Phase 4 — Frontend Resilience

**Rationale:** Several components showed blank states during loading, gave generic error messages, and lacked accessibility labels for screen readers.

### Frontend

- [x] **Loading states**: Skeleton loaders in `ProductTable` (mobile cards + desktop rows via `loading` prop), `PriceChart` (spinner during history loading via `loading` prop, wired to `historyLoading` in product detail page)
- [x] **Error differentiation**: `ApiError` integration in `AddProductForm`, `ProductTable` quick-alert, `ProductCard` quick-alert — field-level feedback for 400/422, retry button for network/server errors. Dashboard `handleAddProduct` lets errors propagate to form.
- [x] **Accessibility**: `role="region"` + `aria-label` on quick-alert regions in `ProductTable` and `ProductCard`, `aria-describedby` on chart canvas linking to hidden sr-only data summary, `aria-invalid` + `aria-describedby` on `AddProductForm` input, `role="img"` + `aria-label` on canvas
- [x] **Null safety**: `product.tags ?? []` guards in `ProductTable`, `ProductTableMobileCard`, `ProductCard` (both `#if` and `#each`)
- **Modified:** `ProductTable.svelte`, `ProductCard.svelte`, `PriceChart.svelte`, `AddProductForm.svelte`, `ProductTableMobileCard.svelte`, `dashboard/+page.svelte`, `products/[id]/+page.svelte`
- **Test files:** `AddProductForm.test.ts` (+8 tests), `ProductTable.test.ts` (+8 tests), `ProductCard.test.ts` (+4 tests), `PriceChart.test.ts` (+8 tests)

## Phase 5 — Health Checks & Diagnostics

**Rationale:** `/health` only checked DB connectivity. A healthy DB with a dead message bus or broken scraping service gives false confidence.

### Backend

- [x] Extend health check to verify Wolverine message bus is responsive
- [x] Add scraping health: check recent scrape success rate (warn if <80% in last hour)
- [x] Add uptime and version info to health response
- [x] Separate liveness (`/health/live` — process is up) from readiness (`/health/ready` — all dependencies healthy)
- [x] Legacy `/health` endpoint preserved for backwards compatibility (maps to readiness)
- **New files:** `Common/HealthChecks/DatabaseHealthCheck.cs`, `WolverineHealthCheck.cs`, `ScrapeHealthCheck.cs`, `HealthCheckResponseWriter.cs`
- **Test files:** `DatabaseHealthCheckTests.cs` (2 tests), `WolverineHealthCheckTests.cs` (3 tests), `ScrapeHealthCheckTests.cs` (7 tests), `HealthCheckResponseWriterTests.cs` (5 tests), `HealthEndpointTests.cs` (9 tests — rewritten for live/ready split)
- **Modified:** `Program.cs` (ASP.NET Health Checks framework registration, 3 mapped endpoints)

## Implementation Order

```
Phase 1 (Logging) → Phase 2 (Queries/Indexes) → Phase 3 (Rate Limits/Security) → Phase 4 (Frontend) → Phase 5 (Health Checks)
```

Each phase shipped independently. Phase 1 had the highest operational ROI. Phase 2 prevents performance degradation at scale.
