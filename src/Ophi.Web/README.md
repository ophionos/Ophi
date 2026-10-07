# Ophi Web

The SvelteKit frontend (Svelte 5, TypeScript, Tailwind). It talks to the .NET API under `/api/v1`.
See the repo [README](../../README.md) and [docs/architecture.md](../../docs/architecture.md).

```bash
bun install
bun run dev          # vite dev server, http://localhost:3000 (proxies /api to :5041)
bun run test:run     # Vitest, single run
bun run check        # svelte-check — `bun run build` does NOT type-check
bun run test:e2e     # Playwright
```

`VITE_API_URL` must be set (copy `.env.example`); `src/lib/api/client.ts` throws at import time
without it. Set `API_URL=http://localhost:5041` for `bun run dev`: server-side loads go through
`src/lib/server/handle.ts`, which defaults to `:5000`. Conventions are in the repo [CLAUDE.md](../../CLAUDE.md).
