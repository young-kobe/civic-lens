# 2026-07-30 — Propaganda and Bot public columns become lensed post feeds

"The Public" on the Propaganda page and the Bot Detector now renders the same paginated post-card feed as the sentiment page, each through its own lens (see `docs/audit-trail/api/2026-07-30-propaganda-bot-public-feeds.md`): Propaganda shows scored posts with technique flags highlighted (clean posts included), Bots shows bot-scored posts with every verdict labeled. The officials columns keep their ranked account rollups; public per-account rollups leave those two pages, matching the sentiment precedent.

## What shipped

- `components/common/PaginatedPostFeed.tsx`: the shared feed frame (PostCards behind a Load-more, reset on filter-identity change, stale-response guard), extracted from the sentiment page's `PublicPostFeed` — which is now a thin wrapper over it, as are the two new page-local feeds (`PropagandaPublicFeed` in `Propaganda.tsx`, `BotPublicFeed` in `BotActivityProfiler.tsx`). One state machine, three callers.
- Cards reuse the pages' existing adapters (`propagandaExampleToPostCard`, `flaggedExampleToPostCard`), so a feed card renders identically to the same doc inside an entity modal. `flaggedExampleToPostCard` now maps the `FlaggedExample.label` verdict to display copy ("Suspected automation" / "Suspicious patterns" / "No automation flags" / "Inconclusive"), falling back to the bot wording for entity-card samples, which carry no label.
- Both columns carry sampling notes ("ordered by engagement, a reach proxy, not verified audience — a sample, not the full corpus") and honest empty copy per lens.
- Public-tier entity rollups (`byGeneralPublic`) stay in both payloads: entity deep links (`?entity=`) still resolve through them, and the Propaganda leaderboard/`TopFlaggedLeaderboard` still ranks across all tiers.

## Why

- The public rollups on these pages mostly collapsed into one pooled catch-all card — near-zero information — while the discourse itself was a modal away. The feed surfaces it directly, and each page shows its own measurement (technique flags, automation verdicts) instead of borrowing tone labels.
