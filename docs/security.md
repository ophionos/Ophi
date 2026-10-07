# Ophi: Security & Configuration

## Authentication

- **UI:** cookie-based sessions, ASP.NET Core Identity (PBKDF2 password hashing), secure/httpOnly/
  sameSite cookie flags, CSRF protection via anti-forgery tokens.
- **Scripting:** bearer API keys (`Authorization: Bearer ophi_...`), SHA-256 hashed at rest, optional
  expiry and per-key scopes. Valid on every endpoint, not just `/auth/*`.
- **Cookie `Secure` policy** is `Always` only in Production (so the Testing environment isn't broken).
- **Security stamp:** every session cookie carries the user's `SecurityStamp` claim, validated
  against the database on each request (`SecurityStampGuard`, 60 s memory-cached). Rotating the
  stamp — any password change or reset — invalidates all sessions issued before it. Tickets
  without a stamp claim (pre-feature) are rejected, so deploying the feature forces a one-time
  re-login.

  > **The stamp protects cookie sessions only.** API keys carry no stamp and survive a password
  > change or reset — if a reset is your response to a suspected compromise, delete the keys
  > separately.

- Rate limiting on auth endpoints, plus per-user sliding windows on resource creation. Anonymous
  and `auth` partitions are keyed on the client IP, which the API only sees through a proxy (the
  SvelteKit `/api` hook on compose, host Caddy on the Pi):
  - The API honours `X-Forwarded-For` (last hop only) from loopback plus the CIDRs in
    `ForwardedHeaders__KnownIPNetworks` (comma-separated), set via `FORWARDED_HEADERS_KNOWN_NETWORKS`.
    Anything that reaches the API from a trusted address can claim any client IP. Connections to a
    published port arrive through docker-proxy from the bridge gateway, so trusting the bridge also
    trusts every caller that can reach that port.
  - Pi: defaults to `172.16.0.0/12` (Docker's default pools). Safe because the API port is bound to
    `127.0.0.1`, so only host processes (Caddy) reach it.
  - Full compose: empty by default (loopback only), so every client shares one per-IP bucket. The
    API port is published on all interfaces, and trusting the bridge would let any caller of `:5000`
    pick its own partition. Set the bridge range only when `:5000` is not reachable by untrusted
    clients. On WSL2, Windows clients reach `web` through docker-proxy too, so the hook sees the
    gateway for all of them and the setting gains nothing there.
  - The SvelteKit hook (`$lib/server/handle.ts`) overwrites `X-Forwarded-For` with the socket
    address and drops client-supplied `Forwarded` / `X-Forwarded-*` / `X-Real-IP`. Caddy ignores
    incoming forwarded headers from untrusted clients by default.
  - Behind another reverse proxy in front of `web`, set adapter-node's `ADDRESS_HEADER` so the hook
    forwards the real client rather than that proxy.
- Password reset is a tokenized email flow (`/auth/forgot-password`, `/auth/reset-password`);
  completing a reset rotates the security stamp, signing out existing sessions.
- **Account self-service** (`/account/*`): password change rotates the stamp (other sessions die,
  the caller gets a fresh cookie); email change and account deletion are gated on the current
  password, so a hijacked session can't silently take over or destroy the account. Deletion is a
  hard delete with FK cascade over all owned data. All three endpoints use the `auth` rate-limit
  policy.
- **Closing sign-up:** `Registration:Enabled=false` (compose: `REGISTRATION_ENABLED=false`) makes
  `POST /auth/register` return 403 `RegistrationDisabled` — checked before the duplicate-email lookup,
  so it leaks nothing about existing accounts. It still allows sign-up while the instance has **no**
  accounts, so a fresh deployment can create its owner; create yours before exposing the URL.
  `GET /auth/registration` reports the state so the UI hides the form and links. Defaults to open.

## Environment Variables

```env
# Database — PostgreSQL
DB_PROVIDER=postgres
ConnectionStrings__Postgres=Host=db;Port=5432;Database=ophi;Username=ophi;Password=ophi

# Application URL (CORS + links in alert emails)
APP_URL=https://ophi.example.com

# Metrics endpoint protection — REQUIRED in Production. The API refuses to start without a
# non-empty value; it gates the Prometheus /metrics endpoint. Generate with `openssl rand -hex 32`.
MetricsToken=

# Email (SMTP)
SMTP_HOST=smtp.example.com
SMTP_PORT=587
SMTP_USER=notifications@ophi.app
SMTP_PASS=secret
SMTP_FROM=Ophi <notifications@ophi.app>

# Discord price-alert webhook (optional)
DISCORD_WEBHOOK_URL=

# Telegram / Pushover (optional) — one operator bot / app; each user saves only their chat id / user key.
TELEGRAM_BOT_TOKEN=
TELEGRAM_BOT_USERNAME=
PUSHOVER_APP_TOKEN=

# Sign-up (compose maps it to Registration__Enabled). Set false once your own account exists.
REGISTRATION_ENABLED=true
```

Most scraping/alert tuning lives in per-user settings (DB-backed), not environment variables —
see the application settings below.

## Application Settings

```json
{
  "Scraping": {
    "DefaultIntervalMinutes": 60,
    "MinIntervalMinutes": 15,
    "MaxRetries": 3,
    "TimeoutSeconds": 30
  },
  "Alerts": {
    "CooldownMinutes": 60,
    "MaxAlertsPerUser": 100
  },
  "Registration": {
    "Enabled": true
  },
  "DataRetention": {
    "PriceHistoryDays": 365
  }
}
```

## Security Measures

- Input validation and sanitization (FluentValidation on every command)
- SQL injection prevention (EF Core parameterized queries; no raw SQL in app code)
- XSS prevention (output encoding) + Content-Security-Policy on API responses. HTML pages from the
  SvelteKit server get `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff` and
  `Referrer-Policy` from the hook, but no CSP yet (Chart.js and inline styles need work first); on
  the Pi, Caddy sets the page headers.
- HTTPS enforcement; secrets via environment variables only
- **API key scopes** are enforced by `ApiKeyScopeMiddleware`: a key without the `write` scope is
  rejected with 403 on any method outside GET/HEAD/OPTIONS/TRACE. Cookie sessions are unaffected.
  - Because the rule is method-based rather than per-endpoint, the handful of non-mutating `POST`
    endpoints are also refused for read-only keys — `POST /stores/detect` most notably, plus
    `/stores/test` and `/webhooks/{id}/test` (those two do make outbound requests). That is the
    accepted cost of a blanket rule that cannot be forgotten on a new slice; a read-only key that
    needs store detection wants the `write` scope, or the endpoint wants to become a GET.
- **SSRF protection** on user-supplied URLs: every URL-accepting slice runs `.MustBeValidHttpUrl()`
  (`ProductValidationRules.IsPrivateOrReservedHost`), which bounds the scheme to HTTP(S) and blocks
  `localhost` / `*.local` / `*.internal` plus loopback, RFC1918, link-local (incl. `169.254.169.254`),
  CGNAT, `0.0.0.0/8`, and the IPv6 unspecified / link-local / site-local / unique-local ranges.
  IPv6 literals carrying an embedded IPv4 address are unwrapped first — both the mapped form
  (`[::ffff:169.254.169.254]`) and the deprecated IPv4-compatible form (`[::169.254.169.254]`, which
  `IPAddress.IsIPv4MappedToIPv6` does not recognize) — so each is blocked the same as its dotted form.
  - **Known gaps (accepted under the single-user trust model):** the check is on the literal host, so
    a DNS name that *resolves* to a private address (e.g. `169.254.169.254.nip.io`) is not blocked,
    and the guard is not re-applied after an HTTP redirect — `HttpClient` follows redirects by default
    in both the scraper and the webhook dispatcher. Closing either requires a resolving/pinning
    primary handler; revisit if the instance is ever opened to untrusted signups. An internet-facing
    instance should close sign-up (see Authentication) so strangers can't reach these slices at all.
- `/metrics` is gated behind `MetricsToken` in Production.
