# Walkthrough 058 — Narrative + propaganda entity routing + UI types

Phase 3b second pass. Extends the entity-registry wiring from walkthrough 057 to the narrative and propaganda aggregators, and mirrors the new fields into the UI type layer + dev fixtures.

With this landed, every aggregator on the three-way frame (sentiment, narrative, propaganda) produces the same shape of per-entity rollup. Phase 4's `EntityProfileCard` component can consume a uniform `EntityProfile` regardless of which page renders it.

---

## What changed

### `analysis/src/reporting/aggregators/narrative.py`

Two fields added to each `NarrativeSummary`:

- `first_seen_entity_profile` — shaped like the `EntityProfile` produced by `entity_registry.*Entity.profile_dict()`. Populated via `resolve_entity()` against the first-seen doc's source type + domain + handle. Catch-all profile when the source tier is known but no registry entry matched.
- `cross_tier` — True when supporting docs span >1 of `{news, officials, public}`. Tier classification matches `resolve_entity`: news rows → `news`; X rows by verified officials → `officials`; everything else → `public`.

The existing `first_seen_tier` field (`elected_official` / `affiliated` / `general_public`) comes from `account_profiles` and is **not** replaced — it tracks a different cut (by-profile classification from the 500-row curated yaml, vs. by-registry match against the 16-seat verified_officials.yaml). Both live side by side on the summary; the UI can pick whichever is relevant.

`_first_seen_info` now returns the X username too so the registry lookup can use the canonical handle instead of re-deriving it. `_is_cross_tier` walks supporting docs once, short-circuiting the moment a second tier is found.

### `analysis/src/reporting/aggregators/propaganda.py`

New dataclass `PropagandaEntityItem`: per-entity propaganda rollup with `total_docs`, `flagged_docs`, `flagged_rate_pct`, `mean_score`, plus the `entity_profile` payload.

`PropagandaOverview` gains `by_news_outlet` / `by_official` / `by_general_public` fields. The aggregator's `_fetch_rows` now LEFT-JOINs `x_posts_raw` + `x_users_raw` for X author-handle resolution. A `_accumulate_entity` helper fans each row into the right bucket via `resolve_entity`; `_finalize_entity_items` sorts entities by `mean_score` desc with catch-alls pushed to the end.

Catch-all sentinels (`CATCH_ALL_OUTLETS` / `CATCH_ALL_X_USERS` / `CATCH_ALL_SUBREDDITS`) come from `entity_registry` so every aggregator uses the same keys.

### `analysis/src/reporting/models/aggregator_models.py`

`NarrativeSummary` extended with the three new optional fields; `to_dict()` serializes them. No breaking changes — older cached snapshots still deserialize via dataclass defaults.

### `ui/src/types.ts`

- New `EntityProfile` interface (kind: outlet / official / subreddit / catch_all + keyed fields).
- New `EntitySentimentItem` + `PropagandaEntityItem` interfaces.
- `PublicSentimentData` gains optional `byNewsOutlet` / `byOfficial` / `byGeneralPublic`.
- `SentimentBreakdown` gains optional `newsNet` / `officialsNet` / `publicNet` + per-tier volumes for the topic-divergence panel.
- `NarrativeSummary` gains optional `first_seen_entity_profile`, `first_seen_tier_group`, `cross_tier`.
- `PropagandaOverview` gains optional `by_news_outlet` / `by_official` / `by_general_public`.

All new fields are optional so older API responses still validate.

### `ui/src/services/fixtures.ts`

Dev-mode mocks now populate the new fields:

- `mockSentiment()`: three-way topic split on byTopic rows; byNewsOutlet/byOfficial/byGeneralPublic lists via new `mockOutletSentiment` / `mockOfficialSentiment` / `mockGeneralPublicSentiment` factories. DRY'd via small `entityItem()` helper that derives volume + netScore from counts.
- `mockNarratives()`: each narrative gets `first_seen_entity_profile`, `first_seen_tier_group`, `cross_tier`.

Mocks are compiled out of production (Vite replaces `import.meta.env.VITE_USE_MOCKS` at build time).

### Tests

- `analysis/tests/test_account_classifier.py`: new `TestNarrativeEntityRouting` class covering all five branches — news matched, official matched, unmatched X → catch-all, cross-tier True when supporting docs span tiers, cross-tier False for single-tier narratives. 5 tests, all pass.
- `analysis/tests/test_propaganda_surfaces.py`: new `TestPropagandaEntityRouting` class covering outlet rollups, official rollups, subreddit + catch-all, sorting by mean_score with catch-alls at the end. 4 tests, all pass.

Both added to existing files to match the neighborhood (no new single-purpose test files).

---

## Why these choices

