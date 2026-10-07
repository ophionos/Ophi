# UX Refinement Plan

> ✅ **All phases complete (2026-09-25).** Archived; kept for the review findings and the B-1–B-3
> decisions (formats, exclusions, merge rules, FX isolation).

Outcome of a code-only review (2026-09-25) after the 2026-06 UX plans shipped. Layout density is
done; the remaining gaps are one missing surface (a cross-product alerts view), accessibility
consistency, and page-level states that only the dashboard gets right. The backlog items at the end
carry UX weight but need their own PRD before any code.

Work each phase TDD-first; update the status table and archive to `history/` as phases land.

## Status

| Phase | Title | Status |
|---|---|---|
| UXR-1 | Accessibility quick fixes (announced form errors, reduced-motion view transitions) | ✅ Complete (2026-09-25) |
| UXR-2 | Split the product detail page into components | ✅ Complete (2026-09-25) |
| UXR-3 | `/alerts` page + pause/resume | ✅ Complete (2026-09-25) |
| UXR-4 | Shared empty states + navigation progress | ✅ Complete (2026-09-25) |
| UXR-5 | Live visual review (desktop, mobile, dark) | ✅ Complete (2026-09-25) |
| UXR-6 | Review follow-ups (7 small layout fixes) | ✅ Complete (2026-09-25) |
| B-1 | Account backup bundle (export/import) | ✅ Complete (2026-09-25) |
| B-2 | Telegram + Pushover channels | ✅ Complete (2026-09-25) |
| B-3 | Display-currency conversion | ✅ Complete (2026-09-25) |

Order rationale: UXR-2 before UXR-3 because the alert row extracted in UXR-2 is what `/alerts`
renders. UXR-4 after UXR-3 so `/alerts` gets the shared empty state in the same sweep. UXR-5 last
so it reviews the finished surfaces; its findings become a follow-up phase, not scope creep here.

---

## UXR-1 — Accessibility quick fixes

1. **Form errors are not announced.** Only `auth/login` and `auth/register` use `role="alert"`.
   Everywhere else an error is a bare `<p class="text-red-…">`, which a screen reader does not
   announce. Sites (`{#if …error}` blocks): `AlertForm`, `RedenominateAlertForm`,
   `ApiKeyCreateModal`, `ComparisonForm`, `AddProductForm`, `AddUrlModal`, `CreateProductModal`,
   `CustomFieldsEditor`, `ProductEditModal`, `ImportStoreModal`, `StoreForm`, `TestStoreModal`,
   `TagModal`, `WebhookModal`, `NotificationBell`, `auth/forgot-password`, `auth/reset-password`,
   `routes/stores`, `routes/scrape-health`, `routes/dashboard`.
   **Fix:** a `shared/FormError.svelte` (`role="alert"`, the existing red text styling, optional
   `id` so inputs can point `aria-describedby` at it) and replace the ad-hoc blocks. Keep
   `AddProductForm`'s retry button as a snippet slot, not a special case. `CommandPalette`'s
   search error is a live status, not a form error — use `role="status"` there.
2. **View transitions ignore reduced motion.** The global rule at `app.css:183` targets `*`, which
   does not match `::view-transition-old/new(root)` (`app.css:147`), so page navigation still
   animates. **Fix:** add those pseudo-elements with `animation: none` inside the same media query.

**As shipped:** components with bespoke layouts (`TestStoreModal`'s icon row, `NotificationBell`'s
centered retry, the dashboard/stores/scrape-health load-error cards) got `role="alert"` on the
message `<p>` instead of `FormError`. Field errors in `CreateProductModal`, `ProductEditModal` and
`TagModal` are now linked to their inputs with `aria-invalid` + `aria-describedby`. Server field
errors in `AlertForm`/`StoreForm` got `role="alert"`; the auth pages' field-error `<ul>`s are wrapped
in a `role="alert"` div (a role on the `<ul>` itself would drop list semantics). The
`settings/account` password hint is `aria-live="polite"` — it changes per keystroke, so an alert
would be noisy. Settings pages report failures via toast, which is already a live region.
Visual change: the error boxes in `CreateProductModal`, `ProductEditModal` and `TagModal` gained the
red border and `text-red-600` the other boxes already had.

