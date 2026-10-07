# Ophi UX / Modernization Review — 2026-06-04

> **Implemented 2026-06-04** (verified live, `shots/15`–`18`; all 1001 frontend tests + svelte-check green):
> - **SSE `/events` double-prefix bug fixed** (`liveUpdates.svelte.ts`) + regression test tightened — dashboard now loads with **0 console errors** (was a 404 every page).
> - **ProductCard image `aspect-4/3` → `aspect-16/9`** — shorter cards.
> - **Dashboard grid gains a 4th column at `xl`** — ~2–3× more products above the fold.
> - **Comparisons stat cards → slim inline strip** (was two full-width single-digit cards).
> - **Styled `+error.svelte`** added (404/error page now uses the design system).
>
> Deferred (lower impact / larger scope): shared `StatCard` extraction, scrape-health/detail
> stat-card tightening, dashboard header-toolbar consolidation, detail-page rail rebalance,
> list-view density toggle, typography hierarchy.



Hands-on review of the running app (demo account `demo@ophi.test`, 9 seeded products
with 75 days of price history). Driven through Playwright at 1440×900 (desktop,
light + dark) and 390×844 (mobile). Screenshots in `/shots`.

> **Caveat — test setup.** This was a local **dev build** (vite + native API) with
> **synthetic seed data**: products inserted directly into Postgres with **no images**
> and **no live worker/scraper**. So the all-"No Image" cards and the empty Scrape-Health
> page reflect the seed, not product defaults — the normal paste-URL flow scrapes an
> image (the one product that has one, Sony, renders fine: `shots/04`,`shots/05`). The
> layout/CSS findings below were each confirmed against source and hold regardless.

The app is genuinely solid: clean brand palette, good dark mode, lazy Chart.js trend
chart looks great, command palette, deal-score badges, sparklines, view toggles. The
bones are modern. The single biggest weakness is exactly the one you named:
**low information density / wasted whitespace on a data-driven product.** It shows up
as the *same* pattern repeated on every page, so fixing it is mostly editing a handful
of shared spacing/grid values rather than a redesign.

---

## Theme 1 — Oversized "stat cards" are the dominant whitespace driver

Every page opens with a row of cards that each hold a single number but consume a full
`p-6` card spanning a third-to-a-half of a 1280px container.

| Page | Code | Problem |
|---|---|---|
| Comparisons | `routes/comparisons/+page.svelte:99` `grid md:grid-cols-2 ... p-6` | **Worst case.** Two ~600px-wide cards for the digits "1" and "3", then a large empty void below (see `shots/09-comparisons.png`). |
| Scrape Health | `routes/scrape-health/+page.svelte:65` `grid-cols-2 md:grid-cols-4` `p-4` | 4 big cards: Domains/Scrapes/Success/Status. `shots/11-scrape-health.png`. |
| Product detail | `routes/products/[id]/+page.svelte:601` `grid-cols-2 md:grid-cols-4` `p-4` | Lowest/Highest/Average/Current — each ~290px wide for one short value. |

**Recommendations**
- Comparisons: change `md:grid-cols-2` → `grid-cols-2 sm:grid-cols-4` and the cards to
  `p-4`, or better, collapse these two counters into a single compact inline summary row
  like the dashboard's `DashboardStatsBar` (which is the *right* density model already).
- Standardize one `StatCard` component (there isn't one — the markup is copy-pasted on
  3+ pages) so density is set in one place. Target `p-3`/`p-4`, `text-xl` value,
  `text-xs` uppercase label.
- The dashboard's `DashboardStatsBar` is the pattern to copy everywhere: it's a slim
  inline strip, not boxes. Reuse it for Comparisons and Scrape Health headers.

## Theme 2 — Product cards are too tall; the image dominates

`ProductCard.svelte:108` uses `w-full aspect-4/3` for the image. **Even with a real
image** this makes cards tall: in a 3-col grid the image is ~300px; on mobile it's a
full-width ~290px block, so **one card fills the entire phone screen**
(`shots/13-dashboard-mobile.png`) and only one row is above the fold on desktop
(`shots/05-dashboard-viewport.png`). (The all-"No Image" look in the screenshots is a
seed artifact — see the caveat — but the height issue is independent of image presence.)

**Recommendations**
- Reduce to `aspect-16/9` (or a fixed `h-36`) — cuts ~35% of card height immediately,
  with or without an image.
- For the no-image fallback, render a short (`h-16`) icon strip rather than a giant
  placeholder box — cheap, and it makes manually-created products less dominant.
