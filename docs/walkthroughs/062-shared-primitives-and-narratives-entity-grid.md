# Walkthrough 062 — Shared primitives + entity-grid Narratives

Follow-on cleanup to walkthroughs 060 and 061. Two threads:

1. **Shared UI primitives** — move reusable pieces out of `pages/` so every page shares the same shape library (`components/common/`).
2. **Narratives entity grid** — restructure Political Narratives to mirror Overall Tone: one `EntityProfileCard` per first-seen entity, grouping that entity's narratives behind a single click-through modal. The narrative-level card grid from walkthrough 061 becomes the modal body, not the page body.

Both were requested explicitly: the component-locality rule is a durable style choice ("all reusable components should be sourced in components/…allow them to be extensible to fit whatever page or content we need inside") and the Narratives restructure was the last unfinished item from the UI redesign plan's Phase 6.

---

## What changed

### `ui/src/components/common/` — three new primitives + one move

- **`ClassificationSampleCard.tsx`** moved from `pages/publicSentiment/` → `components/common/`. Three consumers updated (`PublicSentiment`, `TopicDivergencePanel`, `SentimentDistributionCard`) to import from the barrel instead of the page folder. No behavioral change.
- **`CollapsibleInfo.tsx`** (26 lines) — thin `<details className="how-this-works">` wrapper with a default `"How this page works"` summary. Replaces four inline copies across `PublicSentiment` (how-this-works + polling-vs-online), `Propaganda`, and `Narratives`.
- **`TopMetricsBlock.tsx`** (103 lines) — Bloomberg-style dense header shell + a `TierRow` primitive that renders the `label / axis-with-dot(s) / value / verb` layout. Both Overall Tone's `TopMetrics` and Propaganda's `PropagandaTopMetrics` route through it now:
  - `TopMetricsBlock({ eyebrow?, meta?, children, aux? })` — enforces the `.top-metrics` / `.top-metrics-rows` / `.top-metrics-aux` structure, leaves rows free-form.
  - `TierRow({ label, value, verb?, dotPct?/dotColor? or dots?, valueColor?, showZeroTick? })` — generic axis row supporting a single dot (Overall Tone) or multiple dots (Propaganda's News-vs-Social split) on the same axis.

Barrel (`components/common/index.ts`) re-exports all three + the `TierRowDot` type.

### `ui/src/pages/PublicSentiment.tsx`

- `TopMetrics` rewritten to render `TopMetricsBlock` + three `ToneTierRow` wrappers (thin wrapper mapping `TierAggregate` → `TierRow` props). Internal `TierRow` renamed to `ToneTierRow` to avoid colliding with the shared export.
- `HowThisWorks` + `PollingComparison` now render `<CollapsibleInfo>…</CollapsibleInfo>` instead of hand-rolled `<details>`.
- Import line consolidated — `ClassificationSampleCard` + new primitives all come from `components/common`.

### `ui/src/pages/Propaganda.tsx`

- `PropagandaTopMetrics` rewritten the same way: three `TierRow`s under one `TopMetricsBlock`. The `dots` prop handles the News-vs-Social row's two-dot axis.
- `HowThisWorks` → `CollapsibleInfo`.

### `ui/src/pages/Narratives.tsx` (entity-grid restructure)

This is the substantive change. Old layout: three columns of `NarrativeCard`s keyed by narrative. New layout: three columns of `EntityProfileCard`s keyed by first-seen entity, with the narratives themselves living inside a per-entity modal.

- **New `NarrativeEntityGroup` type** + `groupNarrativesByEntity(narratives)` — groups by `${kind}:${key}` so `catch_all` across tiers stays disjoint. Per-group computes `count`, `totalDocs`, `avgNetSentiment` (doc-weighted), `crossTierCount`, `mostRecent`. Narratives inside a group sort by `supporting_doc_count` desc; groups sort by `count` desc then `totalDocs` desc.
- **New `entityStatsForNarratives(group)` → `EntityStat[]`** — feeds the card: "Stories" (emphasis), "Avg tone" (sentiment-colored), "Supporting docs", plus conditional "Crossing groups" when ≥1 narrative in the group crosses tiers. Same shape/rules as `sentimentStats()` on the Tone page.
- **`ThreeWayColumn` rewritten** — renders `EntityProfileCard`s via `onClick={() => onOpen(group)}`. `TOP_N = 12` cap matches Overall Tone. Column headers switched from "First said by the news / …officials / …public" to the Overall Tone wording ("The News / Politicians & Officials / The Public") with bylines updated to reflect the "first surfaced" framing. `readsAs` per card: "N stories first surfaced here."
- **New `NarrativeEntityModal`** — click an entity card → modal showing `EntityHeader` (big avatar + full blurb) + 3–4 stat tiles (Stories / Avg tone / Supporting docs / Crossing groups) + external visit link (when available via `entityExternalUrl`) + the group's narratives rendered as existing `NarrativeCard`s. Clicking a `NarrativeCard` inside the entity modal sets `activeNarrative` which short-circuits the entity modal and shows `NarrativeDetailModal` in its place (`{activeEntity && !activeNarrative && …}` guard). ESC / close on the detail modal drops back to the entity modal naturally because `activeEntity` is preserved.
- **State split**: the page now owns two independent drill-down slots — `activeEntity: NarrativeEntityGroup | null` and `activeNarrative: NarrativeSummary | null`. Narrative-clicks from the `ClaimsSpreadingPanel` still go straight to the detail modal (skipping the entity modal) by setting `activeNarrative` directly.
- **Kept intact**: `NarrativeCard`, `NarrativeDetailModal`, `ClaimsSpreadingPanel`, `SourceBar`, all the formatting helpers, `buildNarrativeTickerItems`, `readsAsToday`. The narrative-level UI didn't change; only the top-level grid grouping did.

Final file size: 768 lines (was 609 in walkthrough 061 — the entity-grouping + entity modal net +159 because we kept both UIs; removed code is small since the narrative-level helpers all stayed).

### `ui/src/index.css`

No new CSS. The existing `.how-this-works`, `.top-metrics*`, `.tier-row*`, `.three-way-*`, `.entity-*`, and `.narrative-card*` rules already covered the new shape. Verified by the clean typecheck + build.

---

## Why these choices

**Why one `TopMetricsBlock` primitive instead of separate `ToneTopMetrics` + `PropagandaTopMetrics`?** Both already shared every CSS class — `.top-metrics`, `.top-metrics-head`, `.top-metrics-rows`, `.top-metrics-aux`, `.tier-row*`. The duplication was 3 wrapper divs and an `eyebrow` + `meta` header on each page, copy-pasted. Extracting the shell is a net 30-line reduction and means the next page that wants this header (Bot Detector in Phase 8) gets it for free. I did **not** extract a `ToneTierRow` variant — kept it inline in `PublicSentiment` because it wraps `TierRow` with tone-specific color/axis logic that's page-specific. `TierRow` itself stays dumb and general.

**Why keep `ToneTierRow` inline in `PublicSentiment.tsx` (and not also export it)?** It bridges `TierAggregate` (Tone-specific) to the generic `TierRow` props. Exporting it would invite other pages to import a Tone-shaped struct to render generic rows, which inverts the dependency. Pages own their data → primitive mapping; the primitive is the shared piece.

**Why dots-array support on `TierRow` instead of two separate rows for the News-vs-Social split?** The Propaganda "News vs social" row visually overlays two dots on the same axis to show the gap — that's the whole point of the row. Splitting into two rows would lose the visual anchoring.

**Why group narratives by `first_seen_entity_profile` (kind+key) instead of by `first_seen_domain` or `first_seen_author.handle`?** Because `EntityProfileCard` is typed on `EntityProfile`. Any grouping key that didn't resolve to a profile would need to either synthesize a card (which means re-inventing the card contract) or fall back to a non-card row — either way a second UI shape. Walkthrough 058 already made `first_seen_entity_profile` optional on `NarrativeSummary` and walkthrough 058's snapshot backfill populates it, so keying off the profile means every group renders the same card the Tone page renders. Single contract.

**Why a nested modal (entity modal → narrative modal) instead of replacing the entity modal outright or stacking two portals?** Stacking two `<Modal>`s at once would double-mount the backdrop + lock scroll twice. Replacing outright would lose the entity context on detail-modal close (user has to re-click the entity card to see the other narratives in that group). The `{activeEntity && !activeNarrative}` guard gives us clean layering: only one portal ever mounts, closing the detail modal restores the entity modal, closing the entity modal clears both.

**Why not also extract a generic `EntityDetailModal` shell (the `EntityHeader` + stats + external link pattern is now in 3 pages)?** Considered. Each page's modal diverges after the stats row: Tone shows classification samples, Propaganda shows flagged examples (future), Narratives now shows the entity's narratives. A shared shell would either be a `children`-slot wrapper (at which point it's four lines of `<Modal>` + `<EntityHeader>` + stats — too thin to extract) or a config-object boilerplate. Left inline for now; revisit if a 4th consumer lands with the same shape.

**Why `NarrativeEntityGroup` as its own type instead of reusing `EntitySentimentItem`?** `EntitySentimentItem` has tone-specific fields (`positive/negative/neutral/netScore/classificationSamples`). Reusing it would force null fields on Narratives or invent a sum-type. The two pages measure different things on the same entity profile — the grouping struct is page-specific, the card component is shared. That's the right split.

**Why bump the column headers to match Overall Tone ("The News / Politicians & Officials / The Public")?** The previous headers ("First said by the news / officials / public") front-loaded the distinguishing verb, which was good when we were comparing across *both* pages. Now that Overall Tone and Political Narratives live side-by-side in the tab bar and use the same entity cards with the same three columns, matching headers + a narrative-flavored byline ("Outlets that first surfaced each story") communicates the split at a glance. The "first surfaced" framing moves into the byline + card `readsAs`, where it describes what the column means in this context.

---

## Verification

```
cd ui
npm run typecheck   # clean
npm run build       # clean, 3.7s
```

Mock fixtures (`services/fixtures.ts`) already populate `first_seen_entity_profile` + `first_seen_tier_group` on the nine seed narratives, so dev mode renders the new entity grid immediately — NYT / Fox / BBC in News; POTUS / Schumer / Johnson in Officials; r/politics / r/Conservative in Public — each clickable into the entity modal, each narrative inside clickable into the detail modal.

---

## Files touched

- `ui/src/components/common/ClassificationSampleCard.tsx` — moved from `pages/publicSentiment/` (contents unchanged).
- `ui/src/components/common/CollapsibleInfo.tsx` — new (26 lines).
- `ui/src/components/common/TopMetricsBlock.tsx` — new (103 lines — `TopMetricsBlock` + `TierRow` + `TierRowDot` type).
- `ui/src/components/common/index.ts` — barrel updated (+ 4 exports).
- `ui/src/pages/PublicSentiment.tsx` — `TopMetrics` / `HowThisWorks` / `PollingComparison` refactored onto the shared primitives; inline `TierRow` renamed `ToneTierRow`.
- `ui/src/pages/Propaganda.tsx` — `PropagandaTopMetrics` / `HowThisWorks` refactored onto the shared primitives.
- `ui/src/pages/Narratives.tsx` — entity-grouped three-way grid + `NarrativeEntityGroup` + `NarrativeEntityModal`; split `activeNarrative` / `activeEntity` state.
- `ui/src/pages/publicSentiment/TopicDivergencePanel.tsx` — import updated to use the barrel.
- `ui/src/pages/publicSentiment/SentimentDistributionCard.tsx` — import updated to use the barrel.
- `docs/walkthroughs/README.md` — index row for 062.
- `docs/ui-redesign-plan.md` — Phase 6 follow-ups marked done.

---

## Follow-ups carried forward

- **`EntityDetailModal` shell**: if Bot Detector lands with the same `EntityHeader` + stats + external-link shape, extract the modal wrapper. Don't pre-extract on 3 consumers alone.
- **Phase 8 (Bot Detector redesign)**: first natural customer of `TopMetricsBlock` outside Tone/Propaganda — a good stress-test for whether the primitive is extensible enough.
- **Snapshot cache version bump**: still open from walkthrough 058 — until old cached snapshots expire, some narratives can land without `first_seen_entity_profile` set and get filtered out of the new grid. Currently acceptable (daily cron overwrites the cache), but worth a `snapshot_version` bump next time aggregator output shape changes.
- **Per-entity trend sparklines** inside `NarrativeEntityModal`: nice-to-have if we ever emit per-entity per-day narrative counts from `NarrativeAggregator`. Not cheap, not urgent.
