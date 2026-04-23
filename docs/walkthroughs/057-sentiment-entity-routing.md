# Walkthrough 057 — Sentiment aggregator three-way entity routing

Phase 3b of the UI Redesign Plan (first pass). Wires the entity registries from walkthrough 054 into the sentiment aggregator so every row buckets into one of three tiers — news outlets / verified officials / general public — alongside the existing platform / topic / time-window / day-of-week rollups. Also adds a per-topic three-way split to power the Phase 5 topic-divergence panel.

Scope-limited to the **sentiment** aggregator. Narrative + propaganda extensions land in a follow-up walkthrough (058); UI types + fixtures come with them.

---

## What changed

### `analysis/src/reporting/entity_registry.py` (new — then trimmed)

One file, ~355 lines. The whole "entity system":

- Canonicalizers: `canonicalize_news_domain` (strips `www.`), `canonicalize_subreddit` (strips `r/`), `canonicalize_handle` (strips `@`). Each lowercases + trims. The contract for "matches the registry" is these functions — any drift between producer (registry loader) and consumer (aggregator) hides bugs, so they live in one place.
- Frozen dataclasses: `OutletEntity`, `OfficialEntity`, `SubredditEntity`. Each has a `profile_dict()` method that serializes to the UI-facing `EntityProfile` shape (renames `partisan_lean → lean`, `tilt → lean`, etc. so the UI sees a uniform key regardless of source).
- `EntityRegistry` dataclass holding three `{canonical_key → entity}` dicts. Populated via a generic `_build_index` helper that unifies what used to be 3 near-duplicate loader functions.
- Module singleton (`get_registry()` + `reload_registries()` for tests) with a `Lock` so concurrent first-call doesn't race.
- Tier classification (`resolve_entity`) + catch-all sentinels (`CATCH_ALL_OUTLETS`, `CATCH_ALL_X_USERS`, `CATCH_ALL_SUBREDDITS`) + `catch_all_profile()` factory.

Pre-check audit (walkthrough 055) committed us to normalizing at match time (not ingest). This module is the one place that normalization happens.

### `analysis/src/reporting/models/aggregator_models.py`

- New `EntitySentimentItem` dataclass: per-entity `{positive, negative, neutral, volume, netScore, entity_profile, classification_samples}`. Used for all three tiers — one generic type instead of three near-identical ones.
- `PublicSentimentResult` gains `byNewsOutlet`, `byOfficial`, `byGeneralPublic` (lists of `EntitySentimentItem`) as optional fields defaulting to `[]` so older cached snapshots stay valid.
- `TopicSentiment` gains `newsNet`, `officialsNet`, `publicNet` (optional floats) + `newsVolume`, `officialsVolume`, `publicVolume` (int, default 0). The floats are None when a tier has zero volume so the UI can distinguish "no data" from a real zero.
- `to_dict()` extended to serialize the new fields via a new `_entity_item_to_dict()` helper.

### `analysis/src/reporting/aggregators/sentiment.py`

- SQL `fetch_task_rows` gains a `extra_joins` for `x_posts_raw` + `x_users_raw` so every sentiment row carries the post author's X handle (column becomes the 10th in the tuple). No-op for non-X rows (`x.tweet_id` is NULL).
- `_aggregate_rows` accumulator grows three new dicts (`by_news_outlet`, `by_official`, `by_general_public`) plus a `by_topic_tier` composite-key dict for the divergence split.
- Each row, after the normal rollups, passes through `_route_and_record()` which calls the shared `resolve_entity()` + builds the per-entity accumulator (counts + up to 10 classification samples).
- `_build_result` assembles the new `byNewsOutlet` / `byOfficial` / `byGeneralPublic` lists via a `_format_entity_items` helper that sorts real entities first and catch-alls last.
- `_format_topic` derives `newsNet`, `officialsNet`, `publicNet` from `by_topic_tier` via a `_split_topic_tier` + `_net_from_tier` pair.

The file stayed monolithic (one file per aggregator matches the pattern in bot.py / narrative.py / propaganda.py). DRY helpers extracted inline: `_build_sample_dict` and `_sample_dict_to_model` eliminate the ~3 places the same sample-construction code was copied.

Final sentiment.py = 872 lines, was 1027. Actual tightening — not just moving lines.

### Tests