**Test coverage as shipped:** role assertions in the suites of `FormError`, `AlertForm`,
`ApiKeyCreateModal`, `ComparisonForm`, `AddProductForm`, `AddUrlModal`, `CreateProductModal` (incl.
accessible-description link), `CustomFieldsEditor`, `ImportStoreModal`, `StoreForm`, `ProductCard`,
`ProductTable`, `NotificationBell`, `CommandPalette` (`role="status"`). Not unit-tested:
`RedenominateAlertForm`, `TagModal`, `WebhookModal` (no suites), `ProductEditModal` and
`TestStoreModal` (suites don't exercise error paths), and the route-level auth/load errors (no route
unit tests — e2e territory).

**Tests:** `FormError.test.ts` (role, text, `id`); one assertion per migrated component that the
error renders under `role="alert"` (extend existing suites, don't add new files per component).
The CSS fix has no unit test surface — confirm in UXR-5 with `emulateMedia({ reducedMotion })`.

## UXR-2 — Split the product detail page

`routes/products/[id]/+page.svelte` is 1014 lines: 25 handlers plus hero, stats, URLs, alerts,
custom fields, comparison group and danger zone inline. Extract by section, keeping the page as the
owner of `product` state and the API calls (children get data + callbacks, no `api` imports):

| New component | Source lines (approx.) |
|---|---|
| `products/ProductHero.svelte` | 515–592 (image, price story, meta, tags) |
| `products/PriceStats.svelte` | 593–694 (stat row, per-URL price rows) |
| `alerts/AlertRow.svelte` + `alerts/AlertList.svelte` | 767–870 (row incl. dormant/redenominate state) |
| `products/ComparisonGroupPanel.svelte` | 896–945 |

`getConditionLabel` and `getStatusBadgeClass` move with their only consumers. Pure refactor — no
visual change. **`AlertRow` gets an optional `showProduct` prop** (product name + link) now, because
UXR-3 needs it and adding it later means re-testing the detail page twice.

**As shipped:** page 1015 → 623 lines. `AlertRow` takes an optional `product` ({id, name}) instead of a `showProduct` flag — the row needs the name and id anyway — and owns its own deleting / redenominating UI state (the page's `deletingAlertId` / `redenominatingAlertId` are gone; `onDelete`/`onRedenominate` return promises the row awaits). Two small fixes rode along: the empty-alerts tip said "Add Alert" for a button labelled "Create Alert", and the comparison-group `<select>` had no label.

**Tests:** move the alert-section assertions from the page suite into `AlertList.test.ts`; the page
suite keeps integration-level checks. Full frontend suite + `bun run check` + `bun run build`
(route file changed).

## UXR-3 — `/alerts` page + pause/resume

**Why:** `api.getAlerts()` exists and `GET /api/v1/alerts` already returns everything a list needs
(`productName`, `active`, `lastTriggered`, `hasCurrencyMismatch`), but no UI calls it. Users can only
see alerts one product at a time; the dashboard's "has alerts" filter lists products, not alerts.

### Backend — `SetAlertActive` slice

There is no way to pause an alert today: `Alert.IsActive` defaults to `true` and nothing sets it
false. Add `Features/Alerts/SetAlertActive.cs` (`PATCH /api/v1/alerts/{id}` with `{ active }`) and
domain methods `Alert.Pause()` / `Alert.Resume()`.

- **Resume must enforce `MaxAlertsPerUser`.** `CreateAlert` counts *active* alerts against the cap
  (`CreateAlert.cs:80`). Without the same check on resume, pause → create → resume bypasses it.
  422 `MaxAlertsReached`, same code as create.
- Ownership check → 404 for another user's alert (match `DeleteAlert`).
- Scoped to `active` only. This is not a general `UpdateAlert`; target edits still go through
  delete + create (dormant alerts through `RedenominateAlert` — see `docs/agent-notes.md`).
- `ShouldTrigger` already returns false when inactive (`Alert.cs:133`), so the checker needs no change.
- **This is a new writer on a row with a Postgres `xmin` concurrency token** (`docs/agent-notes.md`
  § Messaging & alerts). `CheckAlertsHandler`'s `LastTriggeredAt` save is the fire claim, so the two
  can race. If the checker saves first, the toggle gets `DbUpdateConcurrencyException`: reload and
  reapply once (the flag is idempotent and doesn't conflict with a claim), then 409. If the toggle
  saves first, the checker's claim loses and returns `[]` — the alert doesn't fire, which is correct
  for a pause. No API slice handles this exception today, so without the retry it surfaces as a 500.
  A fire already claimed before the pause still delivers; say so in the UI copy only if UXR-5 shows
  it confuses anyone.
- Rejected: a separate `/pause` + `/resume` pair — two slices for one boolean.
- When this ships, update the agent-notes line "there is still no general alert-edit endpoint" to
  name `SetAlertActive` as the one other writer and why it stays scoped to `active`.

### Frontend

- `routes/alerts/+page.ts` loader (`api.withFetch(fetch).getAlerts()`), `+page.svelte` renders
  `AlertList` with `showProduct`.
- Filter chips: All / Active / Paused / Dormant (currency mismatch) / Fired in last 7 days. URL-driven
  (`?status=`) like the dashboard filters (UX-2). Client-side filtering is fine here — the set is
  capped per user, unlike products.
- Sort: last fired (default), product name, created.
- Row actions: pause/resume toggle, delete (via `ConfirmModal`), redenominate for dormant rows.
- Bulk select → pause / resume / delete, with the dashboard's `Promise.allSettled` + "N of M failed"
  toast pattern (`dashboard/+page.svelte:233`).
- Live refresh: re-load on the `notification` SSE event. `SendAlertNotificationHandler` publishes it
  on every fire, after `LastTriggeredAt` is stamped, so "last fired" is current when the reload runs.
- Entry points: top nav link (desktop + mobile menu in `+layout.svelte`) and a `CommandPalette`
  static item. The dashboard stats-bar "alerts" count stays a filter (`DashboardStatsBar.svelte:36`,
  "products with alerts" is still useful there); add a small "View all" link beside it rather than
  repurposing the button.
- Icons come from `docs/design/icon-vocabulary.md` (alerts = the `Bell` already used on the detail
  page); same rule for UXR-4's empty-state icons.
- Detail page: `AlertRow` gains the same pause toggle.
- Client: `api.setAlertActive(id, active)` on the `api` singleton.

**As shipped:** the "Active" chip excludes dormant alerts (flagged active but unable to fire); a row's status line now reads "Paused" instead of "Inactive"; live refresh falls back to a 30 s poll while SSE is down. Mutation check: removing the reload from the retry makes the Postgres test fail with the 409.

**Tests:** backend — handler tests for pause, resume, resume at cap (422), other user's alert (404);
domain tests for `Pause`/`Resume`; **a Postgres-tier test in `Ophi.Postgres.Tests`** using the
stale-context approach from `AlertConcurrencyTests` (SQLite has no xmin, so the retry path is
untestable there). Frontend — loader test, filter/sort from URL, bulk partial-failure
toast, empty state. `bun run build` with `VITE_API_URL=/api/v1` (new route).

## UXR-4 — Shared empty states + navigation progress

1. **Empty states.** `products/EmptyState.svelte` is dashboard-specific (hard-coded copy and icon).
   Comparisons, stores, tags, scrape-health and comparison detail render plain gray text ("No
   stores configured"). **Fix:** move it to `shared/EmptyState.svelte` with `icon`, `title`,
   `description` and an optional action snippet; the dashboard passes its current copy. Each page
   gets a real next action (e.g. tags → "Create tag", scrape-health → "Scrapes appear after your
   first product check"). Tighten the `py-20` padding for in-page use; keep the large variant for
   the dashboard.
2. **Navigation progress, not skeletons.** Every route loads through `+page.ts`, so SvelteKit holds
   the old page until the loader resolves — skeletons would never render on client navigation. The
   actual gap is that a slow loader gives no feedback after a click. **Fix:** a thin top progress
   bar in `+layout.svelte` driven by `navigating` from `$app/state`, shown after a ~150 ms delay so
   fast navigations don't flash. Respects reduced motion via the global rule.
   Rejected: streaming loaders + per-page skeletons — more code on every page for the same outcome.

**As shipped:** `EmptyState` moved to `shared/` (`icon`, optional `badgeIcon`, `title`, `description`, `size`, action as children); the old `products/EmptyState` and its unused `onAddClick` are gone. Empty-state actions use distinct labels ("Create your first tag", not a second "Create Tag") so two same-named buttons never sit on one page. `NavigationProgress` is prop-driven (`active={navigating.to !== null}`) for testability; the `$app/state` test mock's `navigating` is now an object like the real one.

**Tests:** `EmptyState.test.ts` covers props and the action snippet; each page suite asserts its
empty state; progress bar test with a mocked `navigating`.

## UXR-5 — Live visual review

Hands-on pass via `/dev-stack` with the seeded demo account, driven by Playwright at 1440×900 and
390×844, light and dark, plus `reducedMotion: 'reduce'` (confirms UXR-1 #2). Cover every route
including the new `/alerts`. Output: findings appended to this doc as a UXR-6 phase, each confirmed
against source, same format as the 2026-06 review.

**Done (2026-09-25)** — native `/dev-stack` (API :5041 + vite :5173 + `ophi-pg-demo`), seeded
account, Playwright at 1440×900 and 390×844, light and dark. Screenshots in `shots/uxr-*.png`.

Verified live:
- `/alerts` end to end against the real API: filter counts, pause (persisted), select-all → bulk
  resume ("2 alerts resumed", persisted), mobile/dark layout.
- Login error renders as `<p role="alert">` inside the `FormError` box.
- `NavigationProgress` in real SvelteKit, with the alerts API delayed 1.2 s: hidden at 80 ms, visible
  and animating at 400 ms, gone after load.
- Reduced motion: before the fix, a view transition ran old/new crossfades (4 × 150 ms) plus the
  `::view-transition-group(root)` animation; with `reduce` it now runs **none**.
- Detail page after the UXR-2 split, desktop and mobile; no horizontal overflow at 390 px.
- Full e2e suite (`--workers=1`, as CI; `@external` excluded): **155 passed**.

Fixed during the review:
- **Reduced motion missed `::view-transition-group(root)`** — added to the UXR-1 rule.
- **Card-in-card empty states** inside `SectionCard` bodies (scrape-health, comparison detail) —
  `EmptyState` gained `framed={false}`.
- **`scripts/seed-demo.sql` inserted alerts with no `Currency`**, so every seeded alert showed as
  dormant with a blank currency (the script predates `AddAlertCurrency`). Now stamps `'USD'`.
- **e2e page objects**: `deleteAlertByIndex` clicked the only button in an alert row (rows now have
  two); the tags empty-state copy changed. Plus a pre-existing flake: the add-product-to-group test
  selected by the prefix `E2E Product A`, which matched leftovers from other runs/workers — now uses
  the exact name it created.

## UXR-6 — Review follow-ups

Confirmed against source; none block anything. All pre-date this plan. **All seven shipped
(2026-09-25)**; the tags e2e page object moved to the new `stat-*` test ids. The hero date now uses
`dateStyle: medium, timeStyle: short`, and `SectionCard` headers wrap app-wide, not just on the
detail page.

1. **Tags page stat cards** (`routes/tags/+page.svelte:107`, `grid md:grid-cols-2`): two full-width
   cards for single digits, stacked on mobile — the Comparisons problem the 2026-06 review fixed with
   the slim inline strip. Reuse that strip.
2. **Tags header button wraps** to "Create / Tag" at 390 px (title + subtitle + button share one
   `flex justify-between` row). Add `whitespace-nowrap shrink-0` to the button.
3. **Price History header wraps** on mobile: the four range buttons sit in `SectionCard`'s
   `justify-between` header (`SectionCard.svelte:107`), squeezing the title onto two lines. Let the
   header wrap (`flex-wrap`) or move the range buttons into the body on small screens.
4. **Hero meta line** (`ProductHero.svelte:83`, `flex items-center gap-1`): on mobile the flex
   children wrap into ragged columns ("Updated …, 11:43:21 / PM · Checks every / 1h"). Use inline
   flow with `flex-wrap`, and a shorter date format.
5. **"No Image" placeholder** is a full-width 128 px block on mobile (`ProductHero.svelte:56`). The
   2026-06 review suggested a short icon strip for the no-image case; it was never applied here.
6. **Price Alerts below Scrape History** on the detail page (`+page.svelte:508–511`). Alerts are a
   primary action; scrape history is diagnostic and collapsed. Swap them.
7. **Scrape-health "Status" card** renders "No data" at `text-sm` beside `text-xl` values in the
   sibling cards (`routes/scrape-health/+page.svelte:72`) — inconsistent weight.

---

## Backlog items (each needs a PRD via `/create-prd` first)

### B-1 — Account backup bundle

Recommend a **per-user export/import bundle** (JSON zip: products, URLs, price history, alerts,
tags, comparison groups, store configs, settings minus secrets) in `/settings/data`, extending the
existing CSV export. Rejected: a `pg_dump`/restore UI — that is an operator task across all users,
and there is no admin role to gate it; document it in the deploy runbook instead. PRD must decide:
ID remapping on import, merge vs. replace, and whether price history is capped by size.

**Decisions (2026-09-25):**

- **Format: one JSON file** (`ophi-backup-YYYY-MM-DD.json`, `"format": "ophi-backup", "version": 1`),
  not a zip — human-readable, diffable, and nothing else in the bundle needs binary packing.
  `version` is checked on import; an unknown version is refused, not guessed at.
- **Contents:** settings (scraping prefs, anomaly/auto-pause, email opt-in, display currency), tags,
  comparison groups, the user's store configurations (`StoreConfiguration` is per-user — checked),
  products with custom fields, URLs, **full** price history and alerts (target, currency, condition,
  active, trigger history).
- **Excluded — credentials and anything that works as one:** password hash, security stamp, API
  keys, the Discord webhook URL, Telegram chat id, Pushover key, **outbound webhook targets** (their
  URL is often the secret — a Home Assistant webhook id, an ntfy topic), notifications and scrape
  logs. The bundle lists what it left out under `excluded`, so a user knows what to re-enter.
- **Import is merge-only — settings included.** Settings are restored only into an account with no
  products yet (the migrate-to-a-new-account case); anywhere else the import leaves them and says so.
  Rejected: replace — one wrong file would wipe an account, and "start
  clean" is already available by deleting the account. Matching: tags and comparison groups by
  name, stores by `storeId` (an existing store is **kept**, never overwritten), products by URL (a
  product with any already-tracked URL is skipped whole). Everything imported gets **new IDs**; the
  bundle's IDs are only references inside the file.
- **Alert cap holds.** Imported active alerts count against `MaxAlertsPerUser`; any beyond it are
  imported paused and reported, never silently dropped.
- **No immediate re-scrape storm.** URLs keep their `lastCheckedAt`, so the dispatcher picks them up
  on their normal schedule rather than all at once.
- **Size:** price history is not capped in the export; import is bounded by a 25 MB request limit
  and runs in one transaction (all or nothing).

**Phases:** B-1a export (`GET /api/v1/account/backup` + download button), B-1b import
(`POST /api/v1/account/backup` + restore card with a result summary).

**As shipped (both phases in one commit, plus a follow-up):** the first version restored settings on
every import, contradicting "never overwrites" — fixed to the empty-account rule above. In the compose
stack uploads pass through adapter-node, whose `BODY_SIZE_LIMIT` defaults to 512K (it also capped the
existing CSV import); the web service now sets `26M`. settings on import go through `UpdateSettings.Validator`,
so a file cannot set what the UI couldn't; imported URLs pass the same SSRF-guarded
`IsValidHttpUrl`; non-positive price points are dropped (the parser invariant); a file with the right
header but missing sections is a 400 `MalformedBackup`, not a crash. Postgres tier: export from one
account → import into another → re-import adds nothing. Verified live on the dev stack: 8 products /
600 price points exported with no credential fields; re-importing the same file skipped all 8; a
foreign file returned 400 `UnsupportedBackup`.

### B-2 — Telegram and Pushover channels

Copy the Discord shape: a `SendAlert{Channel}Requested` event + handler, per-user settings on
`User`, a "send test" endpoint like `TestDiscordWebhook`, and a card in `/settings/notifications`.
PRD must decide: Telegram bot model (one operator-configured bot + per-user chat id, recommended,
vs. per-user bot tokens) and secret storage for the Pushover user key.

**Decisions (2026-09-25, defaults the user accepted by asking to complete the list):**

- **One operator bot / one operator app.** `TELEGRAM_BOT_TOKEN` (+ optional `TELEGRAM_BOT_USERNAME`
  for the "open the bot" link) and `PUSHOVER_APP_TOKEN` are server env, like `SMTP_*` and
  `DISCORD_WEBHOOK_URL`. Users store only their recipient: a Telegram chat id, a Pushover user key.
  Rejected: per-user bot tokens / app tokens — every user would have to register a bot or a Pushover
  app, and a token is a real credential to store; a chat id or user key only addresses a recipient.
- **No server-side chat linking.** The user pastes their chat id (the card explains how to get it
  and links the bot). Rejected: `/start <code>` deep-link linking — it needs the worker to poll
  `getUpdates` or host a webhook, i.e. a long-lived inbound surface for a convenience.
- **Recipients are never echoed.** Settings return only `…Configured` booleans (as Discord does) plus
  `…Available` (server has the token). The cards are hidden when the channel is unavailable.
- **Plain text only.** No Telegram `parse_mode`, no Pushover `html=1`, so a product name cannot
  inject formatting or links.
- **Delivery semantics copy Discord:** one cascaded event per channel from
  `SendAlertNotificationHandler`, cascaded when the user has the channel enabled and a recipient
  set; the channel handler rethrows on failure so Wolverine retries it independently. With no
  operator token the service logs and skips (a user can't be left with a silently retrying queue).

**Phases:** B-2 is one phase — domain fields + migration, services, handlers + fan-out, settings
slices + test endpoints, settings UI.

**As shipped:** the Telegram and Discord HttpClients call `RemoveAllLoggers()` — the bot token and
webhook URL sit in the request path, which the default logging handlers write at Information (checked
live: a marker token never reached the log). Migration `AddPushChannels` (nullable recipients, `false` flag defaults); one
`TestPushChannel` slice serves both `/settings/{telegram|pushover}/test`; env added to both compose
files, `.env.example`, README, security and Pi docs. Verified live with placeholder tokens: cards
appear, the chat id saves and is not echoed, and Telegram's real 401 surfaces in the toast. The card
shows a validation error's field message rather than the generic summary.

### B-3 — Display-currency conversion

Users with stores in several currencies already see `CurrencyMismatchBanner`. Scope this as
**display-only**: an optional preferred currency, converted values shown secondary to the native
price ("€75 ≈ $81"). **It must never touch alert targets or comparison math** — `docs/agent-notes.md`
records why alerts are denominated and not auto-converted. PRD must decide the rate source (ECB
daily reference rates: free, no key, one outbound call per day) and how staleness is shown.

**Decisions (2026-09-25):**

- **Source: ECB euro reference rates**,
  `https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml` (format checked against the live
  feed: `<Cube time='YYYY-MM-DD'>` holding `<Cube currency='USD' rate='1.1367'/>`, units per 1 EUR,
  ~30 currencies, published on TARGET working days). EUR is implicitly 1. Cross rates go through EUR.
  A currency the ECB does not list gets no conversion — never a guess. Rejected: commercial FX APIs
  (keys, quotas, and another party learning the server's traffic pattern).
- **Structural isolation.** The backend only fetches, stores (`ExchangeRates` table) and serves
  rates (`GET /api/v1/fx-rates`). All conversion happens in the frontend display layer. Nothing in
  `Ophi.Domain`, `ProductPriceAggregator`, alert checking or comparisons can reach a rate, so the
  "never touches alerts/comparisons" rule holds by construction, not by review.
- **Fetch only when needed.** `ExchangeRateRefresher` runs from the worker dispatcher loop (the
  `DataRetentionService` pattern), at most every 6 h, and only while at least one user has a
  display currency set — a server nobody uses this on makes no outbound call.
- **Staleness is shown, not hidden.** The converted value's tooltip names the ECB date; when the
  rates are more than 4 days old (longer than any ECB holiday gap) the value is labelled with that
  date instead of shown as current.
- **Where it shows:** a muted "≈ …" beside the native price on dashboard cards and table rows, and
  on the detail page's current-price and per-store prices (most useful exactly when
  `CurrencyMismatchBanner` shows). Native price always stays primary. Preference lives in
  `/settings/account` (`User.DisplayCurrency`, nullable = off).

**Phases:** one phase — rates client + table + refresher + endpoint, setting, frontend converter +
display.

**As shipped:** migration `AddDisplayCurrencyAndExchangeRates`; `/fx-rates` also returns the
`supported` list so the settings picker and the validator share one owner. Verified live with the
embedded worker: no ECB call while nobody had a display currency; after choosing GBP the next tick
logged "30 currencies as of 2026-09-24", and the dashboard showed e.g. $189.00 ≈ £142.97
(189 / 1.1367 × 0.85986). Postgres tier covers the delete-and-insert refresh and numeric(18,6).
