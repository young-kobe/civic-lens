# Walkthrough 061 — Political Narratives page redesign

Phase 6 of the UI Redesign Plan. The Narratives page gets the same three-way treatment Overall Tone got in walkthrough 060, plus a dedicated cross-tier panel and an amplification/propaganda overlay. The old elected / affiliated / general-public subdivision of X-origin narratives is dropped — the 3-way tier grouping from walkthrough 058 replaces it.

Tab id stays `narratives` (URL hashes + cache keys unchanged). Label becomes "Political Narratives".

---

## What changed

### `ui/src/App.tsx`

Tab label: `"Narratives" → "Political Narratives"`; `shortLabel` stays `"Narratives"` (already fits). Id unchanged.

### `ui/src/pages/Home.tsx`

TabCard body rewritten to describe the three-tier framing + cross-tier panel + amplification overlay (was "news vs social" language).

### `ui/src/pages/Narratives.tsx` (rewritten)

- Inline helpers kept and tightened: `authorLabel`, `formatRelativeDate`, `netSentimentColor`, `firstSeenLabel`, `SourceBar` + `SOURCE_DOT_COLOR`, `buildNarrativeTickerItems`.
- New `readsAsToday(narratives)`: derives a sentence naming the dominant tier and the cross-tier count (e.g. "Most claims (12 of 30) first surfaced in the general public. 4 narratives now cross ≥ 2 tiers.").
- New `NarrativeCard`: compact clickable card — claim (two-line clamp) + first-seen line + source mix bar + metrics row (docs / net / cites) + optional flag chips (propaganda / bot-pushed / cross-tier). `<button>` root, keyboard-accessible, opens the detail modal on click/Enter/Space.
- New `NarrativeDetailModal`: full drill-down — stats grid (supporting docs, net sentiment, inbound citations, propaganda score, bot-pushed%), daily-volume sparkline, source-mix bar + legend, first-seen entity profile (when attached by walkthrough 058).
- New `ThreeWayColumn`: header + byline + stack of `NarrativeCard`s; empty-state copy points at walkthrough 056 on the Officials column.
- New `CrossTierPanel`: dense list of narratives flagged `cross_tier=true`. Each row shows claim, tier chips (News / Officials / Public), and supporting-doc count. Derived via `tierChipsForNarrative(n)` from `source_breakdown` + `first_seen_tier_group`.
- New `AmplificationPanel`: only renders when ≥1 narrative has `propaganda_score >= 0.4` OR `bot_pushed_fraction >= 0.3`. Lists those narratives as `NarrativeCard`s (so users see the same flag chips inside the overlay).
- Dropped: old `NarrativeRow` + `NarrativeSection` + the 4-way elected / affiliated / general-public X tier splits + Reddit group + "orphan" group. All superseded by `first_seen_tier_group` buckets.
- Page layout: sampling disclaimer → GlobalTicker → Reads-as-today → three-way grid → Cross-tier panel → Amplification overlay (conditional).

### `ui/src/index.css`

Swap: the old `.narrative-row` / `.narratives-list` / `.narrative-row-*` / `.narrative-row-metrics` block (~90 lines) is removed. In its place:

- `.narrative-card*` — compact card styling: claim clamp, origin line, metrics row, flag chips (`.narrative-flag-prop` / `-bot` / `-cross`).
- `.narrative-source-bar` — small 6px bar for source-mix visualization inside the card.
- `.cross-tier-row` + `.cross-tier-chip-*` — cross-tier list row + tier chip color variants.
- `.amplification-list` — responsive grid of narrative cards for the overlay.
- `.narrative-modal-stats` — auto-fit metric grid in the detail modal.

Three-way grid + Reads-as-today CSS reused from walkthrough 060 (already present in index.css).

---

## Why these choices

**Why swap the old NarrativeRow + NarrativeSection instead of keeping them for the cross-tier panel?** The old 5-column row layout assumed each section sits at ~half-width (col-span-6). The new columns are ~1/3-width (col-span-4 via `.three-way-grid`), and the cross-tier panel needs a denser row shape with tier chips. Retooling the row would've pushed toward either `variant` props (speculative flexibility) or two near-duplicate row components. Replacing with a single compact card + a purpose-built cross-tier row is smaller net code.

**Why derive tier chips from `source_breakdown` + `first_seen_tier_group` instead of passing them through the API?** Those two fields already tell us which tiers a narrative touches. Adding a dedicated `tiers: string[]` response field would be boilerplate the aggregator doesn't need to carry — the client can synthesize at zero cost per narrative.

**Why not an LLM-derived "Reads as today" sentence?** Same answer as Phase 5: deterministic, cheap, debuggable. The template captures "dominant tier" + "cross-tier count" which is the shape of the sentence the plan called for.

**Why drop the "Other Narratives" catch-all section that used to render for orphaned first_seen_source_type?** After walkthrough 058, every narrative resolves to a tier_group via `resolve_entity`. Narratives with null tier_group fall under the sampling-disclaimer wording but don't merit their own section anymore — near-zero volume, and "orphaned" reads as a bug to a reader.

**Why render the Amplification panel inline instead of as a separate "Propaganda Amplification" tab?** The plan explicitly puts it on the Narratives page as an overlay — it's the same list of narratives, sliced by a different threshold. Tab-splitting would fragment the signal. If a reader wants the full propaganda breakdown, the Propaganda tab (Phase 7, next) is the right place.

**Why not show cross-tier narratives as a directed graph?** The plan phrased it "with a visual showing propagation direction". We don't have directed propagation data — `source_breakdown` + `first_seen_tier_group` tell us which tiers have the claim but not the temporal order in which tiers adopted it. A directed arrow would be fabricated signal. Tier chips are honest. If we later emit per-tier first-seen timestamps, revisit.

---

## Verification

```
cd ui
npm run typecheck   # clean
npm run build       # clean, 4.2s
```

Dev-mode mock fixtures (walkthrough 058) already populate `first_seen_entity_profile`, `first_seen_tier_group`, and `cross_tier` on 3 of the 4 mock narratives so the three-way grid + cross-tier panel have content to render.

---

## Files touched

- `ui/src/App.tsx` — tab label rename.
- `ui/src/pages/Home.tsx` — tagline + body rewritten.
- `ui/src/pages/Narratives.tsx` — full rewrite (497 → 609 lines; net +112 reflects the new panels, not bloat — the page does more than before).
- `ui/src/index.css` — old `.narrative-row*` block replaced with `.narrative-card*`, `.cross-tier-*`, `.amplification-list`, `.narrative-modal-stats`.
- `docs/walkthroughs/README.md` — index row for 061.
- `docs/ui-redesign-plan.md` — Phase 6 checkboxes marked done.

---

## Follow-ups carried forward

- **Per-tier first-seen timestamps** would unlock a real "propagation direction" visual in the cross-tier panel. Requires a new accumulator in `NarrativeAggregator._build_summary` + wire-shape addition. Defer until a page genuinely needs it.
- **Officials column empty state** will stay empty until walkthrough 056's X timeline ingest lands in production and a few cycles populate — the empty-state copy already tells users that.
- **Amplification panel's source threshold** (0.4 propaganda / 0.3 bot-pushed) is hard-coded. Consider making it a URL query param for ad-hoc debugging in a future pass.
- **Mobile breakpoint for the three-way grid** falls back to single-column below 1024px (already defined in walkthrough 060's CSS). On phones the card stack can get long; if it reads as too much, Phase 11 cleanup could introduce per-tier collapsing.
