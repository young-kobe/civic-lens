# Backend aggregator audit: redundancy + overlapping concerns

The UI redesign consolidated shared primitives on the frontend; the backend aggregator layer did not get the same pass. Each of `sentiment.py` / `propaganda.py` / `narrative.py` / `bot.py` / `movers.py` re-implements the same window + registry + rollup pattern.

**MVP rule:** no endpoint or job should redundantly recompute the same aggregate. Compute once per window per pipeline run, cache in `SnapshotCache` (or a `WindowContext` in-memory), read from the cached slice.

## Concrete hotspots

### 1. X-author join duplicated across 4+ aggregators — **DONE 2026-04-23**

Ten call sites across `sentiment.py` / `propaganda.py` / `narrative.py` / `bot.py` / `movers.py` / `review.py` now route through `base.X_AUTHOR_JOIN_SQL` — a single constant containing the two LEFT JOINs. No helper function or SQL view was needed: a string constant is the simplest thing that drifts nowhere. A future move to a materialized `docs_with_x_author` view would be a one-constant change.

- [x] Single canonical SQL fragment in `base.X_AUTHOR_JOIN_SQL` (2026-04-23).
- [x] All ten aggregator + review sites migrated.

### 2. `_first_seen_info` + `_registry_lookup` overlap

Both resolve a doc to a registry entity; one pulls `account_profiles`, the other calls `resolve_entity(get_registry(), …)`.

- [ ] Introduce `resolve_doc_to_entity(cursor, doc_id) -> (tier_group, entity_profile_dict, author_dict)` in `entity_registry.py`.
- [ ] Use it from narrative.py, sentiment.py's `_accumulate_tier`, propaganda.py's `_accumulate_entity`, bot.py's `_fetch_entity_rollups`.

### 3. `aggregation_min_confidence` read per method per aggregator

`get_settings().aggregation_min_confidence` called on every `_net_sentiment` / sentiment-join call. Settings are cached so no perf issue, but a single source keeps "what counts as confident" honest.

- [ ] Lift to module-level constant at import time OR a `sentiment_filter_clause()` helper returning `(sql_fragment, params_tuple)`.

### 4. Time-window cutoff → SQL WHERE clause repeated 15+ times

```python
params: List[Any] = [narrative_id]
if cutoff is not None:
    sql += " AND d.published_at >= ?"
    params.append(cutoff)
```

- [ ] `base.with_cutoff(sql, params, cutoff) -> (sql, params)` helper. Verbose-and-error-prone → tiny call site.

### 5. `_accumulate_tier` (sentiment) and `_accumulate_entity` (propaganda) are structurally identical

Both walk rows, route each via `resolve_entity` into a tier bucket, accumulate counts, sort + finalize. `bot.py::_fetch_entity_rollups` is a third copy.

- [ ] `analysis/src/reporting/aggregators/entity_rollup.py` — generic `EntityRollup[ItemT]` with `add_row(row, entity) / finalize() -> List[ItemT]`.
- [ ] Migrate sentiment, propaganda, bot.

### 6. `net_sentiment` math re-implemented per consumer

Same `total += conf if POSITIVE else -=conf if NEGATIVE / count * 100` formula in two files.

- [ ] `compute_net_sentiment(rows) -> float` in `base.py` (or `sentiment_math.py`).
- [ ] Call from sentiment.py's per-entity formula and narrative.py's `_net_sentiment`.

### 7. Source-label + URL formatting forked into helpers twice

- `sentiment.py:_build_sample_dict` builds `url` + `source_name` for classification samples.
- `narrative.py:_build_source_label` + `_build_doc_url` build "News · nytimes.com" for the supporting-docs table.

Both derive from `(source_type, domain, ident, x_handle)`. Two different output shapes because they were written for different surfaces.

- [ ] `format_doc_source(source_type, domain, ident, x_handle) -> {label, url}` in `base.py`.
- [ ] Converge both consumers; aggregators emit one shape, UI maps presentation.

### 8. `SnapshotCache.save_snapshots()` walks docs 4+ times per pipeline run

Each aggregator opens its own cursor and re-walks `docs` + joins `ai_outputs` + joins x tables. One full crawl cycle = ~4× walks of `docs`, ~4× of `ai_outputs`.

- [ ] Introduce a `WindowContext` built once per pipeline run:

```python
@dataclass
class WindowContext:
    cutoff: Optional[int]
    docs: List[Doc]
    ai_outputs_by_doc: Dict[int, Dict]
    x_authors_by_doc: Dict[int, Tuple]
    registry_match_by_doc: Dict[int, Tuple]
```

- [ ] Each aggregator accepts `WindowContext` and derives its view without re-querying. Biggest design win; biggest blast radius — do after #1–#7 land.

### 9. `SnapshotCache` key overlap between aggregators

Some metrics may be computed twice because two aggregators emit them for different consumers (per-outlet propaganda in `propaganda.by_news_outlet` + some sentiment-side metadata).

- [ ] Dump live cache JSON, diff shapes, identify duplicates, delete the second emitter.

### 10. Confidence-threshold filter inconsistency

`ORDER BY COALESCE(a.confidence, 0) DESC` (walkthrough 063) vs. `a.confidence >= ?` (narrative net_sentiment) — two philosophies on nulls in the same table.

- [ ] Pick one rule, apply uniformly, document in `SCORING_METHODOLOGY.md`.

## Suggested refactor order

1. Helpers pass (#1, #4, #6, #7) — low risk, big readability.
2. `resolve_doc_to_entity` (#2) — narrative.py already has two overlapping methods; unify first.
3. `EntityRollup[ItemT]` generic (#5).
4. `WindowContext` build-once pipeline (#8) — most impactful; do after 1–3.
5. Snapshot key audit (#9) + confidence rule (#10).

Each step is its own audit-trail entry under `docs/audit-trail/analysis/`. First step landed as `2026-04-23-x-author-join-helper.md`.

## MVP constraints — do NOT

- Backwards-compat shims. Breaking an older snapshot shape is fine — the cron rebuilds it.
- Per-tier subclasses (`NewsEntityRollup` / `OfficialEntityRollup`). The rollup generic is one class parameterized.
- Introduce an ORM. Straight SQL + helpers is working.

## Related

- See `dead-code-cleanup.md` for the broader code audit.
- `todo-bot-propaganda-entity-signals.md` in the repo root — feeds registries into detectors (separate initiative).
