# 2026-04-25 — Overall Tone is now topic-driven

The Overall Tone page is filtered by political topic. A 15-tab category bar (14 backend topics + "All Topics") sits as the visual anchor of the page; selecting a topic re-scopes the three tier headlines, the editorial framing sentence, and the entity profile modal's evidence list. The biggest-movers ticker has been removed from this page; GOP party stance moved out of the dense top-metrics block and into the page's GlobalTicker strip, color-coded with the same threshold map as Overall tone.

## What shipped

### New surfaces

- `ui/src/services/topics.ts` — Canonical UI taxonomy. 14 topics + "all", each with key, slug, inline 24x24 stroke-SVG path data, and a copy of the backend keyword list. Mirrors `analysis/src/reporting/aggregators/constants.py::TOPIC_KEYWORDS`. Source of truth remains the Python file; this file exists because the UI needs the icons and slugs the backend doesn't carry.
- `ui/src/pages/publicSentiment/TopicTabBar.tsx` — Big-tab category bar. Lays out as a CSS grid using `grid-template-columns: repeat(auto-fill, minmax(96px, 1fr))` so wrapping a partial second row keeps every column the same width as the first row (the earlier flex-wrap layout let orphan tabs balloon to ~2x the others — see `docs/evidence/nav table spacing bug.png`). Tabs stack icon over label and surface the current-window post count under each label so a reader sees at a glance which topics are dense. Active tab gets a solid bottom-border accent + brighter background. Below 640px the layout switches back to a horizontally-scrolling flex strip (84px tabs) instead of compressing into a dropdown — keeps the affordance visible.
- `ui/src/index.css` — Two new style blocks: `.topic-tabbar` + `.topic-tab*` and `.modal-topic-strip*` (the in-modal scope chip).

### Reworked surfaces

- `ui/src/pages/PublicSentiment.tsx`:
  - Holds `activeTopicKey` state synced to a `?topic=<slug>` URL query param via `history.replaceState` + `popstate`. URL drives initial state on first paint; if no URL value is present, the page picks the highest-volume topic from `data.byTopic` once data lands. `'all'` clears the param.
  - `TopMetrics` now takes an `activeTopic` + pre-resolved `topicRow`. When a topic is active, tier rows derive from the matching `byTopic` row's three-way split (`newsNet`/`officialsNet`/`publicNet` + matching volumes) rather than aggregating the global per-tier entity rollups. Tier rows distinguish "no data on this topic" from "real zero" via a nullable `agg.net` — empty tiers render `—` with the verb "no posts on this topic". The `aux` slot now carries only `IntensityMini`; `GOPMini` was deleted (and with it the `Sparkline` import) and the GOP party-stance number moved into the page's `GlobalTicker` strip — see below.
  - `buildSentimentTickerItems` puts GOP stance immediately after Overall tone in the GlobalTicker, color-coded with the same threshold map (`>10 → accent`, `<-10 → negative`, else `neutral`) factored into a shared `netToneColor` helper. Posts-scored + Confidence trail behind so the two political-tone numbers read as a pair.
  - `readsAsToday` returns the spec sentence when filtered: *"How news outlets, public officials, and everyday people are feeling and talking about [TOPIC]."* The unfiltered fallback keeps the prior static sentence.
  - `EntitySentimentModal` filters `classificationSamples` client-side via `matchesTopic` (substring scan over title + body + evidence spans) when a topic is active. The headline net score remains the entity's global score; a `TopicScopeStrip` chip below the title makes that limit explicit ("Showing: Economy · 4 of 12 recent posts match this topic") and the "How they lean" stat carries an inline disclosure that the per-topic entity score is not yet available from the backend.
  - Three-way grid columns keep their global entity cards; the column bylines get a suffix when a topic is active ("scores are global; click to filter evidence to <topic>") so the user understands the click-through is the topic-scoped surface, not the card itself.
  - `MoversTicker` is no longer rendered on this page — neither the entity-mover strip nor any topic-pill variant. The page's only ticker is now the `GlobalTicker` at the top. The `fetchMovers` call and its `useFetch` were removed; the `MoversTicker` component file is kept in `components/common/` for possible reintroduction elsewhere.

### Internal fixture rename

`ui/src/services/fixtures.ts` topic names were renamed from prose labels (`'Border & immigration'`, `'Economy & inflation'`, `'Foreign policy'`, `'Climate & energy'`) to the canonical backend keys (`'Immigration'`, `'Economy'`, `'Foreign Policy'`, `'Climate'`). Fixtures were already inaccurate to current production output; the rename makes mock-mode usable for verifying topic-scoped behavior.

## API gaps surfaced (backend follow-ups)

Two pieces of backend work would replace client-side workarounds with proper data. Both are filed as follow-up tasks:

1. **Per-entity per-topic rollups in `SentimentAggregator`.** `byNewsOutlet` / `byOfficial` / `byGeneralPublic` are currently global. The UI works around this by (a) filtering the modal's classification samples client-side using a duplicated keyword list and (b) leaving entity-level scores global with a disclosure chip. To replace both: emit `byNewsOutlet[topic="Economy"]` (or equivalent nesting) so the modal can show a real per-topic net score, and so the three-way grid can re-rank entities by topic. Touches `analysis/src/reporting/aggregators/sentiment.py` and `analysis/src/reporting/models/aggregator_models.py`. When this lands, delete `keywords` + `matchesTopic` from `ui/src/services/topics.ts`.

2. **Window-over-window topic deltas in `MoversAggregator`** — *deferred until movers come back.* When the page first shipped, the plan was to surface topic deltas inside the ticker; the ticker was removed before merge. If a future revision reintroduces a topic-mover surface, this is the backend work it would need: `MoversAggregator.get_movers` computes prev-window per-topic nets and emits `topic_movers: {topic, current_net, prev_net, delta_pts, ...}`. Touches `analysis/src/reporting/aggregators/movers.py` and the `/movers` response schema.

Neither blocks ship — UI degrades honestly without them.

## URL persistence pattern

This is the first per-page filter on the dashboard; all prior filters live as global component state in `App.tsx`. `?topic=<slug>` was chosen over hash for two reasons: the hash already encodes the active tab (set by `App.tsx`), and a query param makes shareable deep links into a specific topic possible without colliding. State + URL kept in sync via `replaceState` (no extra history entries on every click) and `popstate` (back/forward navigation does the right thing).

## Why this shape

Spec was authored in collaboration with the user during the implementation conversation. The big-tab bar (option A2) won over a dense pill row (A1) because Civic Lens prioritizes simplicity for non-expert users — the tab bar reads as obviously interactive at a glance. The in-modal accent strip (B2) won over an inline header chip (B1) because the data limitation is non-trivial and deserves more than a chip's worth of disclosure. A first iteration shipped a hybrid topic + entity ticker (C2); it was removed in the same PR after a visual review — the GlobalTicker already carried the only numbers the page needed at a glance, and a second scrolling strip read as duplicative noise above the topic tab bar.

## Follow-ups (UI side)

- After the per-entity per-topic backend work lands, delete the `keywords` field + `matchesTopic` helper from `topics.ts`, replace the modal's client-side sample filter with the topic-scoped backend payload, and drop the "scores are global" disclosure from the modal + grid bylines.
