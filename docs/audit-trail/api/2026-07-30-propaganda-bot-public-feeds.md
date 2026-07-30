# 2026-07-30 — Add the propaganda- and bot-lens public feeds

The public-column feed frame introduced by `GET /public-posts` (see `api/2026-07-30-public-posts-feed.md`) now has two lensed siblings: `GET /propaganda-public-posts` and `GET /bot-public-posts`. All three share one frame — non-official Reddit/X posts (canonical `kind='official'` exclusion), engagement-ordered with `published_at`/`doc_id` tiebreaks, SQL `LIMIT/OFFSET` at 20/page — and differ only in which analysis task qualifies a doc and what each item carries. All three live in `queries/public_posts.py`. Cross-linked: `docs/audit-trail/ui/2026-07-30-propaganda-bot-public-columns.md`.

## What shipped

- `queries/public_posts.py::get_propaganda_public_posts`: docs with a current done `propaganda` run, flagged or clean — the clean baseline is part of the story. Bot-excluded to match the page's own example pool. Items reuse the `PropagandaExample` drill-down shape (built with the page's `_fetch_example_techniques`), so a feed card is identical to the same doc's card in the entity modal; `party` is always None (the feed excludes officials, the only entities that carry one).
- `queries/public_posts.py::get_bot_public_posts`: docs with a current done `bot` run, every `bot_signals` verdict included. Deliberately NOT bot-author-excluded — this page measures automation; excluding bot-heavy authors would delete its subject. Empty-body docs are excluded in SQL so `total` and page size stay honest. Items reuse `bots.py::_build_flagged_example` with the new `FlaggedExample.label` field populated (the feed mixes verdicts, so each card must say which; the field is None on `BotEntityItem.samples`, which are bot-labeled by construction — `bots_basic.json` re-recorded for the added null field).
- Routes on the owning routers (`routers/propaganda.py`, `routers/bots.py`), same window-XOR-range contract and 30d default as their pages. Response models `PropagandaPublicPostsResponse` / `BotPublicPostsResponse`.
- Tests: lens cases in `test_api_queries_public_posts.py` (officials excluded per lens, clean posts carry empty technique lists and true density, every bot verdict labeled, indicators humanized) and contract snapshots `propaganda_public_posts_basic.json` / `bot_public_posts_basic.json`.

## Why

- The Propaganda and Bot pages' public columns showed per-account rollups that mostly collapsed into pooled catch-all cards; the owner wants the same paginated post feed the sentiment page got, with each page showing its own measurement rather than tone labels everywhere.
