# UI/API fix sweep — officials routing, public feed, mobile tone rows, propaganda constellation

Plan approved 2026-07-30. Audit-trail entries (the permanent record):
`api/2026-07-30-canonical-officials-predicate.md`, `api/2026-07-30-public-posts-feed.md`,
`ui/2026-07-30-public-feed-column-and-topic-default.md`, `ui/2026-07-30-tone-row-mobile-reflow.md`,
`ui/2026-07-30-propaganda-density-constellation.md`.

## A — canonical officials predicate (API)

- [x] `is_official_kind` + `OFFICIAL_AUTHOR_TIERS`; editorial dropped from all routing (routing, panel, received, sql, bots, propaganda, narratives, profiles)
- [x] Tests flipped/added; `sentiment_panel_basic` / `entity_profile_basic` / `movers_basic` snapshots re-recorded and diffs reviewed
- [x] Full suite green against live Postgres (902/902)

## B — public post feed + topic default (API + UI)

- [x] `GET /public-posts` (SQL LIMIT/OFFSET, canonical officials exclusion, lateral dominant-topic filter, engagement ordering)
- [x] `PublicPostFeed.tsx` replaces the public column's rollup cards on Public Sentiment only; footer + payload fields kept
- [x] Topic filter defaults to All Topics; `pickDefaultTopic`/`pickedDefault` removed
- [x] PG-gated + contract tests (`public_posts_basic.json` recorded)

## C — mobile tone/provenance rows (CSS)

- [x] Two-row reflow for `.tone-bar-row` (640px) and `.provenance-group-row` (1024px); `.grid-2`/`.grid-3` tracks `minmax(0, 1fr)`

## D — propaganda density constellation (UI)

- [x] `DensityConstellation.tsx` (deterministic beeswarm) + `TechniqueExplorer.tsx` chips/evidence rewrite; old bar-list CSS removed
- [x] `ByPartySection` demoted to a prose readout naming the real denominator (tracked officials' own posts); party-bar CSS removed
- [x] Highest-flagged leaderboard bumped to top 5
- [x] `npm run typecheck` + `npm run build` clean

## Remaining (Kobe)

- [ ] Visual pass on a live dev DB (`./run.sh dev`): feed pagination + topic switching; party panel and public footer at 375px; constellation hover/chips/`?technique=` round-trip; promoted officials in officials columns on all four pages
- [ ] Stage/commit per the branch split (Kobe owns git); delete this file when done
