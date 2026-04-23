# Walkthrough 063 — Nested-modal back navigation + supporting-docs drill-down

Follow-on to walkthrough 062. Two asks:

1. **Back navigation** for nested modals — walkthrough 062 layered the narrative detail modal on top of the narrative entity modal via an `{activeEntity && !activeNarrative}` guard, but the detail modal's close button tore the whole chain down. The user asked for a clear way to go back to the parent drill-down without losing context.
2. **Top supporting documents table** inside `NarrativeDetailModal` — richer drill-down. Below the daily-volume sparkline + source-mix section, list the highest-signal docs clustered into the narrative with headline / source / when / tone + confidence / reasoning / external link.

---

## What changed

### `ui/src/components/common/Modal.tsx`

Two new optional props:

```ts
/** When provided, renders a ← arrow in the header that invokes this handler. */
onBack?: () => void;
/** Label describing what Back returns to — shown in the title / aria-label
 *  (e.g. "Back to NYT"). Defaults to "Back". */
backLabel?: string;
```

The `<header>` now renders the arrow button before the title block when `onBack` is set, so single-level modals (the default) look exactly as before. Accessibility:

- `aria-label="Back to <label>"` (falls back to `"Back"` when `backLabel` is omitted).
- Matching `title` attribute for hover tooltip.
- Arrow SVG styled with the same stroke widths as the close X so the two buttons read as a pair.

No change to the Esc-to-close handler — users can still dismiss via Escape, which on the detail modal now also clears the parent entity modal (see the Narratives wiring below). This matches the behavior of every other modal on the site: Esc / backdrop click = "I'm done with this drill-down entirely."

### `ui/src/index.css`

New `.modal-back` rule, identical visual style to `.modal-close` so the two buttons sit symmetrically around the title block. Hover state matches.

```css
.modal-back {
  /* 32×32 flush icon button, neutral-500 → neutral-800 on hover. */
  /* margin-right: calc(-1 * var(--space-1)); tightens the gap to the title. */
}
```

### `ui/src/pages/Narratives.tsx`

`NarrativeDetailModal` now takes optional `onBack` + `backLabel` props and forwards them straight to `<Modal>`. Page-level wiring:

```tsx
{activeNarrative && (
    <NarrativeDetailModal
        narrative={activeNarrative}
        onClose={() => {
            // Full dismiss — Escape / X / backdrop tears the chain down.
            setActiveNarrative(null);
            setActiveEntity(null);
        }}
        onBack={activeEntity
            ? () => setActiveNarrative(null)   // Back → pop one level.
            : undefined}                        // No parent → no arrow.
        backLabel={activeEntity?.profile.displayName}
    />
)}
```

Close = full dismiss; back = pop one level. When the user reached the detail modal via `ClaimsSpreadingPanel` (no parent entity), `onBack` is undefined and the arrow doesn't render — exactly the behavior a single-level modal wants.

### `ui/src/types.ts`

New `SupportingDoc` interface (generic — used by both the Narratives drill-down table and the Overall Tone entity / topic drill-downs):

```ts
export interface SupportingDoc {
    doc_id: number;
    title: string | null;
    source_type: string;
    source_label: string;   // "News · nytimes.com" | "X · @Schumer" | "Reddit · r/politics"
    url: string | null;
    published_at: number | null;
    sentiment_label: 'positive' | 'negative' | 'neutral' | null;
    confidence: number | null;
    reasoning: string | null;
}
```

`NarrativeSummary` gets `top_supporting_docs?: SupportingDoc[]`.

### `ui/src/components/common/SupportingDocsTable.tsx` — shared drill-down table

New sub-component. Table columns: Headline / Source / When / Tone / Reasoning / external-link arrow. Tone cell shows the label colored by sentiment plus a small tabular-nums confidence percentage. Reasoning is 2-line clamped with a full-text `title` tooltip. When `docs` is empty the component renders nothing — no placeholder UI for data we don't have yet.

Ships with a `classificationSampleToSupportingDoc(sample)` adapter so the Overall Tone page's existing `ClassificationSample[]` payloads feed the same table without a backend change. Label enum normalized (`POSITIVE → 'positive'` etc.), `source_name` re-shaped into the "Kind · name" label used by narratives.

**Applied at three drill-down sites:**

- `Narratives.NarrativeDetailModal` — bottom section of the narrative drill-down (the original walkthrough 063 use-case).
- `PublicSentiment.EntitySentimentModal` — replaces the previous `ClassificationSampleCard` card-list for the "Recent classified posts" section. Consolidates to one scan-oriented shape per entity.
- `publicSentiment/TopicDivergencePanel.TopicSamplesModal` — same swap; per-topic samples also render as a table.

`ClassificationSampleCard` stays on disk — still used by `SentimentDistributionCard`'s per-intensity bucket drill (a read-oriented view, not a scan-oriented one) where the evidence-span + full-text layout earns its keep.

### `ui/src/index.css` — `.supporting-docs-table*`

