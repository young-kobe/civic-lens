# 2026-04-22 — Consistent empty-state: always render the page frame

All four data pages (Tone, Narratives, Propaganda, Bot Detector) now behave identically on empty data: they render the GlobalTicker + reads-as-today banner + three-way grid with per-column empty copy, and fall back to a full-page `<EmptyState>` only when the underlying fetch yielded nothing (`!data`).

## What shipped

Three pages had early-returns that collapsed the entire frame when aggregate counts were zero. Aligned with the pattern Tone already used:

- `ui/src/pages/Propaganda.tsx` — was `if (!data || data.total_eligible_docs === 0) return <EmptyState …>`. Now `if (!data) return <EmptyState title="No propaganda data available" />`.
- `ui/src/pages/Narratives.tsx` — was `if (!data || data.length === 0) return <EmptyState …>`. Now `if (!data) return <EmptyState title="No narratives data available" />`.
- `ui/src/pages/BotActivityProfiler.tsx` — was `if (!data || data.overview.totalFlaggedAccounts === 0) return <EmptyState …>`. Now `if (!data) return <EmptyState title="No bot-activity data available" />`.

The downstream three-way grid on each page already renders per-column empty copy (via the shared `ThreeWayColumn` primitive from `../2026-04-22-three-way-grid-primitive.md`), so the frame-with-empty-columns state renders correctly without any further changes.

## Why

User feedback: *"for these just be consistent across the app. go with whichever implements less boilerplate and cosmetic code and apply it uniformly."*

The Tone pattern (early-return only on `!data`) is the lighter one. Keeping the page frame visible also fixes an operational ambiguity: a stalled propaganda-detection cron produces zero flagged docs, but so does a healthy run on a calm news day. Before this change both states rendered identically as "No propaganda-scored posts yet" — misleading. With the frame always present, the reader sees the GlobalTicker's refresh timestamp and can tell a stale snapshot from a genuinely empty one.

## Follow-ups

None for this change. The three-way grid primitive's per-column empty copy already handles the "one tier has data, another doesn't" case honestly.
