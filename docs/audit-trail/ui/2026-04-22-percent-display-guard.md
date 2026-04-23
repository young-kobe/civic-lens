# 2026-04-22 — Defensive percent-display guard across the UI

Every user-facing percentage now renders through a single `formatPct()` helper in `ui/src/services/format.ts`. Values outside the declared range are clamped before display and logged with a dev-mode console warning, so a future aggregator bug can't leak a value like "197% of flagged posts" to a reader again.

## What shipped

### `ui/src/services/format.ts` (new)

```ts
export function formatPct(
    value: number | null | undefined,
    opts?: { decimals?: number; min?: number; max?: number; signed?: boolean; fallback?: string },
): string
```

Behavior:

- `null` / `undefined` / non-finite → `"—"` (configurable via `fallback`).
- Clamps to `[min, max]` before formatting. Defaults to `[0, 100]`; pass `{ min: -100 }` for signed measures like net sentiment.
- `signed: true` prefixes `+` on positive values (for diff-style display).
- When clamping activates in development, prints a `console.warn` naming the value and the clamped result, so the backend bug surfaces to whichever engineer is in DevTools.

Also exports `clampWidthPct(value)` — returns a plain 0-100 number for piping into `style={{ width: \`${x}%\` }}` where a "—" would break layout.

### Migrated sites

Every user-facing rate/share display now routes through `formatPct`:

- `ui/src/pages/Propaganda.tsx` — flagged rate, per-tier flagged rates, top technique %, entity modal stats, news-vs-social split rates, technique-row % of flagged.
- `ui/src/pages/BotActivityProfiler.tsx` — bot-rate stats, automation-rate ticker + metric card, burst-timing similarity, account-age distribution %, link-domain concentration %, similarity-bar labels, and the reads-as-today automation-rate sentences.
- `ui/src/pages/Narratives.tsx` — net-sentiment metrics on cards, modal, entity stats (signed, range `[-100, 100]`).
- `ui/src/pages/PublicSentiment.tsx` — tier-row net, GOP net favorability, entity modal net score, ticker overall-tone + GOP stance, online-vs-polling comparison %s.
- `ui/src/pages/publicSentiment/SentimentDistributionCard.tsx` — skew/polarization chips, reads-as copy, intense/measured headlines. Local `fracPct(value, total)` kept for the "share of segments" case since its shape is share-out-of-N and now routes through `formatPct` internally.
- `ui/src/pages/publicSentiment/TopicDivergencePanel.tsx` — tier dot titles and tooltip text.
- `ui/src/pages/Review.tsx` — reviewed %, accuracy %.
- `ui/src/components/common/EntityProfileCard.tsx` — "How they lean" stat (signed).
- `ui/src/components/common/ClassificationSampleCard.tsx` — model confidence label, aria, and readout.

Dead-code cleanup along the way: four unused `const sign = x >= 0 ? '+' : ''` declarations removed from `PublicSentiment.tsx` and `Narratives.tsx` (formatPct now handles the sign internally when `signed: true`).

### Out of scope (intentionally)

- CSS width expressions like `style={{ width: \`${x}%\` }}` still read the raw number. A clamped "—" would break layout; callers that might receive out-of-range values can use `clampWidthPct()`, but none of the current sites have shown a problem and forcing the sweep now would be churn for its own sake.

## Why

User feedback: *"we need to guard against erroneous percentages like this everywhere before displaying on ui."*

Context: the 197% on `pct_of_flagged_docs` was a real backend bug (multiple evidence spans per doc inflated the counter), already fixed at the source in `analysis/2026-04-22-propaganda-technique-pct-dedup.md`. The UI-side guard is the belt on top of those suspenders — it makes the class of bug visible-but-non-fatal the next time anything similar slips through, so a user's first encounter is never "the app shows nonsense" but "the number is capped at 100% and we see the warning in DevTools."

The guard is a pure presentation layer — it does not, and cannot, reconstruct the *correct* value from a corrupted backend field. When the underlying `count` is inflated, clamping the rendered % hides the magnitude of the bug but the correct number only comes back once the backend regenerates the affected cache (in this case, re-running `analyze -Tasks propaganda,snapshots` with the fixed aggregator).

## Follow-ups

- `clampWidthPct` is exported but currently unused. If a future CSS-width site ever receives a buggy >100 value, switch it to that helper rather than inline math.
