# 2026-07-09 — UI remediation: correctness fixes + number-literacy pass

Closes the correctness (U-N) and readability (R-N) findings from the same-day
adversarial review (`2026-07-09-adversarial-review.md`). Three fronts: surgical
honesty fixes across the data pages, a structural rework of the Bot Detector
(the review's worst offender), and a site-wide vocabulary / scale-literacy pass.
The system now states its sample scope honestly on every surface, never renders
a hardcoded conclusion as if it were data, and gives a first-time reader an
on-surface key for the signed percentages and 0-1 scores it shows.

## What shipped

### Correctness (Phase A)

- **U-2a — no fabricated conclusions.** `BotActivityProfiler.tsx`: deleted the
  hardcoded "Concentrated activity during off-hours…" and "Young accounts…
  over-represented" notes. The account-age card now derives its caption from the
  youngest bucket's actual share, or renders nothing.
- **U-6 — no hardcoded trust signal.** The sentiment aggregator hardcodes
  `confidence="medium"`; it is no longer surfaced. Removed from the Overall Tone
  ticker, the TopMetrics header meta, and the per-entity card stamps
  (`sentimentStats` in `EntityProfileCard.tsx`, which no longer takes/renders
  `confidence`). Bot confidence is left in place — it is sample-size-derived, not
  hardcoded.
- **U-4 — posts, not accounts.** Bot Detector ticker hint/label, the automation
  metric-card subtitle, and the flagged-count caption relabeled to "posts" (the
  underlying figures are per-doc).
- **U-5 — one metric shown once.** Removed the duplicated `burstTimingSimilarity`
  (it aliased `coordination_index`) from the UI row and from `bot.py`,
  `aggregator_models.py` (dataclass + `to_dict`), `types.ts`, and `fixtures.ts`.
- **U-3 — `MetricCard` renders its subtitle** in a delta-less branch, restoring
  the Coordination Index scale note.
- **U-8 — MIXED renders as "Mixed"** via a new bucket in `SupportingDocsTable`
  and the `SupportingDoc` type, instead of folding into Neutral.
- **U-7 — Review queue skips stick.** Skipped ai_output ids are tracked in a set
  and filtered from every (re)fetched page (reset on task/confidence change);
  `LoadingCard` renders only when there is no current item.
- **U-9 — cross-group chips match the rule.** Chips derive from the
  `source_breakdown` groups actually present; the panel subtitle now states the
  real ">=2 of three groups" criterion instead of "all talking about them".
- **U-10 — SourceBar denominators + dates.** Segment widths normalize by the sum
  of `source_breakdown` counts (not the mismatched `supporting_doc_count`);
  date-only strings parse as local dates so relative labels don't drift a day.
- **R-7 — Home copy.** Dropped "unbiased" from the hero (a prohibited universal
  claim); the favorability bullet now describes the GOP-stance surface that ships.

### Bot Detector rework (Phase B)

- **U-1 — window honesty (option b).** The tab serves the full, un-windowed
  sample, so it takes no `filters` prop, hides the global window pills
  (`GlobalFilters` gained a `windowScoped` prop; App passes `false` for `bots`,
  rendering "Full sample · not time-windowed"), and its eyebrow states the same.
- **Heatmap dropped.** The backend emitted `day: 0` for every row and bucketed by
  server-local hour while the legend claimed per-day UTC — a fabricated viz. The
  card and its shade legend are gone; the cadence payload is left untouched.
- **R-5 — measured bylines.** "where bot amplification actually lives" / "should
  skew near 0%" replaced with rate descriptions; "Coordination Idx" renamed
  "Coordination" (0-1 scale hint), with the full scale note on the MetricCard.

### Number-literacy pass (Phase C)

- **R-1 — ticker legend.** `GlobalTicker` gained a `legend` slot; the Overall Tone
  ticker uses a `MethodPopover` defining net tone and GOP stance.
- **R-2 — topic scope markers.** Inside an active topic filter, the ticker's
  tone/stance/posts items and the Tone-intensity mini are stamped "all topics".
- **R-3 — Narratives vocabulary.** "tiers" -> "groups", doc count unified to
  "posts", "prop 0.42" -> "Propaganda 0.42 / 1", "cross-tier" -> "Cross-group".
- **R-4 — Propaganda example rows.** Friendly source labels, visible per-technique
  confidence (was hover-only), "doc #NNNN" ids removed.
- **R-6 — window honesty per tab.** Satisfied by U-1: Bot visibly opts out, all
  other tabs honor the pills.
- **R-8 / R-9 — encodings + enums.** SourceBar shows a visible text legend on the
  compact card; `ClassificationSampleCard` shows friendly source-type labels and
  dropped its "DOC #" footer (ids stay in admin Review only).

Preserved patterns (validated by the review): plain-English banner, TierRow verb
phrasing, honest per-column empty states, `formatPct` fallback-not-clamp, and the
`SupportingDocsTable` row shape. Redesigned surfaces converged onto these.

## Why

The review found the Bot Detector fabricating conclusions in static copy,
mislabeling posts as accounts, double-counting one metric, misdescribing its
heatmap axes, and relabeling an un-windowed sample with whatever window pill was
clicked — direct violations of the no-fabrication and sample-labeling invariants.
Elsewhere the numbers were honest but unreadable: signed percentages, 0-1 scores,
and raw enums handed to non-technical readers with no on-surface key.

## Verification

`npm run typecheck` and `npm run build` both green. Python files parse; no
remaining `burstTimingSimilarity` references; no test referenced the removed
field. No UI unit tests exist.

## Follow-ups (deferred to backend, tracked in `docs/todos/ui-rework.md`)

- Window `/api/bot-activity` (`get_bot_activity()` cutoff), then re-enable the Bot
  Detector window pills — option (a), the better product.
- Emit real `(day, hour)` UTC cadence buckets in `bot.py`, then re-introduce the
  posting-cadence heatmap.
- Compute real coverage/confidence in the sentiment aggregator (`sentiment.py`),
  then restore the confidence surfaces removed for U-6.
- Expose per-narrative mean supporting-row confidence in the aggregator, then add
  the narrative-level confidence chip (R-3's one open sub-item — cannot be done
  honestly UI-side from the truncated `top_supporting_docs` sample).
