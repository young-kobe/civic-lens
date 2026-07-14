# 2026-07-10 — Add GET /outlet-profiles

New public endpoint serving the per-domain cross-signal rollup (net tone x
bot rate) from the `outlet_profiles_{window}` snapshots written by
`job_runner.save_snapshots()`, with the standard live-aggregation fallback.
Analysis-layer entry: `../analysis/2026-07-10-sample-enrichment-tonetrend.md`.

## What shipped

- `GET /api/v1/outlet-profiles?window=24h|7d|30d|90d|all`
  (`api/routers/data.py`) → `{window, disclaimer, outlets: [{outlet,
  source_type, net_tone, bot_rate_pct, volume, total_scanned}]}` via
  `get_cached_or_fallback`, matching the sibling endpoints' pattern.
- Existing endpoints' payloads grew additively: sentiment snapshots gain
  `toneTrend` and per-sample `engagement`/`author`; bot snapshots gain
  per-example `confidence`/`indicators`/`reasoning`. All new fields are
  optional — stale cached snapshots keep parsing.

## Why

- The UI's Tone page ships an outlet cross-signal panel (Phase 2e of
  `docs/todos/ui-depth-overhaul.md`); the aggregator existed but had no
  endpoint or snapshot key.

## Follow-ups

- None. The endpoint inherits the server-wide rate limit.
