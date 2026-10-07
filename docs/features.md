# Ophi: Features

## Product Tracking

**Operations:** Add via URL, auto-extract metadata, manual selector config, edit (name, image, status), pause/resume, delete, organize with tags

**Key Behaviors:**
- URL validation and duplicate detection
- Auto-detect price selectors using common patterns
- Fallback to user-provided CSS selectors
- Image extraction and caching
- Pause/resume tracking (paused products excluded from price checks)
- Status transitions: only Active ↔ Paused allowed via user action

## Price Scraping Engine

**Operations:** Fetch page, parse HTML, extract price, normalize format, detect failures

**Key Behaviors:**
- Hybrid: HTTP (AngleSharp) first, Playwright fallback for JS sites
- User-agent rotation
- Request throttling per domain
- Retry with exponential backoff
- Currency detection from meta tags (Open Graph, Schema.org)
- Optional display currency (`/settings/account`): prices also show "≈ £142.97" converted with the ECB
  daily reference rates, native price primary; stale rates (>4 days) are labelled with their date.
  Display only — never used for alerts, comparisons or the best-price rollup.
- Store-specific adapters with optimized selectors
- `RequiresJavaScript` flag for automatic browser scraping

**Tuned adapters:** Amazon, eBay (`CodeStoreConfigProvider`). Every other domain scrapes through the
generic selector set in `CommonSelectors` — Open Graph, Schema.org, and common price classes — which
covers most e-commerce pages but is not per-store tuned.

### Which stores you can scrape depends on where you host

Ophi is self-hosted, so scrapes leave from *your* network. Some retailers refuse traffic by origin,
and no setting changes that — it is a property of the connection, not of the app. Two instances of
Ophi running the same code will reach different sets of stores.

Probed from a residential connection in the EU, with an eBay control loading normally in the same
run:

| Site | Homepage | Product page | Mechanism |
|---|---|---|---|
| Walmart | 200, real | ❌ `/blocked` — "Robot or human?" | PerimeterX challenge, on both the Playwright and plain-HTTP paths |
| Target | 200, real | ❌ unreachable | Search returns "no match" — soft mitigation rather than a hard block |
| Best Buy | 200, country splash | ❌ connection dropped | Geo splash; clearing it still leaves every product-page connection dropped |

The generic fallback does not help: no product page loads at all. From a US connection these may work
fine. **If a store fails for you, try it from the machine running the worker before assuming a bug** —
the same URL in your desktop browser proves nothing, because your browser is not what scrapes.

What Ophi does about it: a challenge page is reported as "Blocked by anti-bot protection" rather than
a parse error, and a URL that keeps hitting one is **paused** instead of retried forever. Resume it if
your network changes.

**Adding an adapter for a store you can reach:** save the product page as
`tests/Ophi.Infrastructure.Tests/Scraping/Fixtures/{store}-product.html` and add a case to
`StoreConfigFixtureTests`. That runs a `StoreConfig` against saved HTML with no network, so you can
contribute and verify a store the maintainers cannot load. A passing fixture proves the config is
internally coherent, not that the store still works — a fixture is a snapshot of the day it was taken.

## Price History & Analytics

**Operations:** Record snapshots, generate charts, calculate stats (min/max/avg)

**Key Behaviors:**
- Configurable check intervals
- Interactive charts with 7d/30d/90d filters
- Price change indicators
- Data retention cleanup

## Price Comparison

**Operations:** Link related products, display side-by-side, highlight best price

**Key Behaviors:**
- Manual product linking via comparison groups
- Multi-line chart visualization
- Best price badge highlighting

## Alerts & Notifications

**Operations:** Create threshold alerts, fan out across channels, track history

**Alert Conditions:** `below`, `above`, `percentDrop`

**Delivery Channels:** in-app notifications, email, Discord (per-user webhook), Telegram and Pushover
(operator bot/app via `TELEGRAM_BOT_TOKEN` / `PUSHOVER_APP_TOKEN`; each user saves only a chat id /
user key, never echoed back), outbound webhooks — each channel retries independently under
Wolverine policies. Telegram and Pushover send plain text only, so a product name cannot inject
formatting; their settings cards are hidden when the server has no token.

Email and Discord are per-account opt-outs (`User.EmailNotificationsEnabled`,
`User.DiscordNotificationsEnabled`); email defaults to on. Opting out silences that channel only —
the in-app notification and the other channels still fire. Password-reset mail and the SMTP test
send ignore the email opt-out: neither is an alert.

**Key Behaviors:**
- Cooldown to prevent spam
- Email switched off per account from Settings → Notifications. There is no unsubscribe link in
  the alert email itself; the mail carries a "View Product" link only.
- `/alerts`: every alert across products — filter (active / paused / dormant / fired in 7 days), sort, pause/resume, bulk pause/resume/delete. Pausing keeps trigger history; resuming counts against the active-alert cap.
- Cross-worker double-fire prevention (Postgres optimistic concurrency on `Alert`)

## Store Configuration

**Operations:** View all configs, create/update/delete custom configs, import/export as JSON

**Key Behaviors:**
- User configs override built-in for matching domains
- Domain pattern matching (exact, subdomain, www-stripped)
- CSS selectors with attribute extraction (`img|src`)
- Caching with invalidation on changes
- Affiliate-tag injection on outbound product links

## Account Management

**Operations:** Change password, update name/email, delete account — via API and the `/settings/account` page (user menu → Account)

**Key Behaviors:**
- Password change rotates the user's security stamp — every other session is signed out
  immediately; the calling session gets a re-issued cookie
- Email change requires the current password (it's the login credential); name-only edits don't
- Account deletion is a hard delete gated by the password: all owned data (products, alerts,
  notifications, tags, comparison groups, store configs, webhooks, API keys) cascades
- All account endpoints sit behind the `auth` rate-limit policy

## Integrations & Operations

**Operations:** Script the data, observe scrape health, stay live in the UI

**Key Behaviors:**
- **API keys** — bearer tokens (`ophi_...`) valid on every endpoint, with optional scopes/expiry
- **Outbound webhooks** — POST events to user-supplied URLs (SSRF-validated)
- **Prometheus `/metrics`** + Grafana dashboard (price gauges + scrape metrics)
- **Scrape-health dashboard** — per-domain success rate, anomaly detection, auto-pause after N failures
- **Live updates** — app-wide Server-Sent Events (`/events`) patch the UI on scrape completion, with a
  polling fallback when SSE can't connect
- **CSV product import/export**
- **Full account backup** (`/settings/data`): one JSON file with settings, tags, groups, stores, products, full price history and alerts; credentials and outbound webhooks are never included. Restore is merge-only (adds, never overwrites; already-tracked URLs are skipped; settings are restored only into an account with no products yet). The compose web service sets `BODY_SIZE_LIMIT=26M` so uploads over adapter-node's 512K default reach the API.