- `analysis/tests/test_entity_registry.py` (new, 22 tests): canonicalizers, real YAML loads, primary + alias lookups, case-insensitive matches, unknown-input → None, singleton reload via `CIVIC_ENTITY_REGISTRY_DIR` override.
- `analysis/tests/test_sentiment_entity_routing.py` (new, 9 tests): seeds a temp SQLite DB with synthetic docs targeting each routing branch (matched outlet, unmatched outlet → catch-all, matched official, unmatched X author → catch-all, matched subreddit, unmatched subreddit → catch-all). Asserts `byNewsOutlet` / `byOfficial` / `byGeneralPublic` populate correctly with the right kinds + volumes, that the per-topic three-way split carries tier-specific net scores, and that real entities sort before catch-alls.
- `analysis/tests/test_rich_aggregators.py`: seed schema extended to include stub `x_posts_raw` + `x_users_raw` tables so the new LEFT JOIN resolves against empty rows (production schema always has them; this closes the test-only gap).

Total: **60 aggregator-related tests passing**. Full analysis suite's 14 prior files also still pass.

---

## Refactor beats during this pass (documented so the next one knows)

Two restructurings happened mid-Phase-3b:

1. I initially split `sentiment.py` into a `sentiment/` package (aggregator / samples / entity_routing / favorability). User flagged it as premature — sibling aggregators are single-file, the growth was self-contained. Reverted to a monolith + kept the DRY helpers inline.
2. The shared tier-classification code briefly lived in `aggregators/entity_routing.py`. User flagged the duplicate-feeling name pair. Folded into `entity_registry.py` so there's ONE "entity system" file for registries + routing primitives.

Lessons baked into `.claude/memory/feedback_trim_boilerplate.md` so future passes start from the right place:

- Match the neighborhood. Don't introduce package splits when peers are single-file.
- Extract helpers for 3+ consumers, not 2. For 2, inline the duplication.
- Delete dead 1-line wrappers. Callers can use the method directly.
- Module docstrings ≤ ~10 lines.

---

## Verification

```
PYTHONPATH=. .\analysis\.venv\Scripts\python.exe -m unittest \
    analysis.tests.test_entity_registry \
    analysis.tests.test_entity_registries \
    analysis.tests.test_sentiment_entity_routing \
    analysis.tests.test_rich_aggregators \
    analysis.tests.test_aggregation_confidence_filter \
    analysis.tests.test_bot_rework \
    analysis.tests.test_account_classifier \
    analysis.tests.test_cache
```

All pass. No live DB needed — the entity-routing tests seed their own synthetic rows.

---

## Files touched

- `analysis/src/reporting/entity_registry.py` — new (canonicalizers, dataclasses, loader, singleton, tier classification, catch-all sentinels).
- `analysis/src/reporting/models/aggregator_models.py` — added `EntitySentimentItem`, extended `PublicSentimentResult` + `TopicSentiment`, `_entity_item_to_dict()` helper.
- `analysis/src/reporting/models/__init__.py` — export `EntitySentimentItem`.
- `analysis/src/reporting/aggregators/sentiment.py` — monolith with entity-routing wiring + DRY sample helpers; net 155 lines shorter despite new functionality.
- `analysis/tests/test_entity_registry.py` — new.
- `analysis/tests/test_sentiment_entity_routing.py` — new.
- `analysis/tests/test_rich_aggregators.py` — added empty `x_posts_raw` / `x_users_raw` stubs in setUp.
- `docs/walkthroughs/README.md` — index row for 057.
- `docs/ui-redesign-plan.md` — Phase 3b pieces that landed checked off; remainder deferred to 058.

---

## Follow-ups / what's deferred to walkthrough 058

- **Narrative aggregator** extension: per-narrative first-seen tier (outlet / official / public) with the entity profile attached + `crossTier` flag when a claim surfaces in >1 tier.
- **Propaganda aggregator** extension: per-entity propaganda rates (mean score + flagged-rate per outlet / per official / for general public); top-flagged entities for the Propaganda page's Phase 7 redesign.
- **Route updates** (`api/routers/data.py`): no changes in this pass since the existing route already serializes via `to_dict()` — new fields flow through automatically. Revisit in 058 if narrative or propaganda payloads need additional endpoints.
- **UI types** (`ui/src/types.ts`): mirror the new `EntityProfile`, `EntitySentimentItem`, and `TopicSentiment` fields. Add realistic `byNewsOutlet` / `byOfficial` / `byGeneralPublic` entries to `ui/src/services/fixtures.ts` so the UI redesign phases (5–8) can be built in dev mode against representative data.
- **Snapshot cache TTL / regeneration**: `job_runner.save_snapshots()` should invalidate / rebuild existing snapshots after this lands so old cached payloads (without the new fields) don't stick around — add a cache-version bump in 058 when the UI starts consuming the new fields.
- **Coverage logging**: Phase 3b pre-check recommendation to log registry-match rates at snapshot-build time not yet implemented — adding it here was declined as scope creep; 058 should include it alongside the narrative/propaganda wiring.

Phase 3b is partially landed. The sentiment pass is the foundation; the next pass is mechanical once the pattern is in place.