**Why keep the existing `first_seen_tier` field alongside the new `first_seen_tier_group`?** They measure different things. `first_seen_tier` is an account-profile classification (`elected_official` from the ~500-row curated yaml — which includes every Congress member individually) used for the existing X-narrative tier splits. `first_seen_tier_group` is a coarser 3-way bucket driven by the 16-seat verified_officials registry, aligned with the dashboard frame. A Congress member like @MikieSherrill01 would be `elected_official` in the first sense but `public` in the second (not in our 16 seats). Keeping both lets the UI pick whichever serves the card it's rendering without losing information.

**Why `cross_tier` as a boolean instead of returning the tier set?** The dashboard's "cross-tier narratives" panel (Phase 6) only needs the flag — it groups narratives into "cross-tier" and "single-tier" piles, not three-way-tier set display. If we later need the exact tier set, add it then; today that would be dead payload.

**Why did propaganda's `_accumulate_entity` inline the bucket logic instead of extracting a helper matching sentiment.py's `_route_and_record`?** Propaganda's per-entity counters are tiny (`total / flagged / score_sum`) vs. sentiment's (counts + up to 10 samples). The per-aggregator accumulator shape diverges enough that a shared helper would need to be parameterized on the bucket shape — more abstraction than 3 aggregators justify today. Matches the "inline until 3+ consumers" rule.

**Why are all UI fields optional?** Pre-Phase-3b cached snapshots stay valid. When the job_runner rebuilds snapshots (next pipeline run after deploy), the fields populate; until then, the UI renders gracefully with the old shape. Eventual cleanup is when we bump a snapshot cache version — not forced here.

**Why append tests to existing files instead of new test_narrative_entity_routing.py / test_propaganda_entity_routing.py?** Sibling test files already group by aggregator (`test_account_classifier.py` covers narrative, `test_propaganda_surfaces.py` covers propaganda). New test files would add navigation overhead for < 200 lines of net additions — matches the "inline until a real reason to split" principle from `.claude/memory/feedback_trim_boilerplate.md`.

---

## Verification

```
PYTHONPATH=. .\analysis\.venv\Scripts\python.exe -m unittest \
    analysis.tests.test_account_classifier \
    analysis.tests.test_propaganda_surfaces \
    analysis.tests.test_sentiment_entity_routing \
    analysis.tests.test_entity_registry \
    analysis.tests.test_entity_registries \
    analysis.tests.test_rich_aggregators
```

All pass (17 + 9 + 9 + 22 + 22 + 4 = 83 tests).

```
cd ui && npm run typecheck && npm run build
```

Both clean; build 4.2s, no type errors.

---

## Files touched

- `analysis/src/reporting/aggregators/narrative.py` — entity_registry import, `_first_seen_info` returns the handle, new `_registry_lookup` + `_is_cross_tier`, `_build_summary` populates the three new fields.
- `analysis/src/reporting/aggregators/propaganda.py` — entity_registry import, `PropagandaEntityItem` dataclass, `PropagandaOverview` gains 3 list fields, `_fetch_rows` adds the X handle join, `_build_overview` fans rows into per-entity buckets via new `_accumulate_entity`, `_finalize_entity_items` sorts + serializes.
- `analysis/src/reporting/models/aggregator_models.py` — NarrativeSummary extended + `to_dict()` updated.
- `analysis/tests/test_account_classifier.py` — added `TestNarrativeEntityRouting` (5 tests), hoisted `from typing import Optional` to the header.
- `analysis/tests/test_propaganda_surfaces.py` — added `TestPropagandaEntityRouting` (4 tests) + Optional import.
- `ui/src/types.ts` — EntityProfile, EntitySentimentItem, PropagandaEntityItem; extended PublicSentimentData, SentimentBreakdown, NarrativeSummary, PropagandaOverview.
- `ui/src/services/fixtures.ts` — three-way entity rollup factories + narrative profile attachments.
- `docs/walkthroughs/README.md` — index row for 058.
- `docs/ui-redesign-plan.md` — Phase 3b remaining checkboxes marked done.

---

## Follow-ups carried forward

- **Snapshot cache version bump**: still not implemented. Stale cached snapshots will silently miss the new fields; the UI renders gracefully via the optional types but dashboards won't show the new panels until the job runner rebuilds. Add a cache-key version suffix in the next pass that touches `reporting/cache.py`.
- **Coverage-match logging at snapshot-build time**: still pending. Add to `job_runner.save_snapshots()` so operators can see the registry-match rate degrade if ingest patterns shift.
- **Route updates**: `api/routers/data.py` changes still unnecessary — existing routes serialize via `to_dict()` and fields flow through. Revisit if narrative/propaganda need dedicated entity endpoints for deep drill-down pages in Phase 4.

Phase 3b is fully landed on the backend + type layer. Phase 4 (`EntityProfileCard` UI component) can now consume real + mock data against a stable contract.
