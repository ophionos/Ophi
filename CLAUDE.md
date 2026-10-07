# CLAUDE.md

## Project Overview

**Ophi** — Price tracking application. .NET 10 backend, SvelteKit frontend, PostgreSQL database (SQLite is test-only).

For architecture, entity relationships, and project layout, see [docs/architecture.md](docs/architecture.md). For the doc index, see [docs/README.md](docs/README.md). For durable implementation invariants, rejected alternatives, and the bug classes behind each CI gate, see [docs/agent-notes.md](docs/agent-notes.md).

**One owner per fact:** deploy topology → `/redeploy` skill; local run topology → `/dev-stack` skill; endpoint shapes → live OpenAPI; implementation invariants → `docs/agent-notes.md`; machine-local values (WSL distro, paths, test DB connection) → the untracked `CLAUDE.local.md`. Link to the owner instead of restating — restated facts drift.

**The repo is public.** Never commit the operator's location, ISP, IP addresses, machine paths, or personal identity — not in code, comments, test fixtures, docs, commit messages, or PR text. Those belong in `CLAUDE.local.md` or nowhere. Describe environment-dependent behavior generically ("a non-US egress").

## Code Style

- Always prefer TypeScript over JavaScript for any frontend file.

## Commands

```bash
# Backend — tests run on Microsoft.Testing.Platform (global.json `test.runner`), NOT VSTest.
# --logger and --collect no longer exist. --filter still takes VSTest syntax, but it can't be
# combined with the --filter-* flags below, so prefer those.
dotnet test tests/Ophi.Postgres.Tests # Postgres tier — needs POSTGRES_TEST_CONNECTION or Docker (Testcontainers fallback)
dotnet test --filter-not-trait "Category=Integration"   # what CI runs (xunit.v3's native spelling)
dotnet test --list-tests                                # prints "Discovered N tests." — catches a silent discovery break

# Frontend (from src/Ophi.Web/)
bun run test:run                      # Unit/component tests (single run) — always use this in an agent session
bun run test                          # Watch mode — human use only; blocks a turn forever
bun run check                         # Type-check (svelte-check) — `bun run build` does NOT type-check

# Migrations (must run from Ophi.Infrastructure, NOT Ophi.Api)
dotnet ef migrations add <Name> --project src/Ophi.Infrastructure --startup-project src/Ophi.Infrastructure
```

## Anchors

Layout is discoverable; these three names are not, and other docs lean on them:

- `src/Ophi.Web/src/lib/api/client.ts` — the `api` singleton. Method names are easy to guess wrong (`listApiKeys`, not `getApiKeys` — see Testing Conventions).
- `src/Ophi.Domain/Services/` — cross-entity invariants (`ProductPriceAggregator`), referenced throughout `docs/agent-notes.md`.
- `src/Ophi.Api/Features/Tags/CreateTag.cs` — the canonical vertical slice to copy.

## Architecture (TL;DR)

- **Backend:** Vertical Slice + Wolverine. Consolidated single-file pattern: `Features/{Domain}/{Action}.cs` (Command, Handler, Validator, Endpoint). Dispatch via `bus.InvokeAsync<TResponse>(command)`. Mutate entities via domain methods (`product.MarkActive()`, `alert.Trigger(now)`, `productUrl.RecordSuccessfulScrape(...)`), not direct field assignment. Cross-entity invariants live in `Ophi.Domain/Services/`.
- **Frontend:** SvelteKit + Tailwind CSS. Routes use `+page.ts` loaders (`await parent()` for auth, `api.withFetch(fetch)` for SSR); pages read initial data from `data` prop. Shared `Modal.svelte` shell; lazy-loaded Chart.js via `$lib/utils/chart.ts`.

## TDD (Mandatory)

1. Write failing test
2. Write minimum code to pass
3. Refactor

**Coverage targets — advisory, NOT CI-enforced:** Backend 80%+, Frontend 70%+, measured via `test:coverage`. Don't claim they gate anything.

## Test Patterns

```csharp
// Backend naming: [Method]_[Scenario]_[ExpectedResult]
AddProduct_WithValidUrl_ReturnsCreatedProduct

// Use constructor syntax for records
new Register.Command("email", "password", "name");

// Seed User before Products/Alerts (FK constraints)
```

