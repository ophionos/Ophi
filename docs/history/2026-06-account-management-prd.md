# PRD — Account Management (UX-5)

**Status:** ✅ Complete (2026-06-10) · **Created:** 2026-06-09 · **Parent plan:** [frontend-ux-plan.md](2026-06-frontend-ux-plan.md) §UX-5

---

## 1. Executive Summary

Ophi users today have no way to manage their own account while signed in. The API exposes
register/login/logout/me/forgot/reset only: a signed-in user cannot change their password, name, or
email, and the only path to a new password is the logged-out forgot-password email flow. There is
also no way to delete an account — data leaves the system only by manual DB surgery.

UX-5 closes that gap with three backend vertical slices (`ChangePassword`, `UpdateProfile`,
`DeleteAccount`), a new **Account** tab in the `/settings` area, and the session-security plumbing
that makes password change meaningful: a per-user security stamp so that changing the password
invalidates every other session, not just the one that clicked the button.

**MVP goal:** a signed-in user can change their password, update their name and email, and delete
their account (with all their data) — each gated by their current password where it matters, each
correctly reflected in the session cookie, and each protected by the existing auth rate limiter.

## 2. Mission

Give users full self-service control over their own credentials and data, in keeping with Ophi's
privacy-respecting principle: user data stays under their control, including the right to walk away.

**Core principles**

1. **Current password is the gate** — every sensitive mutation (password change, email change,
   account deletion) requires re-entering the current password. The session cookie alone is not
   enough.
2. **Password change kills other sessions** — a stolen-laptop password rotation must actually lock
   the thief out.
3. **The cookie never lies** — `/auth/me` reads identity from cookie claims, so any mutation that
   changes name/email/credentials re-issues the cookie in the same request.
4. **Deletion is real** — hard delete, full cascade, no soft-delete ceremony (self-hosted app; the
   operator owns backups).
5. **Reuse the house patterns** — vertical slices, domain mutation methods, `auth` rate-limit
   policy, `/settings` tab layout, FluentValidation rules identical to register/reset.

## 3. Target Users

