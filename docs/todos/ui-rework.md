# UI rework: correctness fixes + readability pass

Plan for the findings in `docs/audit-trail/ui/2026-07-09-adversarial-review.md` (U-N correctness, R-N readability). Three phases: A = surgical correctness fixes (no redesign), B = Bot Detector rework (the one page whose problems are structural), C = site-wide number-literacy pass. Distinct from `ui-consistency-audit.md` (mobile/refactor debt) — cross-referenced where they touch.

Preserve throughout (these tested well): the plain-English banner pattern, TierRow verb phrasing ("clearly negative · 812 posts"), honest per-column empty states, formatPct fallback-not-clamp policy, and the SupportingDocsTable tone+confidence+link row shape. Any redesigned surface should converge on these patterns, not invent new ones.

## Phase A — correctness fixes (small PRs, no visual redesign)

- [x] **U-2a: delete the fabricated static notes.** Both hardcoded conclusions removed. The account-age note is now data-derived (renders the youngest bucket's actual share, or nothing); the heatmap note is gone with the heatmap.
- [x] **U-6: stop rendering hardcoded "medium" confidence as a trust signal.** Removed from the sentiment ticker, the header meta, and entity-card stamps (`sentimentStats`). Backend recompute stays a tracked follow-up:
  - [ ] compute real coverage/confidence in the sentiment aggregator (`sentiment.py:235`), then restore the UI surfaces.
- [x] **U-4: fix posts-vs-accounts labels.** Ticker hint/label + metric-card subtitle + flagged-count caption relabeled "accounts" -> "posts" (honest minimum; real account counts not plumbed).
- [x] **U-5: stop showing one metric as two.** Dropped the "Burst timing similarity" row from the UI and removed `burstTimingSimilarity` from `bot.py`, `aggregator_models.py` (dataclass + `to_dict`), `types.ts`, and `fixtures.ts`.
- [x] **U-3: MetricCard renders subtitle without delta.** Subtitle now renders in a delta-less branch too; restores the Coordination Index scale note.
- [x] **U-8: show MIXED as Mixed.** Added a `mixed` bucket to `SupportingDocsTable` + the `SupportingDoc` type; no longer folded into Neutral.
- [x] **U-7: Review queue skip/refetch.** Skipped ids tracked in a set and filtered from every (re)fetched page (cleared on task/confidence change); LoadingCard renders only when `!current`.
- [x] **U-9: cross-tier chips from the actual breakdown.** Chips derive from `source_breakdown` groups actually present (dropped the unconditional origin-tier fallback); subtitle softened to the real ">=2 of three groups" criterion. Known limit: x_post can't be split officials-vs-public from the breakdown (noted in code).
- [x] **U-10: SourceBar denominators + date parsing.** Widths normalize by the sum of `source_breakdown` counts; date-only strings parse as local dates in `SupportingDocsTable`.
- [x] **R-7: Home copy fixes.** Dropped "unbiased" from the hero; replaced the "Favorability scores per political figure" bullet with a GOP-stance description matching what ships.

## Phase B — Bot Detector rework (structural)

The page fabricates, mislabels, and ignores its filters (U-1, U-2, U-4, U-5, R-5); Phase A stops the bleeding, this phase makes the page worth keeping.

- [x] **U-1: window honesty decision — chose (b).** Bot Detector takes no `filters` prop, hides the GlobalFilters window pills (App passes `windowScoped={false}`, which renders a "Full sample · not time-windowed" note), and the eyebrow now reads "Full sample · all collected data, not time-windowed". Backend windowing stays a tracked follow-up:
  - [ ] add a `window` param to `/api/bot-activity` + cutoff in `get_bot_activity()`, then re-enable the pills on this tab (option (a), the better product).
- [x] **Heatmap: dropped.** Backend emits `day: 0` for all rows and buckets by server-local hour, so the viz + its shade legend were fabricated; both removed from the UI. Backend cadence payload left in place. Follow-up:
  - [ ] emit real (day, hour) UTC buckets in `bot.py`, then re-introduce the heatmap.
- [x] **R-5: de-editorialize bylines.** "where bot amplification actually lives" / "should skew near 0%" replaced with measured rate descriptions; "Coordination Idx" ticker label renamed "Coordination" with a "0-1 scale" hint, and the MetricCard subtitle (U-3) now carries the full scale note.
- [x] **Metric provenance pass.** U-4/U-5 relabels plus the data-derived account-age caption; every remaining number on the page is captioned by what it counts (posts / accounts / share-of-sample).
- [x] Re-ran the review's Bot Detector findings (U-1, U-2, U-4, U-5, R-5) as an acceptance checklist — all closed UI-side.

## Phase C — number-literacy + vocabulary pass (site-wide)

- [x] **R-1: GlobalTicker legend.** Added an optional `legend` slot to `GlobalTicker` (rendered inline before the timestamp) and wired a `MethodPopover` on the Overall Tone ticker defining net tone (-100..+100, share positive minus share negative) and GOP stance (net stance of sampled posts toward GOP entities). Reusable on any ticker.
- [x] **R-2: topic-filter scope markers.** When a topic filter is active, the ticker's tone/stance/posts items and the "Tone intensity" mini are stamped "all topics" — no unmarked global numbers inside a filtered view.
- [ ] **R-3: Narratives vocabulary unification.** DONE UI-side: "tiers" -> "groups" in user-facing chips/titles, doc count unified to "posts", "prop 0.42" -> "Propaganda 0.42 / 1", "cross-tier" -> "Cross-group". LEFT OPEN — narrative-level confidence chip: `NarrativeSummary` carries no confidence field, and averaging only the top-N `top_supporting_docs` would misrepresent the full cluster. Needs a backend mean-row-confidence field before it can ship honestly:
  - [ ] expose mean supporting-row confidence per narrative in the aggregator, then add the chip.
- [x] **R-4: Propaganda example rows.** Friendly source labels (News · domain / X · @handle / Reddit · r/...), visible per-technique confidence (was a hover-only `title`), and the "doc #NNNN" id dropped. (`formatScore` from `ui-consistency-audit.md` doesn't exist yet; used the existing `formatPct`.)
- [x] **R-6: window-honesty indicator.** Satisfied by U-1: Bot Detector visibly opts out ("Full sample · not time-windowed" in both the filter bar and the eyebrow); every other tab honors the pills.
- [x] **R-8/R-9: color-only encodings + raw enums.** SourceBar renders a visible text legend on the compact card (colors no longer the only key); ClassificationSampleCard shows friendly source-type labels and its "DOC #" footer was dropped (ids remain only in admin Review). Heatmap shade legend removed with the heatmap.

## Exit criteria

`npm run typecheck` + build green per PR; each phase gets an audit-trail entry under `ui/` naming the U-N/R-N ids closed (Phase B likely also one under `analysis/` for the bot-aggregator changes); a final pass re-reads the 2026-07-09 review doc and confirms every finding is either fixed or consciously waived in writing. When every box is ticked, delete this file.
