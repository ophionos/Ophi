# Ophi: API Conventions

**Base URL:** `/api/v1`

## Where the endpoints are listed

The OpenAPI document is the one owner of endpoint paths and request/response shapes. It is served in
the **Development** environment only:

- `/openapi/v1.json` — the spec
- `/scalar/v1` — browsable reference UI

The frontend's typed schema (`src/Ophi.Web/src/lib/api/schema.generated.ts`) is generated from it with
`bun run gen:types` (expects the native API on :5041 — see `/dev-stack`). Validation rules live in each
slice's `Validator` under `src/Ophi.Api/Features/`.

This file used to hand-list ~70 endpoints; it had drifted (missing routes, a wrong 429 body, a partial
query-parameter list) because nothing checked it against the code. It now holds only what the spec
can't express.

## Authentication

All endpoints except `/auth/*` require auth. Two schemes are accepted everywhere:

- Session **cookies** — the UI.
- Bearer **API keys** — `Authorization: Bearer ophi_...`, for scripting. Keys carry scopes; any
  non-safe method (anything but GET/HEAD/OPTIONS/TRACE) needs `write`, enforced for every endpoint by
  `ApiKeyScopeMiddleware`. That includes non-mutating POSTs such as `/stores/detect`.

## Request rules

- **CSRF:** every non-safe request needs an `X-Requested-With` header (any value) or it is refused with
  403 before reaching auth. Send `Content-Type: application/json` with JSON bodies.
- **Rate limits:** the `auth` policy (login, register, password reset, account changes) and per-user
  sliding windows on resource creation. Limits and the client-IP trust model are in
  [security.md](security.md).

## Responses

- `202 Accepted` and `204 No Content` have empty bodies — clients must not treat them as errors.
- `DELETE /account` is a hard delete with FK cascade over everything the user owns. `PUT
  /account/password` rotates the security stamp: every other session is signed out and the caller gets
  a re-issued cookie. `PUT /account/profile` requires `currentPassword` only when the email changes.

## Error format

Errors are JSON `{error, message}`, with extra fields where noted:

| Status | `error` | Extra |
|--------|---------|-------|
| 400 | `ValidationError` | `details`: property → messages |
| 400/401/404/409/422 | the `ApiException` code (`Unauthorized`, `NotFound`, `EmailInUse`, `AlertNotDormant`, …) | — |
| 403 | `ForbiddenRequest` (missing `X-Requested-With`) or `InsufficientScope` (API key lacks `write`) | — |
| 429 | `TooManyRequests` | — |
| 500 | `InternalError` | `traceId` — matches the server log line |

## Host-root endpoints

Served outside `/api/v1`:

| Endpoint | Purpose |
|----------|---------|
| `GET /health/live` | Liveness — process is up |
| `GET /health/ready` | Readiness — dependencies reachable, incl. DB |
| `GET /health` | Legacy alias; returns per-check status (`database`, `wolverine`, `scraping`) |
| `GET /metrics` | Prometheus exposition; gated by `MetricsToken` in Production |

Live updates are a per-user SSE stream at `GET /api/v1/events` (`scrape-completed` and `notification`
events); the frontend consumes it through `liveRefresh()` — see `docs/agent-notes.md`.
