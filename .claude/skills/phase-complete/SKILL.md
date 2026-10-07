---
name: phase-complete
description: Close out a completed phase — run the full test suite (backend, frontend, type-check), update the plan doc's status table and affected docs, then commit with the phase name.
---

# Phase Complete

Close out a phase from the active plan document in `docs/` (plans track phases in a Status table; see `docs/history/2026-06-frontend-ux-plan.md` for the shape).

1. **Green before touching docs** — the suites and gates in CLAUDE.md § Testing Conventions, including the Postgres tier if the phase touched DB behavior, run through the `gate-runner` agent. Everything passes first.
2. **Update the plan doc:** mark the phase ✅ Complete with today's date in its Status table.
3. **Update affected docs:** `docs/features.md`, `docs/architecture.md` as applicable (`docs/api.md` only if a cross-cutting rule changed — endpoints are owned by OpenAPI; run `bun run gen:types` instead) — and remove anything the phase just shipped from `docs/future.md`.
4. **Archive when the whole plan is done:** move the narrative to `docs/history/<yyyy-mm>-<topic>.md`, update `docs/history/README.md` and the index in `docs/README.md`.
5. **Commit** with a conventional message naming the phase: `feat(<scope>): complete <PHASE-ID> — <short description>`.
6. **Summarize** what was completed and what's next in the plan.