New CSS block immediately after `.narrative-modal-stats`. Design choices:

- `.supporting-docs-table-wrap` — hairline rounded border, `overflow-x: auto` for narrow screens.
- Mono uppercase 10px header row with `letter-spacing: 0.08em` to match the `.eyebrow` / `.tier-row-label` family.
- Body rows: 12px serif-adjacent text, hairline bottom border, hover tints using `var(--bg-inset)` so the interaction signal stays within the paper-feel palette.
- `.supporting-docs-headline` + `.supporting-docs-reasoning` use WebKit line-clamp at 2 lines; headline max-width 280px, reasoning 340px.
- `.supporting-docs-link a` uses the existing `--accent` / `--accent-hover` tokens so the arrow matches every other external link on the site.

Every color reference goes through a CSS custom property — zero hard-coded hex in the new block.

### `ui/src/services/fixtures.ts`

New `supDoc({...})` helper + extended `narrative()` helper to accept an optional `supporting: NarrativeSupportingDoc[]`. Populated the three most-clickable mock narratives (Border-crossings / POTUS reciprocal tariffs / Tech-giants suppression) with 4–6 representative supporting docs each. Dev mode now shows the full table immediately — every column populated, tone mix, reasoning spans, external links.

### `analysis/src/reporting/models/aggregator_models.py`

`NarrativeSummary.top_supporting_docs: List[Dict[str, Any]] = field(default_factory=list)`. `to_dict` appends the key. The UI's TS interface is authoritative for the row shape; Python just stores the dicts verbatim.

### `analysis/src/reporting/aggregators/narrative.py`

New constant `TOP_SUPPORTING_DOCS_LIMIT = 6` and two module-level helpers:

- `_build_source_label(source_type, domain, x_handle)` — produces the same human-readable labels the TS mock fixtures use. Single source of truth for label formatting across the three source types.
- `_build_doc_url(source_type, domain, ident, x_handle=None)` — mirrors the sentiment sampler's URL logic but adds X support (`https://x.com/{handle}/status/{ident}`).

New method on `NarrativeAggregator._top_supporting_docs(cursor, narrative_id, cutoff)` — LEFT JOIN `ai_outputs` (task_type='sentiment'), order by `COALESCE(a.confidence, 0) DESC, d.published_at DESC`, limit 6. Rows without a sentiment classification still appear (null tone / confidence / reasoning) so the table doesn't drop docs purely for missing analysis. Reasoning truncated at 240 chars with `...` suffix. Sentiment label normalized to the UI's lowercase enum: `POSITIVE → 'positive'`, `NEGATIVE → 'negative'`, `NEUTRAL|MIXED → 'neutral'`.

Called from `_build_summary` and passed through into `NarrativeSummary.top_supporting_docs`.

### `docs/walkthroughs/README.md`

The user fixed an ordering mistake I introduced in walkthrough 062 (062 appeared above 061); re-ordered to chronological. Index row for 063 appended.

---

## Why these choices

**Why `onBack` as a prop on `Modal` rather than a separate `<NestedModal>` wrapper?** The only thing a nested modal needs that a top-level modal doesn't is a back button. A wrapper would re-render an identical shell for one extra button — net negative. Prop on the base is a 4-line addition and every existing caller is unchanged.

**Why does the narrative modal's X / Esc tear down *both* modals in the chain instead of just the top one?** Escape-closes-the-current-modal is the familiar pattern, but when you're two modals deep on a drill-down, "close the current" usually means "I'm done with this whole drill-down" — if I wanted to continue browsing the entity, I'd have clicked Back. Making X + Esc dismiss the chain keeps the exit path short. Back is always available for users who want to step up one level; it's an additive capability, not a replacement for close.

**Why order supporting docs by sentiment confidence instead of by propaganda score, bot-pushed, or publish date?** Confidence is the proxy for "how sure is the model that this doc actually belongs in this narrative's tone column" — high-confidence docs are the best evidence for what the narrative is saying. Propaganda score is signal about rhetoric, not about how representative the doc is. Publish date privileges recency which is orthogonal to quality. If we later want "most-recent first" as a secondary sort, `ORDER BY confidence DESC, published_at DESC` already gives us that as a tiebreaker.

**Why keep rows with null sentiment instead of filtering them out?** During bootstrap of a fresh time window, some supporting docs may be clustered into narratives before the sentiment engine runs on them. Dropping them from the table means a narrative can look empty-ish for an hour or two after crawl; keeping them as "—/—/—" rows with the headline + source + link lets users drill down anyway. The tone column displays `—` for nulls — visually honest.

**Why synthesize `source_label` in Python instead of the UI?** Three reasons: (1) the backend already has domain, x_handle, subreddit in the same row — the UI would have to re-assemble pieces we just split apart; (2) the label shape ("News · nytimes.com") is an editorial choice that should live with the data contract, not the presentation; (3) future source types (Mastodon, Threads, Substack) add a label case in one place instead of every consumer.