```typescript
// Frontend naming: "should [behavior] when [condition]"
it('should display product name when loaded')
```

## Testing Conventions

- This project uses **Moq** for mocking, NOT NSubstitute. Always check existing test files for mocking patterns before writing new tests.
- **Green-before-commit — this bullet is the one owner of the rule.** After implementation work: `dotnet test`, plus from `src/Ophi.Web/` `bun run test:run` **and** `bun run check`. DB-behavior changes additionally need the real Postgres tier (see Known Gotchas); route/loader changes additionally need `bun run build` with `VITE_API_URL=/api/v1` (the illegal-export class — `docs/agent-notes.md` § CI gates). `/verify` and `/phase-complete` point here instead of restating it — don't add a fourth copy, and don't stack extra self-verification passes on top of it.
- **`bun run build` does NOT type-check.** Run `bun run check` (svelte-check) before considering frontend work done — TypeScript errors such as calling a non-existent API-client method (e.g. `getApiKeys` instead of `listApiKeys`) compile fine and only surface as a runtime 500. Enforced in CI (frontend "Type check" step) and locally by a Stop hook (`.claude/hooks/svelte-check.ps1`) that runs while `src/Ophi.Web/` has uncommitted changes, skipping when those exact changes already passed this session. Its sibling `dotnet-build.ps1` does the same for backend code.

## Scraping

- HTTP (AngleSharp) first, Playwright fallback for JS sites.
- `StoreConfig.RequiresJavaScript` is the single signal that triggers browser scraping.

## Git

Conventional Commits: `<type>(<scope>): <description>`. Types: `feat`, `fix`, `docs`, `style`, `refactor`, `test`, `chore`.

## Workflow

- Green before commit — see Testing Conventions for what "green" means here.
- Use conventional commit messages; include phase/feature name when applicable.
- Project skills (`.claude/skills/`): `/phase-complete` closes out a plan phase (tests → docs → commit); `/dev-stack` runs the app locally for UI/manual verification; `/redeploy` ships committed code to the WSL2 Docker stack; `/verify` proves a change works end-to-end (not just tests); `/pr-triage` works through open PRs (CI state, review, merge order, Dependabot, branch cleanup). Project commands (`.claude/commands/`): `/create-prd` generates a PRD with the phase Status table `/phase-complete` expects.

## Implementation Approach

- Follow TDD when implementing new features: write tests first, then implementation.
- When working from a PRD or implementation plan document, read it fully before starting work.
- Update documentation in `docs/` after completing each phase. Archive completed-phase narratives under `docs/history/`.
- **Doc length matches the change.** Cover the substance and the non-derivable "why"; skip filler sections, restated summaries, and boilerplate. Edit the existing doc in place — a new narrative file per change is how the docs got stale contradictions. A three-line change gets three lines.

## Known Gotchas

- Frontend files (`src/Ophi.Web/`) are tab-indented (`.prettierrc`). Copy indentation exactly from the Read output; if an Edit fails on whitespace, retry with a smaller unique anchor. Do not rewrite a large file with Write to get around it — that is how content silently goes missing.
- Be careful with CSS selectors in tests — ensure they are specific enough to not match unintended elements (e.g., stats cards vs product cards).
- Windows `nul` file: never redirect to `nul` in bash — creates a literal file. Use `2>&1` instead.
- **Postgres-vs-SQLite false greens**: SQLite is test-only and diverges from production Postgres. Verify DB-behavior changes against real Postgres — SQLite hides GUID case-sensitivity bugs, type-affinity issues, and index/EXPLAIN behavior. When asserting on indexes, run against Postgres and account for GIN pending-list and exact query-shape artifacts in EXPLAIN output (a passing SQLite test is not evidence the query uses the index).

## Svelte MCP Tools

Loaded on demand from `src/Ophi.Web/CLAUDE.md` — use the Svelte MCP server (`list-sections`, `get-documentation`, `svelte-autofixer`) for any Svelte work. Only generate a `playground-link` when the user explicitly asks **and** the code was not written to project files.
