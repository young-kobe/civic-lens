# 2026-07-25 — Entity stance UI drops the redundant "Favorability net" metric

Following the API layer's retirement of `analysis.favorability_stances` (`docs/audit-trail/api/2026-07-25-favorability-retirement.md`), `EntityStanceAggregate.favorability` no longer exists on the `GET /api/v1/sentiment` response — the UI's separate "Favorability net" display, which measured the same "stance toward this entity" axis as the "mentions"/`targetStance` display right next to it (just from a Republican-only sample instead of the party-neutral one), is removed rather than left pointing at a field that no longer exists.

## What shipped

- **`ui/src/types.ts`**: `EntityStanceAggregate` drops the `favorability: StanceCounts` field, matching the API response shape.
- **`ui/src/pages/PublicSentiment.tsx`**: `ENTITY_SORTERS` (posts/net-tone sort in the News/Officials/Communities three-way grid) and `EntityThreeWayGrid`'s card stats read `item.targetStance` instead of `item.favorability`. The three column bylines/empty-states reworded from "a favorability reading" to "a stance reading" (no functional change, just no longer naming a retired metric). The unresolved-mentions footnote sums `targetStance.volume` alone instead of `favorability.volume + targetStance.volume`. `EntitySentimentModal`'s stat block drops the "Favorability net" tile (whose tooltip literally named `favorability_stances`) and keeps only the remaining tone tile, relabeled "Net tone" since there is no longer a second tile to contrast it against.
- **`ui/src/pages/DataDesk.tsx`**: the cross-signal matrix's `netTone`/`posts` columns read `e.targetStance` instead of `e.favorability`; the `netTone` column's tooltip reworded from "the entity's own posts" (a description that was never accurate for this field — see the API entry: neither `favorability_stances` nor `target_mentions` filtered by authorship) to "stance toward this entity."
- **`ui/src/components/common/MoversTicker.tsx`**: unchanged. `FavorabilityMover`'s shape didn't change (only its backend data source did, per the API entry) and it measures a genuinely distinct axis from the entity-stance grid above (stance TOWARD an entity, window-over-window, vs. the current-window snapshot `EntityStanceAggregate` provides) — nothing here needed touching.
- **Verify**: `npm run typecheck` and `npm run build` both clean (only the pre-existing >500 kB chunk-size warning, unrelated to this change).

## Why

- **The two tiles measured the same thing.** `item.favorability` and `item.targetStance` in the entity-stance grid were both "stance toward this entity," just sourced from two overlapping engines (see the API entry's rationale) — showing both, ranked interchangeably, was presenting one signal twice under two names. Once the backend collapsed to one source, the UI collapsing to one tile is the honest reflection, not a feature cut.
- **A stale, misleading tooltip is worse than none.** The removed "Favorability net" tile's tooltip claimed "Tone of this entity's own posts (favorability_stances)" — a description that never matched what the query actually computed (no join ever filtered by the entity's own authorship; see the API entry). Deleting the tile removes the misleading claim along with the redundant number.

## Follow-ups

- None — this is a straight field-removal following the backend cutover, no new UI capability deferred.
