# Ophi Web

The SvelteKit (Svelte 5 + TypeScript + Tailwind) frontend for Ophi. Talks to the .NET API under
`/api/v1`. See the repo [README](../../README.md) and [docs/architecture.md](../../docs/architecture.md)
for the full picture.

## Develop

```bash
bun install
bun run dev          # vite dev server (default http://localhost:5173)
```

Point the dev server at a running API with `VITE_API_URL` (defaults to the local API). The backend
needs a reachable Postgres — see the repo README for the quickest way to bring one up.

## Test

```bash
bun run test:run     # Vitest (single run)
bun run test         # Vitest (watch)
bun run test:e2e     # Playwright E2E
bun run check        # svelte-check — type-check (bun run build does NOT type-check)
```

> `bun run build` does **not** type-check. Run `bun run check` before considering frontend work done —
> e.g. calling a non-existent API-client method compiles fine but 500s at runtime.

## Conventions

- Svelte 5 runes (`$state`, `$derived`, `$props`, `$effect`); prefer TypeScript everywhere.
- Routes load data via `+page.ts` loaders (`await parent()` for auth, `api.withFetch(fetch)` for SSR);
  pages read initial data from the `data` prop.
- Shared modal shell at `src/lib/components/shared/Modal.svelte`; Chart.js is lazy-loaded via
  `$lib/utils/chart.ts`.
- E2E uses the Page Object Model under `e2e/pages/`.
