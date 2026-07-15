# 2026-07-14 — Retire the Political Narratives and Bot Detector tabs

The UI no longer surfaces narrative clustering or bot detection. The tab bar now runs home, sentiment, propaganda, desk, review (plus admin gating as before), with the header subtitle reading "Accountability & Disinformation Tracker" — the two replacement features (Promise Tracker, Disinfo Watch) land in subsequent phases of `docs/todos/accountability-disinfo.md`.

## What shipped

- `ui/src/App.tsx`: `narratives` and `bots` removed from `BASE_TABS`; subtitle updated.
- Deleted: `pages/Narratives.tsx`, `pages/narratives/`, `pages/BotActivityProfiler.tsx`, `pages/bots/`.
- `services/api.ts` / `types.ts` / `services/fixtures.ts`: `fetchNarratives`, `fetchBotActivity`, and their types/fixtures removed.
- `pages/Home.tsx`: narrative/bot promo cards removed; `pages/home/DigestSection.tsx` no longer fetches narrative or bot data.
- `pages/DataDesk.tsx`: `botRate` and `stories` matrix columns removed.
- `components/common/EntityHubLinks.tsx`: Bot Detector cross-link removed.

## Why

- At current ingestion volume the narrative clusters and bot scores were low-signal and read as noise next to the confidence-labeled surfaces; the replacement features (curated promise tracking, fact-checker-anchored disinformation) make claims the data can support.

## Follow-ups

- Promise Tracker and Disinfo Watch tabs: `docs/todos/accountability-disinfo.md` Phases 1-4.
- Cross-links: `docs/audit-trail/api/2026-07-14-retire-narratives-bots-endpoints.md`, `docs/audit-trail/analysis/2026-07-14-narratives-legacy-default-off.md`.
