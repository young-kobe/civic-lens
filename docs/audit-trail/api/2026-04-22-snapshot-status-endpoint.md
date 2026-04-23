# 2026-04-22 — Public /snapshot-status endpoint

The snapshot cache already records `generated_at` per aggregation (written by `SnapshotCache.save()` on every pipeline run). It was exposed only via the admin `/cache-status` route; the public UI had no way to see when data was last refreshed, so every header + ticker rendered `new Date()` at render time — misleading.

## What shipped

- `analysis/src/api/routers/data.py` — new public `GET /snapshot-status` returning `{snapshots: [{key, generated_at, doc_count}, ...]}`. No rate-limit decorator — it's a single file stat per cached key, no SQL, cheaper than any other /data endpoint. The admin `/cache-status` endpoint (`routers/admin.py`) is preserved with its fuller operator view; the public variant omits the absolute `cache_dir` path that admin returns.

## Why

Without a real freshness signal the header strip ("LIVE · 2026-04-22 19:07:51 UTC") always read as current regardless of pipeline state, which would have masked a stopped cron. Showing the honest `generated_at` lets a reader catch a stalled pipeline at a glance.

## Follow-ups

- None for this change. The UI side is documented in `../ui/2026-04-22-real-refresh-timestamp.md`.