- Consider a denser default: 4 columns at `xl` (`lg:grid-cols-3 xl:grid-cols-4`,
  `dashboard/+page.svelte:339`). The 1280px container easily fits 4.

## Theme 3 — The dashboard header stack pushes data below the fold

Above the product grid sit five stacked full-width bands: stats bar → Add-URL bar →
"Your Products" + view toggle → search/sort row → filter chips. Each has its own margin
(`mb-4`, `mb-3`, etc.). On the list view the first data row doesn't appear until ~330px
down (`shots/06-dashboard-list.png`).

**Recommendations**
- Merge the "Your Products" heading row, search/sort, and filter chips into a single
  toolbar row (heading left; search + sort + view-toggle right; chips can sit inline or
  move into a dropdown). Saves ~80–120px.
- The full-width Add-URL bar (`dashboard/+page.svelte:284`) is prominent real estate for
  a power action. Consider collapsing it into a "+ Add" button that expands the input on
  click, freeing the top of the page for data.

## Theme 4 — List view density (the data-table)

The table (`shots/06-dashboard-list.png`) is the most "data-driven" surface and the best
direction, but rows are tall and it only shows ~8 at once.

**Recommendations**
- Tighten row padding and add a compact/comfortable density toggle.
- The `STORE`/`STATUS` columns are low-value-per-pixel (every row shows "1" / "Active");
  consider merging status into the name row as a dot, and showing store as a favicon.
- Make list the default `viewMode` for users past N products — it's the denser fit.

## Theme 5 — Detail page right rail leaves a large void

`routes/products/[id]/+page.svelte:691` `grid-cols-1 lg:grid-cols-3 ... items-start`:
chart spans 2 cols and is tall; the right rail (Comparison Group + Danger Zone) ends
high, leaving a big empty bottom-right (`shots/07-detail-data.png`).

**Recommendations**
- Move Store URLs and/or Price Alerts into the right rail so both columns fill, or let
  the chart span full width and put the stat cards + side panels in a denser row beneath.
- The hero card (`:505`, `p-6 md:p-8`, `w-48 h-48` image) is ~280px tall for a title +
  two metadata lines. Tighten to `p-5` and a smaller `h-32` image.

---

## Real bug found — app-wide live updates (SSE) are broken in every environment

Not UX, but found while reviewing: the realtime SSE client builds a **doubled path**.
`liveUpdates.svelte.ts:41` does `new EventSource(`${API_BASE}/api/v1/events`)`, but
`API_BASE` is already `/api/v1` (`client.ts`, from `VITE_API_URL`). Result:
`GET /api/v1/api/v1/events` → **404** (server route is `/api/v1/events`,
`StreamEvents.cs:25`). Confirmed live in the browser console on every page.

Because every other endpoint relies on `API_BASE` ending in `/api/v1` (e.g.
`${API_BASE}/products`), this isn't environment-specific — the `/events` call is always
doubled, so the app-wide live-update transport (PR #34/#40) silently never connects and
the UI falls back to polling.

**Fix:** drop the redundant prefix — `new EventSource(`${API_BASE}/events`, …)`. Add a
test asserting the constructed URL is `/api/v1/events`; an e2e/unit guard would have
caught this since it's a static string mismatch.

## Smaller / polish findings

- **404 page is unstyled** — bare "404 / Not Found" top-left, ignores the design system
  (`shots/10-settings.png`, reached via `/settings` which isn't a route). Add a styled
  `+error.svelte`. Note `/settings` itself 404s; Settings is a nav dropdown only —
  a real `/settings` landing page may be worth adding.
- The 90d/30d/**7d** charts all render well (`shots/08`, `shots/14`); no issue there.
- **Typography hierarchy** is slightly flat: page `<h1>` is `text-2xl` and section `<h2>`
  is `text-lg`; values and labels often share weight. A modern data UI benefits from a
  tabular-nums font feature on prices and a clearer size jump between levels.

## Suggested sequencing
1. **Quick wins (hours):** ProductCard `aspect-4/3 → 16/9` + no-image strip; Comparisons
   stat cards `md:grid-cols-2 p-6 → grid-cols-4 p-4`; styled `+error.svelte`.
2. **Shared component:** extract one `StatCard` and reuse on detail/scrape-health/
   comparisons so density lives in one file.
3. **Toolbar consolidation** on the dashboard; list-view density toggle.
4. **Detail page** rail rebalance + hero tightening.
