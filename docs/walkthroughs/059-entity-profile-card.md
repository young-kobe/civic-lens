# Walkthrough 059 — EntityProfileCard component

Phase 4 of the UI Redesign Plan. Foundational reusable component for every three-way dashboard frame (Phases 5–8 will grid these into News Outlets / Verified Officials / General Public columns).

Self-contained: each card manages its own detail-modal state, opens on click/Enter/Space, and sorts correctly against its siblings via the data the aggregators already emit (walkthrough 057/058).

---

## What changed

### `ui/src/components/common/EntityProfileCard.tsx` (new)

One file, ~200 lines, includes both the card and the modal that opens from it. Public export via `components/common/index.ts`.

**Card layout** (top → bottom):

1. Header row — display name (serif, two-line clamp) + lean chip (party letter for officials, lean/tilt label for outlets + subreddits, no chip for catch-alls).
2. Blurb — serif italic, clamped to 120 chars; full text in the tooltip on hover + in the modal.
3. Stats strip — net tone (mono, colored by sign/magnitude), volume, optional confidence dot.
4. Optional `readsAs` line — plain-English interpretation, dashed top border.

**Modal** (opens on click):

- Full blurb rendered as a lead paragraph (`.lead` class from walkthrough 053).
- Stats strip expanded to flat metric cards.
- Lean-source citation below the stats.
- Up to N classification samples (reuses `ClassificationSampleCard` from `publicSentiment/`).
- For officials, a bio link to the Wikipedia source from the registry.

**Props** — flat, not nested:

```ts
interface EntityProfileCardProps {
    profile: EntityProfile;
    netTone: number;      // -100..+100
    volume: number;
    confidence?: ConfidenceLevel;
    readsAs?: string;
    samples?: ClassificationSample[];
}
```

Flat because the callers (aggregator responses) already have these as flat fields on `EntitySentimentItem` — a nested `stats: {…}` object would force callers to repack props they already hold.

**Keyboard / accessibility**:

- `<button>` is the root — Enter and Space both trigger click by default.
- `aria-label` reads `"{name}: net tone {sign}{n}%, {volume} documents. Open details."`.
- Modal already handles `role="dialog"`, `aria-modal="true"`, Escape to close, and focus-trap → close button (from walkthrough 051).

### `ui/src/theme.ts`

New `leanClass(profile)` helper — returns one of `'left' | 'center' | 'right' | 'mixed' | 'neutral'` for the CSS class suffix. Officials derive their class from `party` (R → right, D → left, else neutral); outlets/subreddits use the registry's `lean`/`tilt`; catch-alls land on neutral.

Kept as a pure function in theme.ts because the classification logic is used by both the card's root class and the chip's color class — two consumers justify DRY.

### `ui/src/index.css`

~120 lines of component-scoped CSS added above the GlobalTicker block:

- `.entity-card` — base card layout: flex-column stack, padding, hover + focus-visible, transition.
- `.entity-card-head` / `.entity-card-name` / `.entity-card-chip` — header row.
- `.entity-card-blurb` — deck-style italic serif.
- `.entity-card-stats` / `.entity-card-stat` / `.entity-card-stat-value` / `.entity-card-stat-label` — stats strip.
- `.entity-card-reads-as` — dashed-top interpretation line.
- `.lean-left / -right / -center / -neutral` — border-left color modifiers.
- `.lean-mixed` — repeating-linear-gradient hatched border for mixed-lean entities (two-tone warning + grey stripes at 45°). Uses the `background-image: linear-gradient(bg,bg), repeating-linear-gradient(…)` + `background-origin: border-box` trick so the hatching renders inside the border band without affecting the card body.
- `.lean-chip-*` — matching color variants for the chip.
- `.entity-modal-stats` — auto-fit grid for the modal's stats row.

### `ui/src/components/common/index.ts`

Export `EntityProfileCard` + its default. No new subfolder — sibling to `Card`, `Modal`, `GlobalTicker`.

---

## Why these choices

**Why inline the modal inside the card file instead of a separate EntityProfileModal.tsx?** The modal is only ever instantiated by the card (the card owns its open state). Extracting it forces callers to wire state the card would otherwise hide. One consumer → inline.

**Why `leanClass` in theme.ts instead of on the EntityProfile type?** The profile dict comes straight from the backend; enriching it on the client would re-introduce drift risk between the Python `*Entity.profile_dict()` and the TS `EntityProfile`. Pure derived-value function keeps the source of truth on the backend.

**Why 120-char blurb clamp?** Fits ≈2 lines at the card's font size on the expected 3-col grid width (~320px). Ellipsis + full text in the modal is enough; a smarter "expand in place" toggle on the card would push the card height around the grid and break the row alignment that the three-way frame depends on.

**Why the hatched border for mixed-lean?** The plan called for `mixed = hatched`. Solid warning-yellow would read as "caution" to a user, not "this outlet is editorially ambidextrous". The diagonal two-tone stripe reads unambiguously as "mixed" without triggering a severity connotation.

**Why reuse `ClassificationSampleCard` from pages/publicSentiment/?** It's the canonical sample-card presentation across the app — moving it to components/common/ is tempting but would ripple through 4 existing imports. Left where it is; the card imports from its current location. If a 4th consumer lands, relocate at that point.

**Why no sparkline trend in this version?** The plan wants a 14-day per-entity trend as part of the stats strip. The aggregators don't emit per-entity daily timelines yet (walkthrough 057/058 put net tone + volume on the entity; trends would be a separate accumulator). Deferred to a follow-up; the card layout has room for it when it arrives.

**Why the component file doesn't own any fetch logic?** Data flow is: aggregator → `EntitySentimentItem` / `PropagandaEntityItem` on the page → EntityProfileCard props. The card is presentation-only, which keeps it testable as a pure render + lets each page decide how to cap the grid (e.g. top 6 by volume).

---

## Verification

```
cd ui && npm run typecheck  # clean
cd ui && npm run build      # clean, 3.47s
```

Visual verification: the card mounts inside the dev-mode sentiment page as soon as Phase 5 wires it into the grid (next walkthrough). For now the file is declared, exported, and compiles — callers in walkthrough 060+ will consume it.

---

## Files touched

- `ui/src/components/common/EntityProfileCard.tsx` — new.
- `ui/src/components/common/index.ts` — export.
- `ui/src/theme.ts` — `leanClass()` helper.
- `ui/src/index.css` — `.entity-card*` + `.lean-*` + `.entity-modal-stats` rules.
- `docs/walkthroughs/README.md` — index row for 059.
- `docs/ui-redesign-plan.md` — Phase 4 boxes checked.

---

## Follow-ups carried forward

- **Per-entity 14-day sparkline** — aggregators don't emit the trend yet; add alongside Phase 5 if the Overall Tone cards need one per entity.
- **Shared location for ClassificationSampleCard** — currently in `pages/publicSentiment/`. If EntityProfileModal becomes the 4th+ consumer, move to `components/common/` in the Phase 11 cleanup pass.
- **By-topic breakdown inside the modal** — plan wanted this; the aggregator data to power it (per-entity × per-topic matrix) isn't cheap to emit. Defer until Phase 7/8 when we have a clearer consumer.
- **Onboarding-friendly empty state** — when the Officials column is empty pending walkthrough 056's data to land, wrap callers with an `EmptyState`. Phase 5's page code is the right place for that, not the card itself.
