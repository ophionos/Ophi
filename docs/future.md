# Ophi: Future Considerations

> Already shipped (do not re-add): PostgreSQL migration + durable Wolverine messaging, user-agent
> rotation across browser profiles, password reset, account self-service (change password / update
> profile / delete account), Prometheus metrics, API keys, outbound webhooks, SSE live updates,
> list view as the dashboard default past 12 products.
> See [history/README.md](history/README.md) for the narratives.

## Post-MVP Features

- Additional notification channels beyond Telegram/Pushover (e.g. SMS)
- Browser extension (one-click tracking)
- Mobile apps with push notifications
- CAPTCHA handling
- Proxy rotation
- Operator-level database backup/restore (`pg_dump`) — per-user backup bundles shipped (B-1)
- Enhanced offline PWA shell
- Account hardening follow-ups (from the UX-5 PRD): email re-verification flow, active-session
  management UI (needs a server-side ticket store), 2FA/TOTP (SecurityStamp infra is the
  prerequisite, already shipped)

## Deferred UX Work

The 2026-06 density quick wins (shared `StatCard`, dashboard toolbar, list-density toggle,
detail-page rebalance) and the UX modernization phases UX-1–6 (URL-driven filters, `/settings`
area, Intl price formatting, palette actions, bulk actions, account management) have all
shipped — see [history/2026-06-ux-modernization.md](history/2026-06-ux-modernization.md) and
[history/2026-06-frontend-ux-plan.md](history/2026-06-frontend-ux-plan.md). List view as the
dashboard default past N products was the last item outstanding; it shipped in #153, so nothing
from that plan remains.

## Integration Opportunities

- Price data APIs (Keepa, CamelCamelCamel)
- Notification services (Telegram, SMS)
- UPC database for product matching

## Known Risks

| Risk | Mitigation |
|------|------------|
| Website blocking | User-agent rotation, throttling, user-configurable selectors |
| Extraction accuracy | Validation bounds, multiple selector strategies, user reporting |
| Job scalability | Queue rate limiting, configurable frequency, horizontal scaling |
| Email deliverability | Proper SPF/DKIM/DMARC, in-app alert history backup |
