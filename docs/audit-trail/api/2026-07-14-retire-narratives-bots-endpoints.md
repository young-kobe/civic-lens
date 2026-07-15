# 2026-07-14 — Retire /narratives and /bot-activity endpoints

The API no longer serves narrative or bot-activity data. `analysis/src/api/routers/data.py` now exposes sentiment, propaganda, entity-posts, outlet-profiles, movers, eval-accuracy, and snapshot-status; the review router still accepts `bot_detection` as a review task type (per-post bot detection keeps running internally).

## What shipped

- `routers/data.py`: `GET /narratives` (including its live-recompute path for limits above the cached top-100) and `GET /bot-activity` removed, along with their `NarrativeAggregator`/`BotAggregator` instantiations.
- `analysis/tests/test_api.py`: endpoint expectations updated to the new surface.

## Why

- The snapshot keys behind these endpoints stopped being written (see the analysis-layer entry) once the tabs were retired; a route serving a never-refreshed cache would violate the freshness contract surfaced by `/snapshot-status`.

## Follow-ups

- `GET /disinfo` and `GET /accountability` replace them in `docs/todos/accountability-disinfo.md` Phases 1 and 3.
- Cross-links: `docs/audit-trail/ui/2026-07-14-retire-narratives-bots-tabs.md`, `docs/audit-trail/analysis/2026-07-14-narratives-legacy-default-off.md`.
