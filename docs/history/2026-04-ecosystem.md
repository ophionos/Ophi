# Ecosystem Integration Pass

**Goal:** Make Ophi a first-class citizen in the self-hosted ecosystem. Shift from a closed notification tool to an **event-emitting data platform** — composable with the tools tinkerers already run (Grafana, Home Assistant, n8n, ntfy, Node-RED, scripts).

Four phases ordered by impact-to-effort ratio.

## Phase 1 — Generic Outbound Webhooks

**Rationale:** Discord is a dead end — one service, one format. Tinkerers route events to ntfy.sh, Gotify, Home Assistant, n8n, and custom scripts. A pluggable webhook target system makes Ophi the source of truth for price events in a home lab, with all routing decisions left to the user.

### Backend

- [x] New entity `WebhookTarget` (`Id`, `UserId` FK, `Name`, `Url`, `Events` — JSON array: `alert_fired`, `price_changed`, `scrape_failed`, `IsEnabled`, `CreatedAt`)
- [x] EF Core migration for `WebhookTargets` table
- [x] New feature slices: `Features/Webhooks/` — full CRUD (`CreateWebhookTarget`, `UpdateWebhookTarget`, `DeleteWebhookTarget`, `GetWebhookTargets`)
- [x] New service `IWebhookDispatchService` / `WebhookDispatchService`:
  - JSON payload: `{ event, product_id, product_name, url, old_price, new_price, currency, timestamp }`
  - HTTP POST with `Content-Type: application/json`, 10s timeout, 2 retries with exponential backoff (1s, 2s delays; 4xx skips retry)
  - Does not throw on failure — logs warning and continues
- [x] `SendAlertNotificationHandler`: dispatches `alert_fired` event to all matching enabled targets
- [x] `CheckProductPriceHandler`: dispatches `scrape_failed` on consecutive failure threshold
- [x] `DispatchPriceChangedWebhookHandler`: new Wolverine handler for `PriceUpdatedEvent` → dispatches `price_changed` when price actually changes
- [x] `POST /api/v1/webhooks/{id}/test` — fire synthetic test payload, return `{ success, error? }`
- **New files:** `WebhookTarget.cs`, `Features/Webhooks/*.cs`, `WebhookDispatchService.cs`, `IWebhookDispatchService.cs`, `DispatchPriceChangedWebhookHandler.cs`
- **Modified:** `OphiDbContext.cs`, `Program.cs`, `SendAlertNotificationHandler.cs`, `CheckProductPriceHandler.cs`

### Frontend

- [x] **Webhooks** section on Stores/Settings page — list, add/edit modal, test button
- [x] Webhook target list: name, URL (truncated), event badges, enabled badge, test/edit/delete actions
- [x] Add/edit modal: Name, URL, Events (multi-select checkboxes), Enabled toggle
- [x] "Send Test" button per target — calls test endpoint, shows toast on success/error
- **New files:** `WebhookModal.svelte`
- **Modified:** `stores/+page.svelte` (Outbound Webhooks section), `client.ts` (webhook types + API methods)

## Phase 2 — Prometheus Metrics Endpoint

**Rationale:** Tinkerers have Grafana and Prometheus already. Exposing price data as metrics requires zero custom dashboarding code on Ophi's part — they build their own panels in minutes. Also signals to the community that Ophi is a serious self-hosted project.

### Backend

- [x] Add NuGet: `prometheus-net` (Infrastructure), `prometheus-net.AspNetCore` (Api)
- [x] Register `UseHttpMetrics()` and `MapMetrics("/metrics")` in `Program.cs`
- [x] New static class `AppMetrics` with all Prometheus metric definitions:
  - `ophi_product_price_current` (Gauge) — labels: `product_id`, `product_name`, `store`
  - `ophi_product_price_lowest_alltime` (Gauge) — same labels
  - `ophi_scrape_total` (Counter) — labels: `store`, `status` (`success`/`failure`/`out_of_stock`)
  - `ophi_scrape_duration_seconds` (Histogram) — label: `store`
  - `ophi_alert_fired_total` (Counter) — label: `condition` (`below`/`above`/`percentdrop`)
  - `ophi_webhook_dispatch_total` (Counter) — labels: `event`, `status`
