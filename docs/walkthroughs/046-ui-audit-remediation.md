# 046 — UI-Layer Audit Remediation (2026-04-20)

Lands § 4 of the 2026-04-19 non-security audit. Closes the UI layer; the
four layers are now audit-clean except for items explicitly deferred on
the audit file (calibration, test scaffolding, Pydantic response models,
loader.py split, settings nesting, O(N·M) cosine index).

## Scope

- Dead-code removal (`transformBotData`, `.grid-4`).
- Palette single-source-of-truth (`theme.ts`).
- Page decomposition (`PublicSentiment`, `Review`, `GlobalHeatmap`).
- Data-layer ergonomics (`fetchJSON` + `useFetch`).
- Retry UX unification.
- First accessibility pass (aria-hidden on decorative, aria-label on
  charts, role=progressbar on confidence meters).

Not in scope: UI unit-test scaffolding (audit-deferred). Error-copy
tightening beyond the X-specific GlobalHeatmap fallback.

## Changes

### Dead code

- `ui/src/services/transformers.ts`: `transformBotData` deleted (no
  importer); `BotData` type import dropped.
- `ui/src/index.css`: `.grid-4` and its two responsive variants removed
  (`.grid-3` is the widest layout actually used). The skeleton-* classes
  the audit flagged as unused turned out to be in active use via
  `components/common/LoadingState.tsx`, so they're kept — audit claim
  was wrong about those.

### Palette single-source-of-truth

- `ui/src/theme.ts` exports `SEMANTIC_COLORS` (mirrors the CSS
  custom properties from `index.css :root`) and three helpers:
  `sentimentColor(label)`, `netSentimentColor(score, ...)`, and
  `accuracyColor(pct)`. Keep the hex values here in lockstep with the
  CSS file — this module is the one place JS/TS consumers read from.
- Replaced hardcoded `#008a4c`/`#d41e0e`/`#8e8e96`/`#b26100` literals in:
  `PublicSentiment.tsx`, `Narratives.tsx`, `GlobalHeatmap.tsx`,
  `Review.tsx`, and the new subcomponents under `pages/publicSentiment/`.
- One color falls outside the semantic tokens: the mild-positive
  midpoint `#4b9e6d` used by `GlobalHeatmap.getSentimentColor`. Kept as
  a local constant with a comment, not a new token — adding a one-off
  token to the palette to accommodate a single gradient step isn't
  worth it.

### PublicSentiment decomposition

Extracted five subcomponents to `pages/publicSentiment/`:

```
pages/publicSentiment/
├── SentimentOverviewHeader.tsx   (net-score + volume + confidence)
├── MiniDonut.tsx                 (CSS-only conic donut; aria-hidden by default)
├── TopicRow.tsx                  (expandable per-topic row)
├── ClassificationSampleCard.tsx  (reasoning + evidence + source text)
└── SentimentDistributionCard.tsx (5-point intensity stacked bar)
```

`PublicSentiment.tsx` dropped from 885 → 417 LoC (-468). The extracted
components accept their palette (`labelBadgeStyles`, `badgeStyle`) as
props so they don't depend on the page's local constants, and each one
renders identically to the pre-refactor version.

### Review extraction

`ReviewItemCard` (215 LoC) lifted to `pages/review/ReviewItemCard.tsx`,
including its `LABEL_OPTIONS_BY_TASK` constant and `modelLabel` helper
that only it consumed. `Review.tsx` dropped from 470 → 222 LoC (-248).

### GlobalHeatmap styles

The ~125-line inline `<style>{...}</style>` block moved to `index.css`
under `.global-heatmap` scope so the page file is pure JSX. Also:
replaced the X-specific "Please enable the X integration" fallback with
the shared `ErrorState` + refetch, so the copy doesn't lie if geo ever
expands beyond X.

### Data-layer: fetchJSON + useFetch

- `services/api.ts`: single `fetchJSON<T>(path, init)` helper runs every
  endpoint. Admin-token header merging centralized via the `admin: true`
  flag on init. Each public endpoint is now a one-liner.
