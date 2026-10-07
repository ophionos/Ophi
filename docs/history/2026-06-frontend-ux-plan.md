# Frontend UX Improvement Plan

> ✅ **All phases complete (2026-06-10).** Archived; kept for the review findings and phase rationale.

Outcome of a full front-end review (2026-06-09). The architecture is healthy — loader-based SSR
(`+page.ts` + `api.withFetch(fetch)`), one shared SSE connection with per-surface poll fallbacks,
optimistic updates that reconcile with the server, lazy-loaded Chart.js, focus traps, skip links.
The gaps are information architecture, state management consistency, and a handful of correctness
bugs. This plan breaks the findings into independently shippable phases, ordered by value.

Work each phase TDD-first; update the status table and archive details to `history/` as phases land.

## Status

| Phase | Title | Status |
|---|---|---|
| UX-1 | Correctness quick fixes (command palette key hijack, `resolve()`, dead code) | ✅ Complete (2026-06-09) |
| UX-2 | Dashboard filter state: URL-driven + fully server-side | ✅ Complete (2026-06-09) |
| UX-3 | Settings IA split (`/settings` area, slim `/stores`) | ✅ Complete (2026-06-09) |
| UX-4 | Price formatting + notifications "load more" | ✅ Complete (2026-06-09) |
| UX-5 | Account management | ✅ Complete (2026-06-10) — see [2026-06-account-management-prd.md](2026-06-account-management-prd.md) |
| UX-6 | Smaller UX wins (palette actions, a11y menus, bulk actions, …) | ✅ Complete (2026-06-09) |

---

## UX-1 — Correctness quick fixes

Pure fixes, no design decisions.

1. **Command palette hijacks keys app-wide when closed.**
   `CommandPalette.svelte` attaches `<svelte:window onkeydown={handleKeydown} />` outside its
   `{#if isOpen}` block and `handleKeydown` never checks `isOpen`. The component is always mounted
   in the layout while signed in, so Enter / Escape / ArrowUp / ArrowDown are `preventDefault()`-ed
   globally: Enter in any form (e.g. the add-product URL box) is swallowed and navigates to the
   first palette item; arrow keys fight caret movement and scrolling.
   **Fix:** guard `handleKeydown` with `if (!isOpen) return;` (the Ctrl/Cmd+K *open* shortcut
   already lives in `+layout.svelte`, so nothing global is needed in the palette).
2. **Command palette navigation bypasses `resolve()`.** `staticItems` and search-result `href`s are
   raw paths passed to `goto()`; every other nav site uses `resolve()` from `$app/paths`. Breaks
   under a configured base path. **Fix:** resolve hrefs where the items are built.
3. **Dead `error` state on product detail.** `products/[id]/+page.svelte` declares `let error =
   $state('')` and renders an `{#if error}` branch, but nothing ever assigns it (loader failures go
   through `+error.svelte`). **Fix:** remove the dead state and branch.

**Tests:** extend `CommandPalette.test.ts` — keydown events with `isOpen: false` must not call
`goto`/`onClose` and must not cancel the event; make the `resolve` mock non-identity so the
`resolve()` usage is actually asserted. Existing suites must stay green.

## UX-2 — Dashboard filter state: URL-driven + fully server-side

Two intertwined problems on `/dashboard`:

- **Filters aren't in the URL.** Only `?tag` is read (once, on load). Search, status, sort, page,
  and view mode reset on refresh, aren't shareable, and ignore back/forward.
- **Half the filters are client-side over one page of a server-paginated list.** `favouriteOnly`
  and the stats-bar `drops`/`alerts` filters run in `displayedProducts` against the current page
  only, while search/status/tag/`atLowest` are server-side. With >1 page, "Favourites" shows only
  the favourites that happen to be on the current page, and pagination totals don't match what's
  displayed. The stats bar mixes scopes the same way: "Price Drops" counts the current page while
  "Total products" is global.

Scope:

1. Backend: add `favourite`, `priceDrop` (negative `priceChange`), and `hasAlerts` filter params to
   `GET /products` (vertical slice `Features/Products/GetProducts.cs`), plus global counts for the
   stats bar (drops count, alert count) on the list response — replacing the dashboard loader's
   "fetch every alert to call `.length`" pattern.
