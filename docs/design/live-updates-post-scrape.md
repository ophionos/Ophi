# Spike: live updates post-scrape

**Issue:** #31 · **Status:** ✅ implemented (decision record) · **Date:** 2026-06-01

> **Outcome:** Option C was adopted — Option A polling shipped first, then Option B (SSE) shipped as the
> app-wide transport with polling kept as a `!connected` fallback. The "Current state" below describes
> the codebase *at spike time* (pre-SSE); see **Follow-ups** at the end for what actually shipped.

## Problem

After adding a store URL to a product, the async scrape completes server-side but the
**product detail page never reflects it** — it shows "No price yet" and the new store contributes
no chart points until a manual reload. Worse, the same page disagrees with itself: the **Scrape
History** panel fetches client-side on mount and *does* show the fresh scrape, while the hero,
Store URLs, statistics and chart all render from the SSR loader snapshot.

## Current state

- **No push infrastructure.** There is no SSE / WebSocket / SignalR anywhere. Every "live" surface
  is poll-based.
- **Dashboard** (`routes/dashboard/+page.svelte`) polls `getProducts` every 3s — but **only while
  some product is `pending`** (`hasPendingProducts`). Once products go active, polling stops.
- **Product detail** (`routes/products/[id]/+page.svelte`) has **no polling at all**. It hydrates
  from the `+page.ts` loader once and only re-fetches in response to user actions (add URL, create
  alert, etc.). A scrape that finishes seconds after the page loaded is invisible.
- **NotificationBell** polls `getNotificationCount` and refreshes on tab focus/visibility.

So the "No price yet" staleness is not a data bug — `GetPriceHistory` and `GetProduct` return the
right thing on the *next* fetch. The page simply never asks again.

## Options

### A. Extend polling to the detail page (near-term)
Mirror the dashboard's pending-poll: while the product is `pending` **or** any URL is awaiting its
first scrape, poll `getProduct` + `getPriceHistory` every 3s; stop once every URL has been checked.

- **Pros:** ~40 lines, reuses an established, tested pattern; zero backend work; closes the actual
  "No price yet" bug. Low risk.
- **Cons:** still polling; only covers the detail page; a few seconds of latency.
- **Bounding:** stop when `isAwaitingScrape` clears (a URL flips to checked once `lastCheckedAt` is
  set, regardless of success/failure) **and** a hard cap (e.g. 20 polls / 60s) so a perma-failing
  URL can't poll forever.

### B. Server push via SSE (strategic)
A `GET /api/v1/events` endpoint streaming `text/event-stream`; a Wolverine handler on scrape-complete
publishes a `scrape-completed` event; the browser subscribes app-wide (dashboard, detail,
comparisons, notifications) and patches state in place.

- **Pros:** true real-time; one mechanism replaces every poll; scales down idle traffic.
- **Cons:** materially more work — per-user fan-out/filtering, auth on the stream, reconnection &
  backoff, proxy/timeout behaviour (WSL/nginx), and a Wolverine→SSE bridge. Needs its own design.

### C. Hybrid
Ship A now; adopt B later as the unified transport and retire the polls behind a shared
`liveUpdates` store so call sites don't change again.

## Recommendation

**Adopt C: ship Option A now, pursue Option B as a follow-up.** The poll closes the user-visible
bug immediately at low risk and reuses a proven pattern; SSE is the right end state but warrants a
dedicated design pass (auth, fan-out, reconnection, deployment) rather than being rushed in behind a
UX fix.

## Follow-ups

- **Near-term (Option A):** detail-page live refresh while awaiting first scrape — see the tracked
  implementation issue.
- **Strategic (Option B):** SSE transport design + app-wide adoption — see the tracked spike/epic (#34).
  Shipped: `GET /api/v1/events` SSE stream fed by a `LiveUpdate` Wolverine message (Worker → API over
  the `scrape-notifications` Postgres queue in split mode, in-process when embedded), fanned out per
  user by `SseConnectionRegistry`. The browser subscribes once via the `liveUpdates` store, mounted in
  the root layout; dashboard / detail / comparisons / notification-bell patch in place. The Option A
  polls are kept as `!connected` fallbacks rather than removed, so the app degrades gracefully if SSE
  can't connect. Known constraint: the queue hop is single-consumer — horizontal API scaling would
  require moving it to Postgres LISTEN/NOTIFY (documented in `WolverineConfig.ScrapeNotificationQueue`).
