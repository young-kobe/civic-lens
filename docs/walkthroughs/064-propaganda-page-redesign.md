# Walkthrough 064 — Propaganda page redesign

Phase 7 of the UI Redesign Plan. Brings the Propaganda page to parity with Overall Tone (060) and Political Narratives (061/062): GlobalTicker at top, "reads as today" headline, entity drill-down modal, source legend.

Tab id unchanged (`propaganda`).

---

## What changed

### `ui/src/pages/Propaganda.tsx`

- **GlobalTicker** (top). `buildPropagandaTickerItems(data)` emits four stats: flagged rate (tone-colored: red/amber/green at 20/10 thresholds, emphasis), flagged post count over total scored, mean score, top technique name + % of flagged.
- **Reads-as-today headline** below the ticker. `readsAsToday(data)` template: names whichever side (news vs social) leans harder on these techniques plus the single most prevalent technique ("Social media leans on these techniques more than news (18.4% vs 12.8% flagged). Loaded language is the most common, appearing in 47% of flagged posts.").
- **Entity drill-down modal**. `PropagandaEntityCard` now takes an `onOpen` callback instead of pointing at the external entity URL. Click → `PropagandaEntityModal` opens — same shell pattern as Overall Tone's `EntitySentimentModal`: `EntityHeader` (avatar + full blurb), flagged-rate / mean-score / posts-scored stats, optional external visit link + lean-source citation, then a filtered list of flagged examples for that entity. Cards with `total_docs === 0` fall back to `href` (external link) so tracked-but-no-data entities still surface the entity homepage.
- **`entityMatchesExample(item, ex)`** filter. Outlets match by `source_type === 'news'` + domain (stripping `www.` both sides, case-insensitive). Officials match by `source_type === 'x_post'` + `author_handle` (case-insensitive). Subreddits match by reddit source type + domain. Catch-all buckets don't filter — the modal shows a "try widening the window" hint instead, since a catch-all isn't a single entity.
- **`ExampleRow` extracted** from inline inside `ExamplesCard` so both the main card and the entity modal render flagged examples in the same shape. Source meta line now prefers `X · @handle` for x_posts when a handle is present — was `x_post · unknown · doc #N` before.
- **Legend dot on NewsVsSocialCard rows**. `.source-split-dot` (10px circle) prepended to the label; News uses `--neutral-600`, Social Media uses `COLORS.warning` to match the existing `.tier-row-dot` color on Propaganda's News-vs-Social row in the top metrics. Bar + row colors now read as one encoding instead of two.

### `ui/src/types.ts`

`PropagandaExample` gains optional `author_handle?: string | null`. Used by the modal's filter (officials) and `ExampleRow`'s source meta.

### `analysis/src/reporting/aggregators/propaganda.py`

- `PropagandaExample` dataclass gains `author_handle: Optional[str] = None`. `asdict()` in `to_dict()` picks it up automatically.
- `_fetch_examples` SQL gains `LEFT JOIN x_posts_raw` + `LEFT JOIN x_users_raw` with the same pattern already used by `_fetch_rows`; selects `u.username`.
- `_build_overview` tuple unpacking extended by one field; `PropagandaExample(...)` constructor passes `author_handle=author_handle` through.