- `services/useFetch.ts`: minimal hook with a module-level `Map<string,
  unknown>` cache. Cache hit resolves synchronously; `refetch()` clears
  the cache entry and re-fires. Safe against unmount races via a
  mounted-ref guard. `invalidateFetchCache(key?)` exported for
  post-mutation invalidation.
- PublicSentiment, BotActivityProfiler, Narratives, GlobalHeatmap, and
  Propaganda now use `useFetch`. Review keeps its own fetch state — it
  has imperative post-submit-refetch semantics that don't fit the
  hook's cache contract.
- **API versioning follow-up**: `API_BASE` in `services/api.ts` already
  switched to `/api/v1` in walkthrough 045. UI matches the backend.

### Retry UX unification

Every `ErrorState.onRetry` now triggers `refetch()` from `useFetch`
(soft retry). Before: PublicSentiment / Narratives / Propaganda forced
`window.location.reload()` (hard refresh); BotActivityProfiler did
`setError(null)` (which was actually a no-op since the effect already
completed).

### Accessibility pass

- `MiniDonut` is `aria-hidden` by default. Callers who surface it as
  primary info can pass `ariaLabel`.
- `ClassificationSampleCard` confidence meter gains `role="progressbar"`,
  `aria-valuenow`, `aria-valuemin`, `aria-valuemax`, and an
  `aria-label`.
- `Sparkline` wraps its ResponsiveContainer in a `role="img"` div with
  a generated `aria-label` (caller can override via `ariaLabel`).
- `SentimentDistributionCard` chart bar gets a `role="img"` +
  `aria-label` that reads the per-segment percentages.
- `ConfidenceBadge` gains `role="status"` and a combined
  coverage+confidence `aria-label`; its decorative dot is
  `aria-hidden`.
- `TopicRow` button gains `aria-expanded` and its chevron is
  `aria-hidden`.
- `Review` confidence slider gains an `aria-label`.

The `.eyebrow`-as-semantic-markup concern (audit) is left OPEN — changing
those to `<h2>`-style semantic tags is a design decision that spans
every page and deserves its own pass.

## Verification

```
cd ui
npm run typecheck    # clean
npm run build        # 1171 modules transformed, dist emitted
```

Pages touched (visual regression check):

- PublicSentiment, Narratives, Propaganda, BotActivityProfiler,
  GlobalHeatmap, Review — all render identically to pre-refactor in
  manual spot-check; `useFetch` cache hit is instant on tab-switch.

## Files touched

```
docs/audits/04_19_2026.md                                 (OPEN → REMEDIATED/PARTIAL)
ui/src/index.css                                          (-.grid-4, +.global-heatmap styles)
ui/src/services/api.ts                                    (fetchJSON rewrite)
ui/src/services/transformers.ts                           (-transformBotData)
ui/src/services/useFetch.ts                               (new)
ui/src/theme.ts                                           (new)
ui/src/components/charts/Sparkline.tsx                    (role=img + aria-label)
ui/src/components/common/ConfidenceBadge.tsx              (role=status + aria-labels)
ui/src/pages/BotActivityProfiler.tsx                      (useFetch migration)
ui/src/pages/GlobalHeatmap.tsx                            (useFetch, ErrorState, inline styles removed)
ui/src/pages/Narratives.tsx                               (useFetch + theme tokens)
ui/src/pages/Propaganda.tsx                               (useFetch migration)
ui/src/pages/PublicSentiment.tsx                          (5 subcomponents extracted)
ui/src/pages/Review.tsx                                   (ReviewItemCard extracted)
ui/src/pages/publicSentiment/ClassificationSampleCard.tsx (new)
ui/src/pages/publicSentiment/MiniDonut.tsx                (new)
ui/src/pages/publicSentiment/SentimentDistributionCard.tsx (new)
ui/src/pages/publicSentiment/SentimentOverviewHeader.tsx  (new)
ui/src/pages/publicSentiment/TopicRow.tsx                 (new)
ui/src/pages/review/ReviewItemCard.tsx                    (new)
```

## Deferred

- UI unit tests — audit flagged; separate walkthrough.
- `.eyebrow` semantic markup — cross-page design decision.
- Code-splitting (Vite warned 715 kB bundle) — bigger perf pass.
