# 2026-04-22 — Real refresh timestamp in header + per-page tickers

Every UI surface that showed "last refreshed" now reflects the actual pipeline run time from the snapshot cache, not render time. A stopped cron is now visible at a glance.

## What shipped

- `ui/src/services/api.ts` — `fetchSnapshotStatus()` calls the new public `/snapshot-status` endpoint. `SnapshotStatusEntry` + `SnapshotStatus` types exported.
- `ui/src/services/fixtures.ts` — `mockSnapshotStatus()` returns all current keys with a timestamp 18 minutes in the past so the dev-mock branch renders realistic "Refreshed 18 min ago" labels.
- `ui/src/services/freshness.ts` (new) — three small helpers:
  - `formatRefreshedAgo(iso)` → `"moments ago"` / `"18 min ago"` / `"3h ago"` / `"2d ago"` / absolute ISO once older than a week.
  - `getSnapshotTimestamp(status, key)` — per-page lookup.
  - `latestSnapshotTimestamp(status)` — max across all keys, used for the site-wide header.
- `ui/src/App.tsx` — header strip now reads "Refreshed 18 min ago" using `latestSnapshotTimestamp`; absolute ISO shown in the title/aria-label so keyboard + screen-reader users can still get the exact time. Footer gets the same string via the existing `timestamp` prop.
- `ui/src/pages/PublicSentiment.tsx` + `Narratives.tsx` + `Propaganda.tsx` + `BotActivityProfiler.tsx` — each page fetches snapshot-status via `useFetch(..., 'snapshot-status')` and resolves its relevant key (`sentiment_{window}`, `narratives_{window}`, `propaganda_{window}`, `bot_activity`). The `useFetch` module-level cache dedupes across pages so tab switching doesn't refire the request.

The old render-time `new Date().toISOString().slice(0, 19)` construction is gone from all five call sites.

## Why

Before: every page rendered `new Date()` in the GlobalTicker's `refreshed` slot, so the header always read as fresh regardless of what the pipeline had actually done. A lagging cron looked identical to a healthy one.

After: the number reflects the underlying `SnapshotCache.generated_at`. If you load the page at 10am and the most recent snapshot was written at 4am, the header says "Refreshed 6h ago" — the reader immediately sees the staleness.

## UX notes

- Header shows the site-wide latest (max across all snapshots). This is the "pipeline heartbeat" — if the most recent snapshot is 6h old, the whole system is 6h behind, not just one aggregator.
- Per-page GlobalTicker shows that page's key specifically — so if `bot_activity` is caught up but `sentiment_7d` is behind, the user sees the correct freshness on the tab they're reading.
- Fallback `"—"` when the snapshot status hasn't loaded yet (first paint) or an individual key is missing. Avoids a flash of "0 min ago" during initial load.
- Older-than-a-week snapshots fall back to absolute ISO rather than "9d ago" / "14d ago" — at that point we're no longer in a "refreshing regularly" regime and the honest calendar date is more useful than a relative string.

## Follow-ups

None for this change. The snapshot-status endpoint + the freshness helpers are ready to power any future UI consumer (e.g. an admin-only "pipeline health" widget).
