# 2026-07-10 — Enrich samples with engagement/author, add per-tier daily tone, wire outlet profiles

Phase 2 of the UI depth overhaul (`docs/todos/ui-depth-overhaul.md`): the
aggregators now surface stored-but-never-shipped data — collection-time
engagement counts and X author metadata on every classification sample, a
per-day per-tier tone series, per-example bot evidence, and the previously
orphaned outlet cross-signal rollup. API + UI entries:
`../api/2026-07-10-outlet-profiles-endpoint.md`,
`../ui/2026-07-10-phase2-enrichment-consumption.md`.

## What shipped

- **Shared enrichment plumbing** (`aggregators/base.py`):
  `REDDIT_ENGAGEMENT_JOIN_SQL` (reddit_posts_raw only — comments have no raw
  table since migration 005), `SAMPLE_ENRICHMENT_SELECT` (11 projection
  columns), and `build_sample_engagement` / `build_sample_author` builders.
  X samples carry `{retweets, replies, likes, quotes}`; reddit posts carry
  `{score, num_comments}`; news and reddit comments carry null — nothing is
  fabricated for sources that store nothing. Authors (X only, from
  x_users_raw): handle, display_name, avatar_url, verified_type,
  followers_count, account_created_at.
- **`toneTrend`** on `sentiment_{window}` snapshots
  (`aggregators/sentiment.py::_format_tone_trend`, accumulated per
  day+tier in `_aggregate_rows`): `[{date, news: {net, volume},
  officials: {...}, public: {...}}]`, dates ascending, capped at 30 days.
  A tier-day below `MIN_TARGET_SAMPLE_N` reports net=null (the UI draws a
  gap) with its honest volume — a three-post day never draws a ±100 spike.
- **Samples enriched end to end**: `sentiment.py::_build_sample_dict` (and
  every collector feeding topic/strength/entity samples),
  `entity_posts.py` (live `/entity-posts` pagination), and
  `narrative.py::_top_supporting_docs` all emit `engagement` + `author`.
  `ClassificationSample` dataclass + serializer extended.
- **Per-example bot evidence** (`aggregators/bot.py::_flagged_example`):
  `FlaggedExample` now carries `confidence`, humanized `indicators`
  (capped at 4; snake_case slugs never render raw), and truncated
  `reasoning` — the Bot Detector's post cards can finally show the per-post
  WHY, not just the excerpt.
- **Amplification id fix**: `NarrativeAmplification.id` is now the REAL
  `narrative_id` (was a synthetic 1..3 index), so the UI's
  `#narratives?open=<id>` deep link from the Bots page resolves to the
  right story.
- **Outlet profiles wired** (`aggregators/outlet.py`): window support,
  `net_tone` on the standard -100..+100 points scale (was a -1..1 mean
  with zero consumers), `bot_rate_pct`, a `MIN_PROFILE_VOLUME=5` floor, and
  the bots-included-by-design disclaimer in the payload.
  `job_runner.save_snapshots()` writes `outlet_profiles_{window}` for all
  four windows.
- Tests: new `tests/test_sample_enrichment.py` (toneTrend suppression +
  ordering, per-source enrichment rules, bot evidence, real amplification
  id); `test_rich_aggregators.py` outlet test rewritten to the new
  contract; five test schemas extended with the enrichment columns +
  reddit_posts_raw.

- **Sampled-author cards** (added 2026-07-11, while Reddit ingestion is
  offline and the Public column held a single catch-all): unmatched
  public-tier X authors clearing BOTH floors — `MIN_SAMPLED_AUTHOR_POSTS`
  (3) and `MIN_SAMPLED_AUTHOR_FOLLOWERS` (1,000) — get their own
  `kind='account'` card (capped at 12 by volume), built from their public
  X profile via `entity_registry.sampled_account_profile` (name, bio,
  follower count; no lean, no party — we know nothing editorial about
  them). Everyone below the floors folds back into "Other X users" via
  `sentiment.py::_consolidate_sampled_authors` — pooled, never dropped.
  The follower floor keeps small personal accounts out of ranked cards.
  `u.description` joined into `SAMPLE_ENRICHMENT_SELECT` (author.bio on
  samples) to power the card blurbs.

## Why

- The Phase 1 UI ships PostCards and trend panels shaped to display
  engagement, authorship, per-post bot evidence, and per-group trends —
  all data the ingest layer already stored but the aggregation layer
  dropped on the floor. The outlet rollup was fully implemented and
  reachable from nothing.
- The reddit-comments join intentionally covers posts only:
  `reddit_comments_raw` was dropped in migration 005 and comment ingestion
  stores no raw rows — absent beats fabricated.

## Follow-ups

- Engagement on received-tone (target-mention) samples is still the summed
  weight only; per-sample counts there are a possible later increment.
- Phase 3 (entity hub cross-links, search) in
  `docs/todos/ui-depth-overhaul.md`.
