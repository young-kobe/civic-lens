# Wave 2: per-tier entity arrays, rich rollups, and narrative provenance

**Date:** 2026-07-27
**Layer:** api
**Todo:** docs/todos/ui-feature-restoration.md
**Follows:** docs/audit-trail/api/2026-07-27-entity-profile-restoration.md (Wave 1 foundations)

Four panels now serve the old pre-Postgres UI contract's per-tier entity
arrays and rich drill-down rollups, built on Wave 1's `EntityProfileModel`/
`ClassificationSampleModel`/`fetch_entity_profiles`/`fetch_rich_sample_fields`
foundations. All four query modules (`sentiment.py`, `propaganda.py`,
`bots.py`, `narratives.py`) plus `entities.py`/`movers.py` are wired live —
no route/server.py changes were needed since the routers already call these
query functions directly and the new fields are additive Optional/default-`[]`
Pydantic fields.

## What shipped

- **Sentiment** (`queries/sentiment.py`): `by_news_outlet`/`by_official`/
  `by_general_public` arrays of `EntitySentimentItem`, each carrying its own
  `entity_profile`, expressed by-topic/daily-tone series, and (officials
  only) a `received` `ReceivedTone` block (by-topic/by-speaker-tier/
  by-narrative, engagement-weighted net) plus `expressed_alignment`
  (same-party vs cross-party net). News/public tiers also get an `outbound`
  "who this bucket talks about" rollup. Sub-floor sampled X authors fold
  into the `other-x-users` catch-all (`MIN_SAMPLED_AUTHOR_POSTS`/
  `MIN_SAMPLED_AUTHOR_FOLLOWERS`, `MAX_SAMPLED_AUTHOR_CARDS`). A
  `TargetToneMeta` block carries the `gop_collective`/`dem_collective`
  received-tone rollups (first consumer of Wave 1's `GOP_TARGET_ALIASES`/
  `DEM_TARGET_ALIASES`) and the panel-wide same/cross-party alignment
  baseline. A day-by-tier `tone_trend` series and `distribution_samples`/
  `day_samples` rich-sample dictionaries round out the drill-downs.
- **Propaganda** (`queries/propaganda.py`): `by_news_outlet`/`by_official`/
  `by_general_public` ranked `PropagandaEntityItem` arrays (flagged-rate
  desc, catch-alls last) and `examples_by_entity` -- a density-ranked pool
  of flagged docs fanned into per-entity buckets (`EXAMPLES_PER_ENTITY`
  each), keyed by the same entity key the tier arrays use, each example
  carrying its technique spans, author handle, and party.
- **Bots** (`queries/bots.py`): `by_official`/`by_general_public`
  `BotEntityItem` arrays with embedded profiles and confidence-ranked
  `FlaggedExample` samples (indicators humanized from the run's own LLM
  response); `coordination_stats` (`account_reuse`,
  `avg_posts_per_suspected_account` over in-range bot-flagged authors --
  `identical_text_pairs` stays `None`, no Postgres source for the retired
  O(n^2) shingle scan); a day-of-week x hour-of-day `posting_cadence_grid`.
  News is out of scope for this rollup (no `by_news_outlet`).
- **Narratives** (`queries/narratives.py`): first-seen provenance
  (`first_seen_source_type`/`domain`/`tier`/`tier_group`, `first_seen_author`,
  `first_seen_entity_profile` -- outlet, then subreddit, then the author's
  registry entity, falling back to a `sampled_account_profile` for an
  unmatched X author), `cross_tier` (whether in-window member docs span more
  than one of news/officials/public), `inbound_by_link_type`, and
  `top_supporting_docs` (rich `ClassificationSampleModel`s ranked by the
  member doc's current sentiment-run confidence).
- **Entities/movers** (`queries/entities.py`, `queries/movers.py`):
  `EntityProfileResponse.profile` and `ToneMover.entity_profile` now embed
  the full editorial card alongside the existing numeric rollups.

## Verification

Clean-room on a throwaway `postgres:17-alpine`: migrations 0001-0007 apply
cleanly via the real `civic-ingest migrate` binary and are idempotent
(second run is a no-op). Full Python suite: 882 tests, 0 failures both
ungated (283 skipped, no DB) and gated (0 skipped against the live DB), run
three times gated to confirm stability. A composition-smoke script (never
committed) seeded one shared fixture spanning news/reddit/x docs, authors,
entities, target_mentions, sentiment/propaganda/bot results, and a narrative,
then called `get_sentiment_panel`/`get_propaganda_overview`/
`get_bot_activity`/`get_narratives`/`get_movers`/`get_entity_profile`
end to end against the same database, confirming tier arrays, embedded
profiles, and `coordination_stats` all populate as expected. `cd ingest &&
go test ./... -count=1` passes unaffected (Python-only wave). `ingest/`
was not touched.

## Contract snapshots

`bots_basic.json`, `entity_profile_basic.json`, `movers_basic.json`,
`narratives_basic.json`, `propaganda_basic.json`, and
`sentiment_panel_basic.json` were re-recorded (the additive fields above
changed their shape); `docs_basic.json`, `entity_posts_basic.json`, and
`outlets_basic.json` were untouched by this wave and did not need
re-recording. Re-recorded snapshots were confirmed byte-identical across
two additional gated suite runs -- no flaky ordering, no leaked timestamps.

## Owner decisions

None new this wave -- the vocabulary/consolidation questions were already
resolved in Wave 1 (`docs/audit-trail/api/2026-07-27-entity-profile-restoration.md`).
The integration pass found two pieces of query-layer duplication
(`_RANGE_PREDICATE`/`_BOT_EXCLUSION_SQL` shared between `sentiment.py` and
`entities.py`; the `_time_filter` helper shared verbatim across
`propaganda.py`/`bots.py`/`narratives.py`), but both predate this wave (unrelated to the four parallel agents' diffs) and are reported as a follow-up, not fixed here, per the surgical-change rule.

## Follow-ups

- Consolidate the pre-existing `_RANGE_PREDICATE`/`_BOT_EXCLUSION_SQL` SQL
  fragments (`sentiment.py`, `entities.py`) and the `_time_filter` helper
  (`propaganda.py`, `bots.py`, `narratives.py`) into `queries/base.py` --
  cross-cutting tech debt that predates Wave 2, not part of this wave's
  scope.
- Wave 3 UI (old pages/modals restored verbatim) and the final gate
  (contract snapshots + full suite + UI typecheck/build + side-by-side
  eyeball) remain open in `docs/todos/ui-feature-restoration.md`.
