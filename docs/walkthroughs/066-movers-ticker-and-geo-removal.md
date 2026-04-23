# Walkthrough 066 — Movers ticker (Phase 12) + geo-sentiment removal + Bot entity rollups

Closes out the remaining bullets of the UI Redesign Plan: Phase 12's dynamic movers ticker, the deferred "Amplification by tier" entity rollups for Phase 8's Bot Detector, and an incidental decommission of the unused geo-sentiment stack.

---

## Phase 12 — Movers ticker

Goal: surface the biggest window-over-window shifts in political tone and GOP favorability as a scrolling ribbon under the GlobalTicker on the two pages the user tracks actively (Overall Tone + Political Narratives).

### Backend — `analysis/src/reporting/aggregators/movers.py` (new)

`MoversAggregator.get_movers(time_window)` returns a `MoversResult` with:

- `window` — active time window label.
- `entity_movers` — list of `EntityToneMover` rows (outlet / official / subreddit). Sorted by `|delta_pts|` desc and capped at 10. Catch-alls excluded. Per-entity volume floor `_MIN_VOLUME = 10` to keep noise out.
- `favorability_mover` — single `FavorabilityMover` row for overall GOP net favorability. `None` when either window had no volume.

Implementation: two SQL passes per call — one for the current window `[now - seconds, now)`, one for the equivalent preceding window `[now - 2·seconds, now - seconds)`. `base.get_previous_window_range(window)` is the shared helper. Delta is `current_net - prev_net` (percentage-point shift). Live-computed, not snapshot-cached — the diff is cheap enough to run per request at the rate limit (20/min).

### API — `/movers?window=…`

Added to `analysis/src/api/routers/data.py` at 20 requests/min. Returns `MoversResult.to_dict()`.

### UI — `ui/src/components/common/MoversTicker.tsx` (new)

Horizontal marquee. CSS animation (`@keyframes movers-scroll`) translates a duplicated row list `-50%` over 60s for a seamless loop. Animation pauses on hover. `(prefers-reduced-motion: reduce)` disables the loop entirely and turns the viewport into a regular horizontally-scrollable element. Accessible label: "Biggest movers in political tone and GOP favorability". Matches the user's framing.

Each entity row shows `{displayName} {▲|▼|▪} {+/-X.X pts}`. The GOP favorability row leads the list and is visually distinct (`.movers-item-fav`). Entity rows are clickable-capable via an `onEntityClick` prop — wiring into the entity drill-down modals is deferred (the modals currently key off the per-page payload, not the movers payload; a follow-up can merge the two).

### Wiring

`PublicSentiment.tsx` + `Narratives.tsx` both call `fetchMovers(filters.timeRange)` via `useFetch` and render `<MoversTicker data={movers} />` in a `col-span-12` slot directly beneath their GlobalTicker. When movers is still loading or empty, the slot renders nothing rather than a spinner — the ticker is optional context, not load-blocking.

---

## Phase 8 completion — Bot entity rollups

The Phase 8 plan had `"Amplification by tier"` marked deferred pending backend support. Filled in:

- `analysis/src/reporting/aggregators/bot.py`: `_fetch_entity_rollups(cursor)` LEFT JOINs `x_users` for handle resolution, runs every social doc through `resolve_entity()`, and accumulates per-entity `(total_docs, bot_docs)` into outlet / official / subreddit buckets (plus a catch-all per tier). Sort: catch-all last, then `bot_rate_pct` desc.
- `analysis/src/reporting/models/aggregator_models.py`: `BotEntityItem` added; `BotOverview` gains `by_news_outlet` / `by_official` / `by_general_public` (all default-empty for backwards compat with pre-existing cached snapshots).
- UI: `BotThreeWayGrid` + `BotEntityCard` in `ui/src/pages/BotActivityProfiler.tsx` render the three columns with per-entity `bot_rate_pct` stats. Thresholds: red >10%, amber >3%, neutral otherwise.

---

