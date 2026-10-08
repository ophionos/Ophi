# Security

## Authentication

- **UI:** cookie sessions (ASP.NET Core Identity, PBKDF2). The cookie is `Secure`-always only in
  Production, so the Testing environment keeps working over HTTP.
- **Scripting:** bearer API keys (`ophi_...`), SHA-256 hashed at rest, with optional expiry and scopes.
  `ApiKeyScopeMiddleware` refuses a key without `write` on any method other than
  GET/HEAD/OPTIONS/TRACE. The rule is method-based, so read-only keys are also refused on non-mutating
  POSTs (`/stores/detect`, `/stores/test`, `/webhooks/{id}/test`). That cost is accepted. Keys are
  created and deleted from a signed-in session only (403 for a key), so a leaked key cannot mint a
  replacement that outlives its expiry or revocation.
- **Security stamp:** each session cookie carries the user's stamp, which `SecurityStampGuard` checks
  on every request (60 s cache). A password change or reset rotates the stamp and signs out every
  other session; the caller gets a fresh cookie.

  > The stamp protects cookie sessions only. API keys survive a password change or reset. After a
  > suspected compromise, delete the keys separately.

- **Account changes** (`/account/*`): changing the email or deleting the account needs the current
  password, so a hijacked session cannot take over or destroy the account. Deletion is a hard delete
  over everything the user owns. These endpoints use the `auth` rate-limit policy.
- **CSRF:** `CsrfMiddleware` refuses any non-safe request without an `X-Requested-With` header.

## Closing sign-up

`Registration:Enabled=false` (compose: `REGISTRATION_ENABLED=false`) makes `POST /auth/register`
return 403 `RegistrationDisabled`. The check runs before the duplicate-email lookup, so it reveals
nothing about existing accounts. Sign-up stays open while the instance has **no** accounts, so a fresh
deployment can create its owner — create yours before you expose the URL. `GET /auth/registration`
reports the state so the UI can hide the form. The default is open.

## Rate limits and the client IP

The global limiter (120/min) partitions by user, or by client IP when anonymous; the `auth` policy
(10/min) partitions by client IP. Creation endpoints (products, alerts, stores, webhooks) have per-user
sliding windows. Development lifts all limits.

The API sees the client IP only through a proxy:

- The API honours `X-Forwarded-For` (last hop only) from loopback plus the CIDRs in
  `ForwardedHeaders__KnownIPNetworks` (compose: `FORWARDED_HEADERS_KNOWN_NETWORKS`). Any caller that
  reaches the API from a trusted address can claim any client IP.
- Connections to a published Docker port arrive from the bridge gateway. Trusting the bridge range
  therefore trusts every caller of that port. The compose stack publishes `:5000` on all interfaces,
  so it leaves the setting empty and all clients share one per-IP bucket. Set the bridge range
  (e.g. `172.16.0.0/12`) only when the API port is unreachable by untrusted clients, for example
  bound to `127.0.0.1` behind a host proxy.
- The SvelteKit hook (`$lib/server/handle.ts`) overwrites `X-Forwarded-For` with the socket address
  and drops client-supplied `Forwarded` / `X-Forwarded-*` / `X-Real-IP`. Behind another reverse proxy
  in front of `web`, set adapter-node's `ADDRESS_HEADER` so the hook forwards the real client.

## SSRF

Every URL-accepting slice runs `.MustBeValidHttpUrl()` (`ProductValidationRules.IsPrivateOrReservedHost`).
It allows only HTTP(S) and blocks `localhost`, `*.local`, `*.internal`, loopback, RFC 1918,
link-local (incl. `169.254.169.254`), CGNAT, `0.0.0.0/8`, and the private IPv6 ranges. IPv6
literals with an embedded IPv4 address (mapped or IPv4-compatible) are unwrapped first.

That check is on the literal host only, for a fast 400. The real control is at connect time: the
scraper's and the webhook dispatcher's HTTP clients use `PublicAddressHandler`. It resolves the host,
drops every address `AddressPolicy` blocks (the ranges above, plus multicast, `240.0.0.0/4` and NAT64
`64:ff9b::/96`), and connects to an address it checked, so DNS rebinding cannot swap the address. It
follows redirects itself, and every hop goes through the same check. A refused scrape is
`BlockedDestination`; its message never contains the resolved address. The client ignores
`HTTP(S)_PROXY`, because with a proxy the check would see the proxy, not the target.

**Browser path (`RequiresJavaScript` stores).** Chromium launches through `PinnedSocksProxy`, an
in-process SOCKS5 proxy on 127.0.0.1. Chromium sends the hostname, and the proxy resolves it and
connects with the same check-then-connect code as the HTTP path (`PinnedConnector`). So every browser
connection is checked: navigation, each redirect hop, sub-resources and WebSockets. TLS runs
end-to-end through the tunnel. The bypass list is `<-loopback>`, because Chromium otherwise sends
loopback directly. WebRTC UDP does not use a SOCKS proxy, so the browser launches with
`--force-webrtc-ip-handling-policy=disable_non_proxied_udp`. Chromium reports a refused connection
as `ERR_SOCKS_CONNECTION_FAILED`, the same code as a down host. A pre-check on the start URL therefore
returns `BlockedDestination` before a page opens; a blocked redirect hop is still refused, but shows
as a generic failure. A blocked HTTP scrape never falls back to Playwright.

## Other measures

- FluentValidation runs as Wolverine middleware for each command that has a `Validator`; EF Core
  parameterizes queries.
- The API sets a strict Content-Security-Policy, `X-Frame-Options: DENY`, `nosniff` and
  `Referrer-Policy`. SvelteKit pages get the same headers from the hook, but no CSP yet.
- `/metrics` requires the `MetricsToken` bearer token. In Production without a token the endpoint
  is not mapped, because its gauges expose every user's tracked products and prices. Outside
  Production an unset token leaves it open, with a startup warning.
- Secrets come from environment variables only.