(This duplication of the X-author join is already flagged in `docs/todo-backend-aggregator-audit.md` as hotspot #1 — unified helper deferred to that cleanup pass.)

### `ui/src/services/fixtures.ts`

`examples` in `mockPropaganda()` expanded from 3 rows to 6 and annotated with `author_handle`. Includes two POTUS examples, two foxnews.com examples, one r/Conservative example, one r/politics example — so every clickable entity card in dev mode has ≥1 matching example visible in its drill-down modal.

### `ui/src/index.css`

`.source-split-row-label` upgraded to flex with `gap: 6px`. New `.source-split-dot` class (10px round swatch) — same visual grammar as the `.tier-row-dot` family, so the legend matches what the reader already sees on the top-metrics axis.

---

## Why these choices

**Why filter the flat `examples` list client-side instead of emitting per-entity examples from the aggregator?** The `examples` list is already capped at a small limit (configured server-side, currently ~N rows). Shipping a separate per-entity array would rebuild the same row payload N times inside the snapshot JSON (once per entity item × tier × example) — all to save one client-side `.filter()` per modal open. The client-side filter is cheap and the data transfer savings are real.

**Why keep the `href` fallback on cards with zero total_docs?** Empty-state cards read as "tracked but no coverage yet" — if the user clicks, an external link to the entity's homepage is more useful than a modal that says "no data". The modal is only worth opening when there's entity-specific content to show.

**Why not make the Propaganda entity modal use the back-nav pattern from walkthrough 063?** The propaganda entity drill isn't nested — clicking an example row inside the modal doesn't open a further modal (the row IS the detail view — title, preview, technique list with evidence spans). One modal deep, one level of Esc/close. The `onBack` prop on `Modal` is still there; this page doesn't need it.

**Why reuse Narratives' `.reads-as-today` + `.lead` classes instead of defining new ones?** The block is visually identical — a serif paragraph introduced by an "As of today" eyebrow. A new class name would diverge for no reason; the shared rule means any future typography tweak touches both pages at once.

**Why template-derived "reads as today" instead of LLM-generated?** Same answer as Phases 5/6: deterministic, cheap, debuggable, no latency. The template captures the two shapes of sentence Propaganda's data makes possible ("side A uses techniques more than side B" + "technique X is most common"). If we want subtler framing later, the template can grow without a backend round-trip.

**Why not split the "Top flagged entities" into a ranked bar chart instead of keeping the three-way entity grid?** The plan floated both options. The entity grid already exists (walkthrough 058 + 059); migrating to a bar chart would replace the card-avatar visual with a dense horizontal-bar list that sacrifices the editorial feel the Tone page establishes. Consistency across pages wins here; the `PropagandaEntityCard` already sorts highest-to-lowest within each column via the aggregator's `mean_score desc` order.

**Why `entityMatchesExample` as a function instead of a `matches(entity)` method on `PropagandaExample`?** Examples are wire-level dicts from the API, not first-class objects with methods. A free function at module scope is the right abstraction level — no class needed.

---

## Verification

```
npm run typecheck   # clean
npm run build       # clean, 3.6s
python -m unittest analysis.tests.test_propaganda_surfaces   # 9/9 pass
```

Dev-mode sanity (`VITE_USE_MOCKS=true`):

- Ticker at top shows 15.5% flagged rate (amber), 187 flagged of 1210 scored, 0.22 mean, "Loaded language · 47% of flagged".
- Reads-as-today: "Social media leans on these techniques more than news (18.4% vs 12.8% flagged). Loaded language is the most common, appearing in 47% of flagged posts."
- Clicking Fox News card → modal with stats, 2 flagged examples (border-policy op-ed + economic-record commentary).
- Clicking POTUS card → modal with 2 flagged examples (radical-left + reciprocal-tariffs).
- Clicking r/Conservative → modal with 1 flagged example (media-ignores megathread).
- Clicking an entity card whose domain/handle doesn't match any example → "No flagged examples in this window — try widening the time window" hint.
- News-vs-Social card now shows grey + amber dots matching the top-metrics axis.

---

## Files touched

- `analysis/src/reporting/aggregators/propaganda.py` — `PropagandaExample.author_handle`; `_fetch_examples` X-author join; unpacking + constructor updates.
- `ui/src/types.ts` — `PropagandaExample.author_handle?: string | null`.
- `ui/src/pages/Propaganda.tsx` — GlobalTicker + reads-as-today + PropagandaEntityModal + ExampleRow extract + source-legend dots.
- `ui/src/services/fixtures.ts` — 3 → 6 propaganda examples annotated with `author_handle`.
- `ui/src/index.css` — `.source-split-row-label` flex + new `.source-split-dot`.
- `docs/walkthroughs/README.md` — index row for 064.
- `docs/ui-redesign-plan.md` — Phase 7 checkboxes marked done.

---

## Follow-ups carried forward

- **X-author join duplication**: fourth aggregator now uses the same `LEFT JOIN x_posts_raw / x_users_raw` pattern. Consolidation tracked in `docs/todo-backend-aggregator-audit.md` hotspot #1.
- **Per-entity example fanout**: if a future design wants every entity card to always show 3 example snippets inline (not just inside the modal), we'd need a backend change — the current `examples` list is window-wide, not per-entity. Defer.
- **Catch-all modal content**: right now the catch-all cards open a modal with the empty hint. Could synthesize a "top 10 domains inside this catch-all" view, but the catch-all is explicitly the bucket we editorialize on *least*, so not worth the mechanism.
- **Methodology copy**: current "technique density, not authorial intent" framing reads cleanly. If the scoring changes (e.g. per-technique calibration), revisit.