2. Frontend: reflect search/status/sort/page/tag/favourite/stats filters into the URL
   (`goto(url, { replaceState: true, keepFocus: true, noScroll: true })`), read them in the loader,
   drop the client-side `displayedProducts` filtering.
3. Persist `viewMode` to localStorage (same pattern as the existing `density` store).

## UX-3 — Settings IA split

`routes/stores/+page.svelte` (33 KB, largest file in the app) is the de-facto settings area: store
configs **plus** eight scraping/notification settings, Discord setup, outbound webhooks, API keys,
and product CSV/JSON import/export. None of that is findable under a page titled "Store
Configurations", and the nav's "Settings" dropdown (Tags / Stores / Scrape Health) contains no
actual settings.

Scope:

1. New `/settings` route with sections (separate sub-routes so each stays small and deep-linkable):
   - **Scraping** — check interval, fetch delay, cache TTL, anomaly threshold, auto-pause
   - **Notifications** — Discord, outbound webhook targets
   - **API Keys**
   - **Data** — product import/export
2. `/stores` keeps only store configurations (cards, filters, store import/export, test modal,
   affiliate toggle if it stays store-scoped).
3. Nav: "Settings" dropdown gains the new sections (or links to `/settings`); update command
   palette static items, breadcrumbs, e2e page objects.
4. Existing components (`WebhookList`, `ApiKeyList`, modals) move unchanged — this is a page split,
   not a rewrite.

## UX-4 — Price formatting + notifications page

1. **Locale-aware prices.** Prices render as `` `${currency} ${price.toFixed(2)}` `` ("USD 189.00")
   at ~10 sites. Add `formatPrice(amount, currency)` to `$lib/format.ts` using `Intl.NumberFormat`
   ("$189.00" / "189,00 €") and apply everywhere prices render (cards, table, detail stats, alerts,
   comparisons, charts' tooltips).
2. **Notifications are a dead end.** The bell dropdown caps at 20 with "Showing 20 of N" and no way
   to see the rest. Add either a "Load more" in the dropdown or a `/notifications` page (the API
   already supports `page`/`pageSize`); link unread badge → full list.

## UX-5 — Account management

There is none: the API exposes register/login/logout/me/forgot/reset only. A signed-in user cannot
change password, name, or email — the only path to a new password is the logged-out
forgot-password email flow.

Scope (needs its own PRD before implementation):

1. Backend slices: `ChangePassword` (requires current password), `UpdateProfile` (name; email
   change with re-verification is a follow-up decision), possibly `DeleteAccount`.
2. Frontend: an **Account** section in `/settings` (depends on UX-3); user menu links to it.
3. Security review: session invalidation on password change, rate limiting.

## UX-6 — Smaller UX wins — ✅ all shipped 2026-06-09

- **Command palette actions** — "Add product" (jumps to the dashboard URL box) and "Toggle theme"
  under a new Actions section; editable store results deep-link via `?edit=<domain>`, built-ins
  land on the list.
- **Logged-in landing redirect in the loader** — `routes/+page.ts` probes `/auth/me` and
  redirects; the `onMount` redirect (which flashed the marketing page and missed fresh page loads)
  is gone. The invented "100+ supported stores" stat became the truthful "Any online store".
- **Dropdown menu a11y** — `menuKeyNav` action (`$lib/actions/menuKeyNav.ts`) gives the layout's
  Settings/user dropdowns ArrowUp/Down/Home/End focus cycling.
- **Bulk actions on the dashboard** — Select mode (grid + list views) with per-item checkboxes,
  select-page, and Pause / Resume / Delete (confirm-gated) fanned out via `Promise.allSettled`
  with a failure-count toast.
- **Chart ranges** — 1y preset on product-detail and comparison charts (endpoints already clamp
  `days` to 365).
- **Post-mutation refetches** — alert create/delete and tag add/remove on the product detail page
  now update locally from the mutation response instead of refetching the whole product.
- **Loader-hydration convention** — `liveRefresh()` (`$lib/utils/liveRefresh.ts`) consolidates the
  SSE-subscribe + poll-fallback trio; dashboard, product detail, and the notification bell all use
  it. The hydrate-from-loader `$effect` itself stays as the documented house pattern.