- [x] `PriceMetricsCollector` (`BackgroundService`) refreshes price gauges from DB every 60s
- [x] Inline instrumentation in `CheckProductPriceHandler` (scrape counter + duration histogram), `SendAlertNotificationHandler` (alert counter), `WebhookDispatchService` (webhook counter)
- [x] Gate `/metrics` behind optional bearer token (`MetricsToken` in `appsettings.json`) — if unset, endpoint is open
- **New files:** `Infrastructure/Metrics/AppMetrics.cs`, `Infrastructure/Metrics/PriceMetricsCollector.cs`
- **Test files:** `AppMetricsTests.cs` (8 tests), `PriceMetricsCollectorTests.cs` (4 tests), `MetricsEndpointTests.cs` (5 tests)
- **Modified:** `Program.cs`, `CheckProductPriceHandler.cs`, `SendAlertNotificationHandler.cs`, `WebhookDispatchService.cs`, `appsettings.json`

> Note: in 2026-05-17 the metric label set was changed (Phase 7 of the [tech-debt pass](2026-05-tech-debt.md)). `product_name` is now carried on the separate `ophi_product_info{product_id, product_name}` info-metric; the price gauges keep only `product_id` + `store`. Dashboards must join on `product_id`.

### Documentation

- [x] Example Grafana dashboard JSON at `docs/grafana-dashboard.json` (9 panels: price table, price over time, scrape success rate, scrape duration p95, scrapes by status, alerts fired, webhook dispatches, HTTP request rate, HTTP latency)

## Phase 3 — API Key Authentication

**Rationale:** Tinkerers write scripts. Curl from cron jobs, pipe to `jq`, query from Home Assistant templates, automate from iOS Shortcuts. Cookie auth is a dead end for scripting. API keys make Ophi a data source, not just a UI.

### Backend

- [x] New entity `ApiKey` (`Id`, `UserId` FK, `Name`, `KeyHash` — SHA-256 of raw key, `Scopes` — `read`/`write`, `LastUsedAt`, `CreatedAt`, `ExpiresAt` nullable)
- [x] EF Core migration for `ApiKeys` table
- [x] Key generation: 32-byte CSPRNG → `ophi_` prefix + Base64URL string → shown exactly once on creation, never stored raw
- [x] `ApiKeyAuthenticationHandler`: reads `Authorization: Bearer <key>`, SHA-256 hashes it, looks up in DB, sets `ClaimsPrincipal` with user claims — updates `LastUsedAt` fire-and-forget
- [x] Policy scheme (`Smart`) auto-selects cookie or API key auth based on `Authorization: Bearer` header presence
- [x] New feature slices `Features/ApiKeys/`: `CreateApiKey` (returns raw key once in response), `ListApiKeys`, `DeleteApiKey`
- [x] `GET /api/v1/products/export?format=csv|json` — flat export: `name, url, current_price, lowest_price, target_price, currency, tags, last_checked`
- [x] `POST /api/v1/products/import` — bulk CSV import (columns: `url`, `name?`, `target_price?`, `tags?`); creates products + alerts + tags, enqueues scrape jobs, returns `{ added, skipped, errors[] }`
- **New files:** `ApiKey.cs`, `ApiKeyConfiguration.cs`, `ApiKeyAuthenticationHandler.cs`, `Features/ApiKeys/CreateApiKey.cs`, `ListApiKeys.cs`, `DeleteApiKey.cs`, `Features/Products/ExportProducts.cs`, `ImportProducts.cs`
- **Modified:** `User.cs`, `OphiDbContext.cs`, `UserConfiguration.cs`, `Program.cs` (dual auth scheme), `ProductValidationRules.cs` (exposed `IsValidHttpUrl`), `Ophi.Api.csproj` (InternalsVisibleTo)
- **Test files:** `ApiKeyAuthenticationHandlerTests.cs` (3 tests), `CreateApiKeyTests.cs` (8 tests), `ListApiKeysTests.cs` (3 tests), `DeleteApiKeyTests.cs` (3 tests), `ImportProductsCsvParserTests.cs` (6 tests), `ApiKeyEndpointsTests.cs` (8 integration tests), `ExportImportEndpointsTests.cs` (8 integration tests)

### Frontend

- [x] API Keys section on stores/settings page — list with scopes badges, created/last-used dates, revoke button
- [x] Create key modal: name + scope checkboxes + optional expiration → on success shows raw key in copy-once dialog with warning
- [x] Import/Export section: Export CSV, Export JSON, Import CSV buttons with file picker
- **New files:** `ApiKeyCreateModal.svelte`, `ApiKeyCreateModal.test.ts` (7 tests)
- **Modified:** `stores/+page.svelte`, `client.ts` (API key + import/export types and methods)