- **The self-hosted operator and their household** — technically comfortable enough to run Docker,
  but the UI must stay consumer-simple (Ophi principle #2).
- **Pain points addressed:** "I want to rotate my password without the email round-trip", "I typo'd
  my name/email at registration", "I'm done with this instance — remove my data."

## 4. MVP Scope

### In Scope

**Core functionality**
- ✅ Change password (current + new, same strength rules as register/reset)
- ✅ Update profile: name and email (email change requires current password; no re-verification)
- ✅ Delete account: password-confirmed, hard delete with full data cascade, signs out
- ✅ Session invalidation on password change via per-user `SecurityStamp` (other sessions die;
  current session gets a fresh cookie)
- ✅ Cookie re-issue on profile change so `/auth/me` stays truthful

**Frontend**
- ✅ `/settings/account` tab (Profile, Password, Danger Zone sections)
- ✅ User menu (top-right dropdown) links to Account settings
- ✅ Auth store refresh after profile change; redirect to landing after delete

**Technical**
- ✅ EF migration adding `SecurityStamp` to `Users`
- ✅ `OnValidatePrincipal` stamp validation with short-TTL memory cache (no per-request DB hit)
- ✅ `auth` rate-limit policy (10/min/IP) on all three endpoints
- ✅ Domain mutation methods on `User` (first behavior on this entity — Phase 8 skipped it)

### Out of Scope

- ❌ Email re-verification flow (tokenized confirm to the new address) — explicitly decided out;
  `EmailVerified` stays a vestigial flag
- ❌ Soft delete / grace period / account export-before-delete (CSV export already exists under
  `/settings/data`)
- ❌ Active-session list ("you are signed in on 3 devices") — no server-side session store exists
- ❌ 2FA / TOTP
- ❌ Admin-managed users (Ophi has no admin concept)

## 5. User Stories

1. *As a signed-in user, I want to change my password by entering my current and new password, so
   that I don't need the email round-trip.* — e.g. rotate after a password-manager audit.
2. *As a user who changed their password, I want every other session signed out, so that a device I
   lost can no longer access my account.*
3. *As a user, I want to fix my display name, so that notifications greet me correctly.*
4. *As a user, I want to change my login email (confirming with my password), so that I can move
   off an old address.*
5. *As a user, I want to delete my account and all my data, so that nothing of mine remains on an
   instance I no longer use.*
6. *As a user, I want clear errors when my current password is wrong, so that I know the mutation
   didn't happen.*
7. *(Technical)* *As the frontend, I want the session cookie re-issued whenever claims change, so
   that `/auth/me` and the header avatar never show stale identity.*

## 6. Core Architecture & Patterns

### New slices

```
src/Ophi.Api/Features/Account/
  ChangePassword.cs    # Command, Validator, Handler, Endpoint
  UpdateProfile.cs
  DeleteAccount.cs
```

A new `Account` feature folder (not `Auth`): `Auth` slices are anonymous/credential-establishing;
`Account` slices operate on the authenticated user. All three follow the consolidated single-file
pattern and dispatch via `bus.InvokeAsync<TResponse>(command)`.

### Domain methods (Phase 8 convention)

`User.Email` and `User.Name` are currently `init`-only — they become `private set`-style mutable
via methods (setters stay accessible for EF/test seeding per house convention):

```csharp
user.ChangePassword(newHash, now);    // sets PasswordHash, rotates SecurityStamp,
                                      // clears reset-token fields
user.UpdateProfile(name, email);      // normalizes email to lowercase
```

### Session invalidation — SecurityStamp

Cookie tickets are self-contained (no server-side session store), so invalidation works by
embedding a `SecurityStamp` claim at sign-in and validating it on each request:

- New `Users.SecurityStamp` column (`string`, e.g. `Guid.NewGuid().ToString("N")`), rotated by
  `ChangePassword` (and by `DeleteAccount` trivially, since the row is gone).
- `AuthSetup` adds `options.Events.OnValidatePrincipal`: compare the cookie's stamp claim against
  the DB value; mismatch → `RejectPrincipal()` + sign-out. The lookup goes through `IMemoryCache`
  keyed by user id with a short TTL (60 s) so steady-state requests don't hit the DB. 60 s is the
  worst-case window in which a revoked session still works — acceptable for this threat model.
- Tickets without a stamp claim (issued before this feature) are rejected → one-time forced
  re-login for existing sessions on deploy. Documented as an expected deploy note.
- `SignInUserAsync` gains the stamp parameter; `Login`/`Register` pass it; `ChangePassword` and
  `UpdateProfile` re-issue the current session's cookie after mutating.

### Frontend

`/settings/account` follows the existing settings-tab pattern (`+page.ts` loader → `await
parent()`; the page reads `data` and calls `api.*` for mutations). The settings `+layout.svelte`
nav gains an **Account** entry (lucide `UserRound`, per the icon-vocabulary doc). The top-right
user menu links to it.

## 7. Features

### ChangePassword
- Inputs: `CurrentPassword`, `NewPassword`.
- Validation: new password = same rules as `Register`/`ResetPassword` (8–128, upper, lower, digit).
- Handler: verify current password via `IPasswordHasher<User>`; wrong → 401-style `ApiException`
  ("Current password is incorrect"); hash new, rotate stamp, clear reset-token fields, save;
  endpoint re-signs-in the current session.
- Effect: all other sessions invalid within the cache TTL.

### UpdateProfile
- Inputs: `Name`, `Email`, `CurrentPassword?` — current password **required iff email differs**
  from the authenticated user's email; name-only edits need no password.
- Validation: name 1–100 chars; email valid format, normalized lowercase; uniqueness checked in
  the handler → conflict error if another user owns it.
- Endpoint re-issues the cookie with the new name/email claims; response mirrors `/auth/me` shape.

### DeleteAccount
- Input: `Password` (required).
- Handler: verify password, hard-delete the `User` row; Products, Alerts, Notifications, Tags,
  ComparisonGroups, WebhookTargets, ApiKeys go via FK cascade (verify each FK is `Cascade`, add
  migration if any is `Restrict`). Endpoint signs the cookie out and returns 204.
- In-flight Wolverine messages referencing the deleted user must not poison queues — handlers
  already tolerate missing entities; covered by a regression test.

### Settings → Account page
- **Profile card:** name + email inputs, password confirm field that appears only when the email
  was edited, Save button, success toast.
- **Password card:** current / new / confirm-new fields, client-side match check, strength hints
  mirroring the server rules, success toast noting "other sessions have been signed out."
- **Danger zone:** Delete account behind the shared `Modal.svelte` confirm — requires typing the
  password; on success clears auth store and redirects to `/`.

## 8. Technology Stack

No new dependencies. Uses existing: .NET 10, Wolverine, EF Core + Npgsql, FluentValidation,
`IPasswordHasher<User>` (ASP.NET Identity PBKDF2), `IMemoryCache` (already in the container),
SvelteKit/Svelte 5, Tailwind, lucide-svelte, Vitest, Playwright.

## 9. Security & Configuration

- **Re-authentication:** current password required for password change, email change, and delete.
  This is also the mitigation for the dual-scheme surface: Bearer API keys reach these endpoints
  too (Smart policy scheme), but a leaked API key cannot rotate the password or delete the account
  without the password itself. No scheme restriction needed.
- **Rate limiting:** all three endpoints use the existing `auth` policy (10/min/IP) — they are
  password-verification oracles and must be brute-force-resistant.
- **Session security:** stamp validation as in §6; logging mirrors existing slices (warn on failed
  password verification with user id, info on success).
- **No new configuration** — no env vars, no settings keys.
- **Enumeration:** email-uniqueness conflict on UpdateProfile does reveal that an address is
  registered. Accepted: `Register` already reveals the same; self-hosted threat model.

## 10. API Specification

All under `/api/v1`, authenticated (cookie or Bearer), `RequireRateLimiting("auth")`.

| Method | Path | Body | Success |
|---|---|---|---|
| `PUT` | `/account/password` | `{ currentPassword, newPassword }` | `200 { message }` + fresh cookie |
| `PUT` | `/account/profile` | `{ name, email, currentPassword? }` | `200 { id, email, name }` + fresh cookie |
| `DELETE` | `/account` | `{ password }` | `204` + cookie signed out |

Errors: `400` validation (FluentValidation problem details), `401` wrong current password,
`409` email already in use, `429` rate-limited.

Frontend client (`$lib/api/client.ts`): `changePassword(...)`, `updateProfile(...)`,
`deleteAccount(...)`.

## 11. Success Criteria

- ✅ User can change password while signed in; a second browser session is rejected afterwards
  (integration-tested with two cookies).
- ✅ User can edit name without a password and email only with one; `/auth/me` reflects the change
  immediately after the call.
- ✅ Deleting an account removes the user and all owned rows (asserted against Postgres tier, not
  just SQLite — FK cascade behavior is provider-sensitive).
- ✅ Wrong current password never mutates anything and returns a distinct error.
- ✅ All three endpoints return 429 under burst (rate-limit test).
- ✅ Coverage holds: backend ≥ 80 %, frontend ≥ 70 %; full suites green (`dotnet test`,
  `bun run test:run`, `bun run check`).
- **UX:** account page reachable in two clicks from anywhere (user menu → Account).

## 12. Implementation Phases

| Phase | Title | Status |
|---|---|---|
| UX-5.1 | Session security foundation (SecurityStamp, stamp validation, cookie re-issue) | ✅ Complete (2026-06-10) |
| UX-5.2 | Backend slices: ChangePassword, UpdateProfile, DeleteAccount | ✅ Complete (2026-06-10) |
| UX-5.3 | Frontend: `/settings/account`, user-menu link, client methods | ✅ Complete (2026-06-10) |
| UX-5.4 | Hardening & docs (Postgres-tier cascade test, e2e, doc updates) | ✅ Complete (2026-06-10) |

### UX-5.1 — Session security foundation (~1 session)
**Goal:** password changes *can* invalidate sessions before any endpoint exists.
- ✅ `SecurityStamp` on `User` + migration (from `src/Ophi.Infrastructure`); backfill existing rows
- ✅ Stamp claim in `SignInUserAsync`; `OnValidatePrincipal` validation with 60 s `IMemoryCache`
- ✅ `User.ChangePassword` / `User.UpdateProfile` domain methods (TDD)
- **Validation:** integration test — tamper/rotate stamp in DB → next request 401; legacy ticket
  without stamp claim → 401.

### UX-5.2 — Backend slices (~1 session)
- ✅ Three slices per §7/§10, endpoint registration in `EndpointRouting`, `auth` rate limiting
- ✅ Handler + validator tests (Moq, `TestDbContextFactory`); integration tests incl. two-session
  invalidation and email-conflict 409
- **Validation:** `dotnet test` green; manual smoke via `/dev-stack`.

### UX-5.3 — Frontend (~1 session)
- ✅ Client methods; `/settings/account` page + loader; settings nav + user-menu entries
- ✅ Component tests (Vitest) for the three cards incl. conditional password-confirm field
- **Validation:** `bun run test:run` + `bun run check` green; svelte-autofixer clean.

### UX-5.4 — Hardening & docs (~½ session)
- ✅ Postgres-tier cascade-delete test (`AccountDeletionCascadeTests` — full ownership graph,
  survivor-user control); deleted-user message-tolerance regression test
  (`DeletedUserMessageToleranceTests` — CheckProductPrice + SendAlertNotification no-op quietly)
- ⏭️ Playwright E2E skipped as not-cheap: the change-password → old-session-401 path is covered
  end-to-end by `AccountEndpointsTests`/`SessionSecurityTests`, and the UI by 15 component tests
- ✅ Update `docs/api.md`, `docs/security.md`, `docs/features.md`, frontend-ux-plan status row,
  `future.md`; archive narrative to `docs/history/` via `/phase-complete`
- **Validation:** full backend + frontend suites; docs index consistent.

## 13. Future Considerations

- Email re-verification flow (would give `EmailVerified` meaning again)
- Active-session management UI (requires a server-side session/ticket store)
- 2FA (TOTP) — stamp infrastructure from UX-5.1 is a prerequisite it can reuse
- Account data export bundle (beyond product CSV) for portability

## 14. Risks & Mitigations

1. **Deploy logs everyone out once** (legacy tickets lack the stamp claim). *Mitigation:* accept
   and document in release notes — it's a one-time cost and strictly safer than honoring
   stamp-less tickets.
2. **FK cascade gaps make DeleteAccount fail or orphan rows.** *Mitigation:* audit every
   User-rooted FK in `OphiDbContext` during UX-5.2; Postgres-tier test (SQLite false-greens on
   cascade/affinity are a known gotcha).
3. **Stamp cache TTL window** — a revoked session lives ≤ 60 s. *Mitigation:* documented
   trade-off; TTL is a single constant if the threat model tightens.
4. **`OnValidatePrincipal` perf regression** (runs on every cookie request). *Mitigation:* memory
   cache + integration assertion that steady-state requests don't query `Users` (log/interceptor
   check); worst case raise TTL.
5. **Queued messages for deleted users poison the `events`/`notifications` queues.** *Mitigation:*
   regression test that handlers no-op when the user is gone.

## 15. Appendix

- Scope sketch: [frontend-ux-plan.md](2026-06-frontend-ux-plan.md) §UX-5 (decisions resolved 2026-06-09:
  delete **in** MVP as hard delete; email change **in**, password-confirmed, no re-verification)
- Related: [security.md](../security.md), [api.md](../api.md),
  [design/icon-vocabulary.md](../design/icon-vocabulary.md)
- Existing slices to mirror: `Features/Auth/ResetPassword.cs` (password rules, hashing),
  `Features/Auth/Login.cs` (password verification, sign-in), `Common/Startup/AuthSetup.cs`
  (cookie events), `Common/Startup/RateLimitSetup.cs` (`auth` policy)
