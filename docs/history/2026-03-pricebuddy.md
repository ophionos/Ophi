# PriceBuddy Parity Pass

> Reference project: [jez500/pricebuddy](https://github.com/jez500/pricebuddy) (PHP/Laravel/Filament).
> This was a comparison-driven audit of Ophi against a more mature competitor, focused on frontend UX gaps and the backend that supports them.

## Feature Parity Items (Complete ✅)

Feature gaps identified from comparison with PriceBuddy. Note: Ophi already handles locale-aware price parsing for amount extraction; per-store locale/currency is not a gap.

- [x] **JSONPath extraction** — Added JSONPath as a third extraction strategy alongside CSS selectors and regex, useful for stores that expose product data via API responses.
- [x] **Affiliate codes** — Per-store affiliate code/tag injection into tracked URLs, with global enable/disable toggle.
- [x] **Configurable fetch schedule** — Two-level configuration: global default on User (via Settings API + stores page) and per-product override (via UpdateProduct + ProductEditModal). Presets: 15m to 24h. Dispatcher cascades: product → user default → system default (60m). Sends 0 to reset per-product to default.
- [x] **Page fetch delay** — User-configurable delay (seconds) between sequential scrape requests to avoid rate limiting.
- [x] **Scrape cache TTL** — Configurable cache duration to skip re-fetching recently scraped pages.
- [x] **Discord notifications** — Added Discord webhook as a notification channel alongside existing email support.

---

## Feature Gap Analysis: Ophi vs PriceBuddy

PriceBuddy, being a more mature project, revealed several UX gaps. All have since been addressed.

### 1. Multi-URL per Product (Completed)

**PriceBuddy:** A single product can have multiple URLs from different stores. Each URL tracks its own price independently. The product view shows per-store prices, trends, and a combined price history chart with one line per store.

**Implemented in Phase 10:**
- `ProductUrl` entity with independent price tracking per URL
- Product detail page shows per-store price cards, combined chart with one line per store
- "Best price" calculation (lowest across all URLs)
- Add/remove URL endpoints with validation
- Dispatcher sends one scrape command per URL
- ComparisonGroups kept separate for cross-product comparison

### 2. Product Editing & Status Management (Completed)

**PriceBuddy:** Products can be edited (title, image, status, tags, notification thresholds, sort weight, favourite toggle). Products can be paused/unpublished to stop tracking.

**Implemented in Phase 6.1:**
- `PUT /api/v1/products/{id}` endpoint with ownership check and status transition validation
- Pause/resume product tracking (paused products skipped by PriceCheckDispatcher)
- Frontend edit modal on product detail page + pause/resume button on ProductCard

### 3. In-App Notifications (Completed)

**PriceBuddy:** Five notification channels — in-app (database notifications with bell icon), email, Pushover, Gotify, and Apprise (which itself supports 80+ services).

**Implemented in Phase 9.1:**
- `Notification` entity with types: PriceAlert, ScrapeError, System
- Full API: list, count, mark-read, mark-all-read
- Frontend: bell icon in nav bar, dropdown panel, polling every 30s
- Remaining gap: push notification integrations (Pushover, Gotify, Apprise) — post-MVP

### 4. Tags / Improved Product Organization (Completed)

**PriceBuddy:** User-scoped tags with sort weights. Products can have multiple tags. Dashboard groups products by tag. Product list is filterable by tag.

**Implemented in Phase 7:**
- `Tag` entity with name, color, weight; `ProductTag` join entity (many-to-many)
- Full Tag CRUD API + product-tag association endpoints
- `GetProducts` extended with `tagId` filter, `GetProduct` includes tags in response
- Frontend: TagBadge, TagPicker, TagFilterChips, TagModal, TagList components
- Tag management page (`/tags`), tag assignment in product edit modal and detail page
- Dashboard tag filtering alongside status filters

### 5. Dashboard Improvements (Completed)

**PriceBuddy:** Dashboard shows products grouped by tags, favourited products prominently, price trend indicators per product, and summary statistics.

**Implemented in Phase 6.3:**
- Favourite flag with star toggle, favourited products sorted to top
- Trend badges (up/down/lowest) on product cards
- Summary stats bar (total products, alerts, price drops)
- Empty state component with guided first-use experience
- Tag filtering alongside status filters on dashboard

### 6. Search, Sort & Filtering (Completed)

**PriceBuddy:** Product list has text search, tag filtering, status filtering, "lowest in period" filtering, and sort options. Global command palette search (Cmd+K spotlight).

**Implemented in Phase 6.2:**
- Text search across product names
- Filter by status (active, paused, error, pending) and tag
- Sort by name, price, date added, last checked, price change %
- UI: search input, sort dropdown, status chips
- Remaining gap: "price at lowest" filter, command palette — lower priority

### 7. Store Test Page (Completed)

**PriceBuddy:** Dedicated store test page where users enter a URL and see what the scraper extracts (title, price, image) using the store's configured selectors. Shows which strategy matched.

**Implemented in Phase 8.1:**
- `POST /api/v1/stores/test` endpoint — runs scraping with supplied config, returns extracted data
- Frontend modal for URL testing with result display
- Tests covering handler, validator, UI button, and client integration

### 8. Auto Store Creation (Completed)

**PriceBuddy:** When adding a URL for an unknown domain, automatically creates a store config by trying multiple fallback strategies (og:title, og:price, schema.org, common CSS patterns, regex).

**Implemented in Phase 8.2:**
- `AutoCreateStoreService` extracts selectors via OpenGraph, JSON-LD, CSS patterns, regex
- On successful scrape of unknown domain, creates `StoreConfiguration` with `IsAutoCreated = true`
- UI indicator for auto-created stores
- Unit tests for each extraction strategy and end-to-end integration

### 9. Per-Product Alert Thresholds (Completed)

**PriceBuddy:** Each product has `notify_price` (absolute threshold) and `notify_percent` (percentage drop) fields directly on the product. Simple: "notify me when this product drops below $X or by Y%."

**Implemented in Phase 9.2:**
- Inline quick-alert form on product cards with auto-suggested price
- Single-click alert creation for common thresholds
- Coexists with the full alert CRUD system for advanced use cases

### 10. PWA Support (Completed)

**PriceBuddy:** Web app manifest with standalone display mode, 512x512 icon, theme color. Can be installed as a home screen app on mobile.

**Implemented in Phase 11:**
- `manifest.json` with app metadata, SVG icons (192x192, 512x512), standalone display mode
- Service worker: cache-first for static assets, network-first for navigation, skips API routes
- Meta tags: theme-color, apple-touch-icon, description

### 11. Store Import/Export (Completed)

**PriceBuddy:** Stores can be exported as JSON and imported, allowing users to share working store configs.

**Implemented in Phase 11:**
- Export endpoint with JSON file download
- Import endpoint with validation (duplicate storeId, domain overlap, selector format)
- Frontend: ImportStoreModal with file picker and paste textarea

### 12. Logging & System Status (Completed)

**PriceBuddy:** Database-backed scrape logging with retention settings. Dedicated status page showing system health. Log viewer in the admin UI.

**Implemented in Phase 11:**
- `ScrapeLog` entity with success/failure, price, error, duration tracking
- Handlers log scrapes with Stopwatch timing
- `GET /api/v1/products/{id}/scrape-log` API endpoint
- ScrapeHistory component: collapsible, lazy-loaded, success/failure icons
- Data retention service with 24h cooldown, configurable retention (default 30 days)

### 13. Affiliate Link Support (Low Priority — Deferred)

**PriceBuddy:** Automatic affiliate code injection into "Buy" URLs, configurable per store.

**Ophi today:** No affiliate link support beyond the per-store `AffiliateParamName` infrastructure shipped with the parity pass.

**Gap:** If monetization is a goal, adding affiliate codes to outbound "Buy" links is straightforward and non-intrusive. Tracked under [future work](../future.md).

---

## Where Ophi Was Ahead

| Feature | Notes |
|---------|-------|
| **Async product creation** | Ophi's 202 + polling pattern is more responsive than PriceBuddy's synchronous scrape |
| **Alert flexibility** | Ophi's separate Alert entity with 3 condition types (Below, Above, PercentDrop) is more powerful than PriceBuddy's two thresholds on the product |
| **Comparison groups** | Ophi has dedicated comparison groups with multi-product charts; PriceBuddy uses multi-URL on a single product instead |
| **Modern frontend** | SvelteKit + Svelte 5 is a faster, more interactive experience vs Filament's server-rendered Livewire |
| **Tagging system** | Flexible many-to-many tags with color and weight, replacing simple product lists |
| **Scraping engine** | Playwright is more capable than SeleniumBase for JS-heavy sites |