**Why truncate reasoning to 240 chars server-side?** The table clamps to 2 lines visually, but the `title` attribute shows the full text on hover — so clients do want the whole string for the tooltip. 240 chars is ~3 lines of rendered text, which matches what the hover tooltip can reasonably show before the reader abandons it. Unlimited reasoning means a single verbose LLM output bloats the snapshot cache JSON. 240 is an editorial ceiling on what the tooltip should ever need to display.

**Why an `<a>` with `rel="noreferrer"` instead of routing through a tracking link?** Consistent with every other external link on the site — we don't attach per-link analytics, and `noreferrer` is the invariant policy (walkthrough 047's security pass).

**Why not also add a "View all X supporting docs" expand below the table?** The table already hits the `supporting_doc_count` for small narratives (< 6) and shows the top 6 for larger ones. A full expand would pull hundreds of rows into one modal — pagination territory. Not justified yet; revisit if users ask.

**Why populate supporting docs in 3 of 9 mock narratives instead of all 9?** The three chosen are the biggest / most-likely-to-be-clicked (highest supporting_doc_count), and each represents one of the three tiers (News / Officials / Public). A quick survey via the entity grid + click-through on any of the three renders a fully populated table; the other six still render their existing modal content. Reduces fixture-maintenance cost without reducing demo-value.

---

## Verification

```
npm run typecheck   # clean
npm run build       # clean, 3.7s
python -m unittest analysis.tests.test_rich_aggregators analysis.tests.test_account_classifier   # 21/21 pass
```

No new Python tests. The supporting-docs helpers are plain-SQL shape; they match the existing test fixtures' schema (walkthrough 058 added `x_posts_raw` / `x_users_raw` stubs in those setUps), so `test_account_classifier` already exercises the new `_top_supporting_docs` code path without any added setup.

Manual dev-mode sanity check (`VITE_USE_MOCKS=true`):

- Click an entity card in the Political Narratives tab → entity modal opens.
- Click a narrative inside → detail modal opens with ← arrow in the header titled "Back to NYT" (for the NYT card) or "Back to POTUS" etc.
- ← arrow returns to the entity modal with that entity's other narratives still listed.
- X / Esc / backdrop click dismisses the whole chain and clears both `activeNarrative` + `activeEntity`.
- Supporting-docs table renders for narratives 1001 / 2001 / 3001; other narratives show their modal without the table (no empty state).
- Clicking a table row's ↗ opens the source in a new tab.

---

## Files touched

- `ui/src/components/common/Modal.tsx` — `onBack` + `backLabel` props; header layout updated.
- `ui/src/index.css` — `.modal-back` + `.supporting-docs-table*` blocks.
- `ui/src/types.ts` — `NarrativeSupportingDoc` interface + `top_supporting_docs` field on `NarrativeSummary`.
- `ui/src/pages/Narratives.tsx` — `NarrativeDetailModal` accepts `onBack` / `backLabel`; new `SupportingDocsTable` component; page-level wiring splits X/Esc (full dismiss) from ← (pop one level).
- `ui/src/services/fixtures.ts` — `supDoc()` helper; `supporting` key threaded through `narrative()`; 3 narratives populated with representative drill-down rows.
- `analysis/src/reporting/models/aggregator_models.py` — `top_supporting_docs` field on `NarrativeSummary`.
- `analysis/src/reporting/aggregators/narrative.py` — `TOP_SUPPORTING_DOCS_LIMIT`, `_build_source_label`, `_build_doc_url`, `_top_supporting_docs`; called from `_build_summary`.
- `docs/walkthroughs/README.md` — 061/062 ordering fix + 063 row.
- `docs/walkthroughs/063-modal-back-nav-and-supporting-docs-table.md` — this doc.

---

## Follow-ups carried forward

- **Propagate back-nav to other drill-down chains**: currently only Narratives uses nested modals. If Overall Tone or Propaganda grow a second drill level, the `onBack` prop is already in place — no Modal changes needed.
- **Backend aggregator redundancy audit**: tracked in `docs/todo-backend-aggregator-audit.md`. Multiple aggregators re-implement the same X-author join, per-window doc walks, and entity-rollup patterns. Refactor deferred — not this scope.
- **Real-data verification post-deploy**: the Python method has only been exercised against test fixtures so far. First prod run will fill the field for all narratives; worth eyeballing a few after walkthrough 056's X ingest has been live for a couple of cycles.
- **Per-tier filter inside the table**: "show only News supporting docs" / "only X" — cheap client-side filter if a reader ever wants to isolate one source type. Defer until users ask.
- **Reasoning truncation visible in the UI**: the 240-char server truncation + 2-line client clamp currently stack. If the tooltip starts getting truncated strings too often, bump the server ceiling to 400.
- **Retire `ClassificationSampleCard`?**: after this pass the only remaining consumer is `SentimentDistributionCard`'s per-intensity drill. If that drill moves to the table shape too, the component + its CSS can be deleted in a Phase 11 cleanup pass.
