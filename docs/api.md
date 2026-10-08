# API conventions

Base path `/api/v1`. The OpenAPI document owns endpoint paths and shapes. It is served in the
Development environment only, at `/openapi/v1.json` (browsable at `/scalar/v1`).

## Rules the spec cannot express

- **Auth:** everything outside `/auth/*` needs a session cookie (the UI) or `Authorization: Bearer
  ophi_...` (an API key). A key without the `write` scope is refused on every non-safe method — see
  [security.md](security.md).
- **CSRF:** every non-safe request needs an `X-Requested-With` header (any value), or it is refused
  with 403 before auth runs.
- **Empty 2xx:** `202` and `204` have no body. Clients must not treat them as errors.
- **Live updates:** `GET /api/v1/events` is a per-user SSE stream with `scrape-completed` and
  `notification` events.

## Errors

JSON `{error, message}`:

| Status | `error` | Extra |
|---|---|---|
| 400 | `ValidationError` | `details`: property → messages |
| 400/401/403/404/409/422 | the `ApiException` code (`NotFound`, `AlertNotDormant`, `RegistrationDisabled`, …) | — |
| 403 | `ForbiddenRequest` (no `X-Requested-With`) or `InsufficientScope` (key lacks `write`) | — |
| 429 | `TooManyRequests` | — |
| 500 | `InternalError` | `traceId`, matching the server log line |

## Host-root endpoints

| Endpoint | Purpose |
|---|---|
| `GET /health/live` | Process is up |
| `GET /health/ready` | Dependencies reachable (`database`, `wolverine`, `scraping`) |
| `GET /health` | Alias of `/health/ready`; the compose healthcheck uses it |
| `GET /metrics` | Prometheus; in Production it exists only when `MetricsToken` is set, and then needs `Authorization: Bearer <token>` |
