# 2026-07-30 — Add GET /public-posts, the sentiment page's public-column feed

The sentiment page's public column is now fed by a dedicated endpoint: `GET /api/v1/public-posts?window=&topic=&page=` returns an engagement-ordered, SQL-paginated page of non-official Reddit/X posts as full `ClassificationSampleModel` items (label, confidence, evidence, engagement, author). Cross-linked: `docs/audit-trail/ui/2026-07-30-public-feed-column-and-topic-default.md`.

## What shipped

- `queries/public_posts.py`: count + page statements sharing one predicate — current done `text` runs joined to `sentiment_results` (the label/confidence contract), confidence floor, range + bot-exclusion gates from the sentiment panel, `source_type IN ('reddit_post','x_post')`, and officials excluded by the canonical `kind='official'` predicate (see `api/2026-07-30-canonical-officials-predicate.md`). Pagination is SQL `LIMIT/OFFSET` (`PUBLIC_POSTS_PAGE_SIZE = 20`) — deliberately not `/entity-posts`' fetch-then-slice, this is corpus-wide.
- Topic filter: a `LEFT JOIN LATERAL` dominant-topic subselect mirroring `_DOC_TOPICS_SQL` semantics (current targets runs, non-'Other', confidence-floored; highest mention count, alphabetical tiebreak). `topic=General` means "no resolved topic"; each item is stamped with that same attribution — never a keyword guess.
- Ordering: summed raw engagement (X retweet/reply/like/quote + Reddit score/comments — a cross-platform reach proxy, labeled as such in the UI) DESC, then `published_at DESC, doc_id DESC` for a total, reproducible order. Confidence is label certainty, not relevance — display only.
- `models/sentiment.py::PublicPostsResponse`; route on `routers/sentiment.py` (same window-XOR-range contract as `/sentiment`, default `30d`).
- Tests: `test_api_queries_public_posts.py` (PG-gated: officials excluded regardless of editorial, exact topic attribution, ordering tiebreaks, disjoint pages with stable total, label+confidence on every item) and `contract/test_public_posts_contract.py` + `public_posts_basic.json`.

## Why

- The public column previously rendered per-account rollup cards, which mostly collapsed into one pooled "Other X users" card — the reader asked for the actual posts. A paginated feed needs server-side topic/window filtering and honest totals, which the `/sentiment` payload can't provide.
- The `byGeneralPublic` / `publicOutboundTargets` payload fields stay: entity deep-link resolution, DataDesk, and the "Who the public is talking about" footer still read them.