## Geo-sentiment decommission

`geo_sentiment` was a walkthrough-020 holdover — a country-level X-sentiment heatmap page. It has been hidden from nav since the Phase 1 work, was never wired into the redesign, and the user confirmed removal. We are in MVP; dead code is worse than no code.

### Removed

**Backend:**
- `analysis/src/reporting/aggregators/geo.py` — deleted.
- `/geo-sentiment` endpoint + `GeoAggregator` singleton in `analysis/src/api/routers/data.py`.
- `GeoAggregator` export from `analysis/src/reporting/aggregators/__init__.py`.
- `save_snapshots` geo block in `analysis/src/scheduler/job_runner.py`.
- `TestGeoSentimentConfidenceFilter` in `analysis/tests/test_aggregation_confidence_filter.py`.
- `/api/v1/geo-sentiment` entry in `analysis/tests/test_api.py`.
- `COUNTRY_NAMES` dict in `analysis/src/reporting/aggregators/constants.py` (only consumer was the deleted aggregator).
- Stale `geo_sentiment_*.json` snapshot cache files.

**UI:**
- `ui/src/pages/GlobalHeatmap.tsx` — deleted.
- `GlobalHeatmap` export from `ui/src/pages/index.ts`, import + `case 'heatmap'` branch in `ui/src/App.tsx`, `'heatmap'` from `TAB_IDS`.
- `geography` field from `Filters` type in `ui/src/types.ts`; `showGeography` control + geography-related clear-filter logic in `ui/src/components/common/GlobalFilters.tsx`.
- `fetchGeoSentiment`, `GeoSentimentData`, `CountryStats` from `ui/src/services/api.ts`.
- `COLORS.heatmapCountry*` + `COLORS.mildPositive` + `geoSentimentColor()` from `ui/src/theme.ts`.
- `--heatmap-country-*` + `--heatmap-mild-positive` tokens + `.global-heatmap*` blocks from `ui/src/index.css`.

### Rationale

Retained features should justify their maintenance cost. The heatmap was: (a) unlinked from nav so no user saw it, (b) dependent on X `place_country_code` which only a small fraction of posts carry, (c) orthogonal to the three-way editorial frame the rest of the dashboard now leads with. Removing it shrinks the API surface, the cache-save loop, and the UI theme dictionary. No user-facing regression — nothing linked to it.

---

## Files touched

**Added:**
- `analysis/src/reporting/aggregators/movers.py`
- `ui/src/components/common/MoversTicker.tsx`
- `docs/walkthroughs/066-movers-ticker-and-geo-removal.md` (this file)

**Modified:**
- `analysis/src/api/routers/data.py`
- `analysis/src/api/server.py`
- `analysis/src/reporting/aggregators/__init__.py`
- `analysis/src/reporting/aggregators/base.py`
- `analysis/src/reporting/aggregators/bot.py`
- `analysis/src/reporting/aggregators/constants.py`
- `analysis/src/reporting/models/aggregator_models.py`
- `analysis/src/scheduler/job_runner.py`
- `analysis/tests/test_aggregation_confidence_filter.py`
- `analysis/tests/test_api.py`
- `ui/src/App.tsx`
- `ui/src/components/common/GlobalFilters.tsx`
- `ui/src/components/common/index.ts`
- `ui/src/index.css`
- `ui/src/pages/BotActivityProfiler.tsx`
- `ui/src/pages/Narratives.tsx`
- `ui/src/pages/PublicSentiment.tsx`
- `ui/src/pages/index.ts`
- `ui/src/services/api.ts`
- `ui/src/services/fixtures.ts`
- `ui/src/theme.ts`
- `ui/src/types.ts`
- `docs/ui-redesign-plan.md`
- `docs/walkthroughs/README.md`

**Deleted:**
- `analysis/src/reporting/aggregators/geo.py`
- `ui/src/pages/GlobalHeatmap.tsx`
- Stale `data/cache/geo_sentiment_*.json`
