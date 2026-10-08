# Features — non-obvious behavior

The README lists what Ophi does. This file holds only the behavior a user or operator would get wrong.

## Scraping

- **Tuned adapters:** Amazon and eBay (`StoreConfigs/`). Every other domain uses the generic
  `CommonSelectors` set (Open Graph, Schema.org, common price classes). A user's store config
  overrides the built-in one for the same domain.
- **A store can refuse your network.** Scrapes leave from the host that runs the worker, and some
  retailers block by origin. Test a failing store from that host, not from a desktop browser. A
  challenge page is reported as "Blocked by anti-bot protection", and a URL that keeps hitting one is
  paused. Resume it when your network changes.
- **Adding an adapter without network access:** save the product page as
  `tests/Ophi.Infrastructure.Tests/Scraping/Fixtures/{store}-product.html` and add a case to
  `StoreConfigFixtureTests`. A fixture proves the config matches that snapshot, not that the store
  still serves the same page.
- **Display currency** (`/settings/account`): prices also show converted at ECB reference rates, with
  the native price first. Rates older than 4 days are labelled with their date. Conversion is display
  only — alerts, comparisons and the best-price rollup never use it.

## Alerts and notifications

- Each channel (email, Discord, Telegram, Pushover) has its own per-account switch. Turning one off
  silences only that channel; the in-app notification and webhooks still fire. Password-reset mail
  and the SMTP test send ignore the email switch.
- Discord uses each user's own webhook URL. Telegram and Pushover use one operator bot/app; each user
  saves only a chat id or user key. Their settings cards are hidden when the server has no token.
  Both send plain text, so a product name cannot inject formatting.
- `Alerts:MaxAlertsPerUser` (default 100) counts **active** alerts. Resuming a paused alert counts
  against it. Pausing keeps the trigger history. Both imports (CSV `target_price`, backup) create
  alerts beyond the cap as paused and say so.

## Account backup

`/settings/data` exports one JSON file with settings, tags, groups, stores, products, full price history
and alerts. Credentials and outbound webhooks are never included. Restore only merges: it never
overwrites, it skips a product with an already-tracked URL, and it restores settings only into an
account with no products. The compose `web` service sets `BODY_SIZE_LIMIT=26M` so uploads above
adapter-node's 512K default reach the API.

The operator backs up the whole database with `scripts/db-backup.sh` and `scripts/db-restore.sh`
(README § Database backup and restore).
