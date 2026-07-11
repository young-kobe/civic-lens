# 2026-07-11 — Modularize the reporting aggregators

The `reporting/aggregators/` layer is now split along cohesive seams: shared
document-presentation and entity-routing policy lives in one place each, wide
SQL rows are typed, and the three oversized aggregators (sentiment, narrative,
bot) are packages of small modules rather than single files. The narrative
endpoint no longer issues a per-narrative query fan-out. Every response model,
JSON field name (including the deliberate per-surface casing), calculation,
threshold, sort, and filter is unchanged — this was a behavior-preserving
restructure verified against the existing aggregator test suite plus three new
unit/regression tests.

## What shipped

- **Shared presentation module** `aggregators/evidence.py`: `build_doc_url`,
  `build_source_label`, `text_snippet`, `sanitize_evidence` (+ `SNIPPET_MAX_CHARS`,
  `MAX_EVIDENCE_PER_SAMPLE`). Previously these were private helpers in
  `narrative.py` that `bot.py` / `propaganda.py` / `review.py` imported
  cross-module, plus an inline URL duplicate in `sentiment.py`. All now import
  one implementation. `narrative`-internal `_SOURCE_LABELS` stayed in narrative
  (it is not shared). Covered by `tests/test_evidence_helpers.py`.
- **Canonical entity routing in the entity layer** (`entity_registry.py`):
  `route_reporting_entity(...) -> EntityRoute(tier, key, kind, entity_profile)`
  and `verified_officials_profile()`, plus the `CATCH_ALL_VERIFIED_OFFICIALS`
  sentinel moved here from `sentiment.py`. `bot`, `propaganda`, and narrative's
  `_registry_lookup` route through it; the plain news/X/subreddit catch-all
  *display copy* stays local to each aggregator (the wording deliberately
  differs per surface — bot/narrative singular, sentiment/propaganda plural),
  so `EntityRoute.entity_profile` is `None` for those and the shared profile is
  returned only for matched entities, account cards, and verified-officials.
  This removed the `bot -> sentiment` and `entity_posts -> sentiment` constant
  imports. `sentiment` re-exports the sentinel for back-compat. Covered by
  `tests/test_entity_route.py`.
- **Typed SQL rows** `aggregators/rows.py`: `SentimentRow` (28 cols),
  `TargetMentionRow` (21 cols), `NarrativeSupportingDocRow`, `BotDetectionRow`,
  each a frozen dataclass with `from_row`. Replaced the wide positional tuple
  unpacks and the `r[0], r[1], r[4], r[20]` magic-index access in
  `get_public_sentiment`; the two identical 21-col target-mention unpacks now
  share one row type.
- **`narrative/` package** (`aggregator.py` / `repository.py` / `projector.py`):
  the per-narrative N+1 is gone. `NarrativeRepository` fetches every fact
  (`source_breakdowns`, `timelines`, `sentiment_stats`, `mean_confidence`,
  `propaganda_scores`, `bot_pushed_fractions`, citation details, cross-tier
  rows, first-seen info, supporting docs) in batched `... IN (<ids>)` queries
  keyed by `narrative_id`, using `ROW_NUMBER() OVER (PARTITION BY narrative_id
  ...)` windows for the per-narrative top-N caps; the projector assembles
  `NarrativeSummary` purely from those fact maps. Query count is now constant in
  the number of narratives (was `~1 + 13N`). Guarded by
  `tests/test_narrative_batching.py`. `is_cross_tier` now delegates tier
  assignment to the canonical `resolve_entity` (was a hand-rolled
  `LOWER(username) IN officials_handles` check) so narrative tiering cannot
  drift from the sentiment/bot dashboards — byte-identical, since `officials`
  keys already include alias handles and `resolve_entity` canonicalizes the
  lowercased handle identically.
- **`sentiment/` package** (`aggregator.py` / `samples.py` / `favorability.py` /
  `target_tone.py` / `entities.py`): the ~1810-line file split into a thin
  orchestrator plus cohesive modules for sample building, GOP favorability,
  received/expressed/outbound target tone, and entity routing/rollup. External
  imports (`SentimentAggregator`, `MIN_TARGET_SAMPLE_N`,
  `CATCH_ALL_VERIFIED_OFFICIALS`, `_build_sample_dict`, `_build_doc_targets`,
  `_extract_topic`) preserved via the package `__init__`.
- **`bot/` package** (`aggregator.py` / `repository.py` / `metrics.py` /
  `entities.py` / `narratives.py` / `types.py`): the untyped ~11-key dict passed
  between phases is now the `BotDetectionAggregate` dataclass; pure calculations
  (coordination index, text-similarity shingles/jaccard, link-domain
  concentration) are isolated in `metrics.py`; narrative amplification is its
  own module. The snake_case `asdict` casing on `BotEntityItem` in `overview`
  is preserved.

## Why

- The aggregators had each independently rebuilt the same reporting concerns
  (URLs, source labels, entity routing, catch-all profiles, evidence
  formatting), which produced cross-aggregator imports of nominally-private
  helpers/constants — a coupling smell with circular-import pressure and a
  tier-drift risk (four near-duplicate copies of the tier/catch-all branching).
- The narrative endpoint's per-narrative query fan-out (~13 queries × N
  narratives, 200+ per request at the snapshot limit) was the layer's largest
  runtime cost.
- `sentiment.py` had grown into a god-object merging several analytical
  products; wide positional SQL tuples (up to 28 columns, unpacked in more than
  one place) were the biggest correctness-risk surface.

## Verification

- Full suite green except the 6 pre-existing `test_api.py` cases (they require a
  live server on `:8000`): `python3 -m unittest discover analysis/tests` →
  `Ran 442 tests, FAILED (failures=1, errors=5)`, all six in `test_api`.
- New tests: `test_evidence_helpers`, `test_entity_route`,
  `test_narrative_batching`.
- The snapshot cache contract and every aggregator method signature are
  unchanged, so `job_runner.save_snapshots()` and `api/routers/data.py` needed
  no changes.

## Review fixes (GOP favorability)

Post-refactor review surfaced pre-existing bugs in the favorability path
(`sentiment/favorability.py`), now fixed — these DO change the favorability
JSON, unlike the rest of this pass:

- `overall_gop_stance` is normalized with `str(...).lower()` (the four-value
  enum can arrive off-enum-cased; movers already did this) and `mixed` is folded
  into `neutral`. Previously a `mixed` row landed in `total` but in no exported
  bucket and was dropped from `by_platform` entirely, so `favorable + unfavorable
  + neutral` under-summed 100 and per-platform counts silently lost mixed rows.
  `mixed` is now non-directional-but-counted (in the denominator, zero net),
  matching the movers favorability treatment; `netFavorability` is unchanged.
- `gopByPlatform.group` uses a stable label map (`x_post -> X`, `reddit ->
  Reddit`, `news -> News`) instead of `platform.capitalize()`, which produced
  `X_post`.
- Covered by `tests/test_favorability_stance.py`.

Also fixed two stale comments in `narrative/repository.py` that claimed the
projector merges/re-caps cross-narrative edges (it is `get_citation_details`
that does), and converted a bare `assert` in `test_narrative_batching.py` to an
explicit `raise` (survives `python -O`).

## Follow-ups

- The plain news/X/subreddit catch-all display copy is intentionally duplicated
  across aggregators (singular vs plural wording). Unifying that copy is a
  deliberate behavior change (it alters `entityProfile.blurb` bytes) and was
  left out of this behavior-preserving pass.