## Phase 4 — Scrape Health Dashboard

**Rationale:** Self-hosters run this for months unattended. The silent failure mode — bad data, missed price drops, parser breakage after a store redesign — is worse than no tracking. Surfacing scrape health builds long-term trust and enables self-healing before the user even notices a problem.

### Backend

- [x] `ScrapeLog` entity extended with `StoreDomain` field + composite index on `(StoreDomain, CreatedAt)` — retained for 30 days by `DataRetentionService`
- [x] EF Core migration `20260403022034_AddScrapeHealthDashboard` for `StoreDomain`, `HasPriceAnomaly`, user settings columns
- [x] `ScrapeLog` entries written after every scrape attempt in `CheckProductPriceHandler` and `ScrapeNewProductHandler` (success, failure, OOS paths)
- [x] **Anomaly detection**: user-configurable `AnomalyThresholdPercent` (percent → ratio) overrides global `PriceAnomalyThreshold`; `ScrapeHealthAnalyzer.AnalyzePriceAnomaly()` sets `Product.HasPriceAnomaly` flag; clears on clean scrape
- [x] **Auto-pause on consecutive failures**: user-configurable `AutoPauseAfterFailures` overrides global `MaxFailuresBeforeError`; sets product to Error status + fires `scrape_failed` webhook + creates in-app `Notification`
- [x] New query `GetScrapeHealth`: aggregate per-domain stats — total scrapes (7d), success rate, p50/p95 duration, last failure message, scrapes today, summary with healthy/degraded/unhealthy domain counts
- [x] `GetScrapeLog` (existing from Phase 11) serves product-level scrape history with `StoreDomain` field
- [x] New settings fields on `User`: `AnomalyThresholdPercent` (int, nullable), `AutoPauseAfterFailures` (int, nullable) — with validation (10–500%, 1–50, 0 = reset to default)
- **New files:** `Features/Scraping/GetScrapeHealth.cs`
- **Modified:** `ScrapeLog.cs` (+StoreDomain), `Product.cs` (+HasPriceAnomaly), `User.cs` (+settings), `CheckProductPriceHandler.cs` (user-aware thresholds), `ScrapeNewProductHandler.cs` (+StoreDomain), `ScrapeHealthAnalyzer.cs` (public `AnalyzePriceAnomaly`), `GetSettings.cs`, `UpdateSettings.cs`, `GetProduct.cs`, `GetProducts.cs`, `GetScrapeLog.cs`, `ScrapeLogConfiguration.cs`, `OphiDbContextModelSnapshot.cs`, `Program.cs`, `AppMetrics.cs`
- **Test files:** `GetScrapeHealthHandlerTests.cs`, `GetSettingsHandlerTests.cs` (+anomaly/auto-pause), `UpdateSettingsValidatorTests.cs` (+anomaly/auto-pause), `MetricsEndpointTests.cs`, `CheckProductPriceHandlerTests.cs` (+5 new tests: anomaly flag, user threshold, auto-pause)

### Frontend

- [x] New **/scrape-health** page (linked from nav): per-domain health table with summary stats
- [x] `ScrapeHealthTable.svelte` component — domain, success rate badge (green >95%, amber 80–95%, red <80%), scrapes today, p50/p95 duration, last error (truncated), last scrape timestamp
- [x] Product detail page: collapsible **Scrape History** panel (existing `ScrapeHistory.svelte` from Phase 11, extended with `StoreDomain`)
- [x] Anomaly flag on product card (amber badge) and product detail (amber warning banner "Price anomaly detected")
- [x] Settings inputs for `Anomaly threshold %` and `Auto-pause after N failures` on stores page
- **New files:** `ScrapeHealthTable.svelte`, `ScrapeHealthTable.test.ts`, `scrape-health/+page.svelte`
- **Modified:** `products/[id]/+page.svelte` (anomaly banner), `ProductCard.svelte` (anomaly badge), `ProductCard.test.ts`, `stores/+page.svelte` (settings inputs), `+layout.svelte` (nav link), `client.ts` (API types + methods)

## Implementation Order

```
Phase 1 (Webhooks) → Phase 2 (Prometheus) → Phase 3 (API Keys) → Phase 4 (Scrape Health)
```

Each phase shipped independently. Phase 1 delivered the most immediate ecosystem value. Phase 4 delivered long-term reliability for unattended deployments.
