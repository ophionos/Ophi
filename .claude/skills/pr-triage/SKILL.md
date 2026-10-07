---
name: pr-triage
description: Work through Ophi's open PRs (own and Dependabot) — classify CI state, review, propose a merge order, merge on approval, then clean up branches and pull main. Use when asked to review/handle/sort/merge the pending or open PRs, deal with Dependabot, or check what's outstanding.
---

# PR Triage

Repo: the `origin` remote (`gh repo view`), default branch `main`, **squash merges** (one commit per PR, `(#N)` suffix).
Branch auto-delete is OFF — pass `--delete-branch`.

## 1. Survey (read-only)

```
git fetch --prune
gh pr list --state open --json number,title,author,headRefName,isDraft,mergeable,reviewDecision,statusCheckRollup,updatedAt
gh run list --workflow e2e.yml --branch main --limit 5
```

Nightly E2E runs on `main` only, never per-PR, and skips nights when `main` did not change. A red streak (consecutive red *runs*) is a real break on `main` that a PR
must not be blamed for — find the first red run and the PR merged just before it (`git log --since`).

## 2. Classify CI per PR

| Symptom | Meaning | Action |
|---|---|---|
| All checks green | Mergeable | Review (step 3) |
| No checks at all | The PR touches only docs, `.claude/`, `.githooks/`, or `LICENSE` (`paths-ignore` in `ci.yml`) | Expected — review normally |
| A job shows "skipped" | The `changes` job found no path that job can catch | Expected — check the `changes` output if it looks wrong |
| Job failed with a log | **Real failure** — red CI is signal | Read the log; fix or report |
| Every job failed in 2-4 s, no log | Usually an Actions **billing block** (rare now: the repo is public, so standard runners are free); the check annotation says "recent account payments have failed" | Confirm the annotation, tell the user; don't merge on it — verify locally per CLAUDE.md § Testing Conventions instead |
| Run predates a fix on `main`, or ran during a block | Stale | `gh run rerun <run-id>` (30-day window), or comment `@dependabot rebase` |
| `mergeable: CONFLICTING` on a Dependabot PR | Another bump touched the same file | Comment `@dependabot rebase`; don't hand-resolve lockfiles |

A backend test failing only in CI (green locally on Windows) is the Linux-divergence class in
`docs/agent-notes.md` § CI gates before it is a flake.

## 3. Review

- **Own/agent PRs:** review the diff for correctness (`/code-review`), and check it followed
  green-before-commit (CLAUDE.md § Testing Conventions). Auth/login/route changes need E2E — one
  merged without it once and broke 10 auth specs.
- **Dependabot:** spawn one `dep-reviewer` agent per PR, in parallel, and judge from their reports. Read
  release notes yourself only where a report quotes a breaking or license item. Give extra scrutiny to
  majors and to runtime-critical groups (`wolverine`, `microsoft-entityframeworkcore`, `microsoft-aspnetcore`,
  `sveltekit`, `oven/bun`, `mcr.microsoft.com/dotnet/*`). An `oven/bun`
  image bump must also bump `packageManager` in `src/Ophi.Web/package.json` — CI reads its Bun version
  from there, so a lone image bump leaves CI testing a different Bun than the images ship.

## 4. Propose the merge order — then wait for approval

Merging is outward-facing: present a table (PR, CI state, verdict, order) and merge only after the user
approves. Ordering rules:

- Fixes to `main`'s own breakage first (they turn other PRs' CI green).
- PRs touching the same file (`Directory.Packages.props`, `src/Ophi.Web/bun.lock`) go one at a time:
  merge, then `@dependabot rebase` the next and wait for its CI.
- **Many NuGet bumps at once:** squash merges of adjacent `Directory.Packages.props` lines conflict
  with each other. Batch them instead: apply every bump on one branch, run the gate (`gate-runner`), open one PR
  titled with the superseded numbers, merge it, then `gh pr close <N> --delete-branch --comment
  "Superseded by #M"` for each. Don't batch a bump whose release notes need their own review.
- Feature PRs after dependency bumps, so their CI runs on the final dependency set.

## 5. Merge and clean up

```
gh pr merge <N> --squash --delete-branch
```

After the last merge:

```
git checkout main && git pull --ff-only
git fetch --prune && git branch -vv | grep ': gone]'   # local branches whose remote was deleted
```

Delete those with `git branch -D` (`-d` refuses — squash merges leave the branch commits unreachable
from `main`). For the same reason `git cherry` / `git branch --merged` list squash-merged branches as
unmerged; they are content no-ops, don't re-merge. Confirm with `gh pr list --state merged --head <branch>`.

If a merge touched the frontend runtime or auth flows, dispatch E2E on `main` instead of waiting for
the night: `gh workflow run e2e.yml --ref main`.

## Report

One table: each PR's final state (merged / blocked + why / needs user decision), E2E state on `main`,
and branches deleted.
