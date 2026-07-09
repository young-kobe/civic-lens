# UI rework: correctness fixes + readability pass

Plan for the findings in `docs/audit-trail/ui/2026-07-09-adversarial-review.md` (U-N correctness, R-N readability). Three phases: A = surgical correctness fixes (no redesign), B = Bot Detector rework (the one page whose problems are structural), C = site-wide number-literacy pass. Distinct from `ui-consistency-audit.md` (mobile/refactor debt) — cross-referenced where they touch.

Preserve throughout (these tested well): the plain-English banner pattern, TierRow verb phrasing ("clearly negative · 812 posts"), honest per-column empty states, formatPct fallback-not-clamp policy, and the SupportingDocsTable tone+confidence+link row shape. Any redesigned surface should converge on these patterns, not invent new ones.

## Phase A — correctness fixes (small PRs, no visual redesign)

- [ ] **U-2a: delete the fabricated static notes.** `BotActivityProfiler.tsx:541-544` ("Concentrated activity ... was detected in this window.") and `:486` ("Young accounts ... over-represented"). Replace with nothing, or with copy derived from the actual data — never a hardcoded conclusion. This is the single worst honesty defect in the UI; it can ship alone, today.
- [ ] **U-6: stop rendering hardcoded "medium" confidence as a trust signal.** Until `coverage`/`confidence` are computed (`sentiment.py:235` hardcodes both), remove them from the ticker, the header meta, and entity-card stamps. Backend follow-up (compute from sample size + mean row confidence) can re-introduce them honestly — track that as a checkbox here so the removal isn't forgotten: 
  - [ ] compute real coverage/confidence in the sentiment aggregator, then restore the UI surfaces.
- [ ] **U-4: fix posts-vs-accounts labels.** `BotActivityProfiler.tsx:158-172` — captions say "accounts" over doc counts. Relabel to "posts" (or plumb real account counts; relabel is the honest minimum).
- [ ] **U-5: stop showing one metric as two.** `bot.py:351,374` assigns `coordination_index` to `burstTimingSimilarity`. Drop the duplicate row from the UI and the duplicated field from the payload.
- [ ] **U-3: MetricCard renders subtitle without delta.** `MetricCard.tsx:88-95` — move `subtitle` out of the `delta !== undefined` block. Restores the Coordination Index scale note.
- [ ] **U-8: show MIXED as Mixed.** `SupportingDocsTable.tsx:113-115` — add the missing bucket instead of silently mapping to Neutral.
- [ ] **U-7: Review queue skip/refetch.** `Review.tsx:115-130` — track skipped ids and filter them from refetched queues; render LoadingCard only when `!current`.
- [ ] **U-9: cross-tier chips from the actual breakdown.** `Narratives.tsx:563-578` — derive chips from `source_breakdown` tier groups present, not `first_seen_tier_group` alone; soften the "all talking about them" subtitle to match the real >=2-group criterion.
- [ ] **U-10: SourceBar denominators + date parsing.** Normalize segment widths by the sum of `source_breakdown` counts (not `supporting_doc_count`); parse date-only strings as local dates in `SupportingDocsTable.tsx:127`.
- [ ] **R-7: Home copy fixes.** Drop "unbiased" from the hero (it is the exact universal claim the media-analysis rules prohibit); fix the "Favorability scores per political figure" bullet to describe the GOP-stance surface that actually ships.

## Phase B — Bot Detector rework (structural)

The page fabricates, mislabels, and ignores its filters (U-1, U-2, U-4, U-5, R-5); Phase A stops the bleeding, this phase makes the page worth keeping.

- [ ] **U-1: window honesty decision.** Either (a) add a `window` param to `/api/bot-activity` + cutoff in `get_bot_activity()` and honor the pills, or (b) suppress the GlobalFilters pills and the "As of <window>" eyebrow on this tab and label the page "full sample". Decide (a) vs (b) first — everything else in this phase depends on it. (a) is the better product; (b) is a one-day honesty patch.
- [ ] **Heatmap: fix or drop.** Backend emits `day: 0` for all rows and buckets by server-local hour while the legend claims per-day UTC (`bot.py:147,356-358`). Either emit real (day, hour) buckets in UTC and keep the viz, or drop the heatmap until the data supports it. No middle ground where the legend describes data that isn't there.
- [ ] **R-5: de-editorialize bylines.** Replace "where bot amplification actually lives" / "should skew near 0%" with measured descriptions; rename "Coordination Idx" to a plain-language label with an inline scale note (the MetricCard subtitle fix from Phase A gives it a home).
- [ ] **Metric provenance pass.** For each number on the page, trace payload field -> SQL -> meaning, and caption it with what it actually counts (posts, authors, share-of-sample). The U-4/U-5 fixes are the first two instances; finish the page.
- [ ] Re-run the review's Bot Detector findings as an acceptance checklist before closing this phase.

## Phase C — number-literacy + vocabulary pass (site-wide)

- [ ] **R-1: GlobalTicker legend.** One shared affordance (info popover or persistent sublabel) explaining net tone ("share positive minus share negative, -100..+100") and "GOP stance" (stance of sampled posts toward GOP entities), rendered wherever GlobalTicker appears. This is the first number every visitor sees; it must be self-explanatory.
- [ ] **R-2: topic-filter scope markers.** `PublicSentiment.tsx` — when a topic filter is active, either scope "Tone intensity" and the ticker to the topic or stamp them "all topics" the way entity cards already do. No unmarked global numbers inside a filtered view.
- [ ] **R-3: Narratives vocabulary unification.** One pass: "tiers" -> "groups" everywhere user-facing; one name for the doc count (pick "posts"); replace "prop 0.42" chips with labeled values ("Propaganda 0.42 / 1"); decide the narrative-level confidence treatment (mean row confidence chip is the cheapest C1-consistent option).
- [ ] **R-4: Propaganda example rows -> SupportingDocsTable pattern.** Friendly source labels (not "x_post · unknown"), visible confidence (not hover-only title attr), no "doc #4821" ids. Overlaps `ui-consistency-audit.md` formatter items — use its `formatScore` helper.
- [ ] **R-6: window-honesty indicator.** After the Phase B decision, ensure every tab either honors the window pills or visibly opts out — no silent ignoring.
- [ ] **R-8/R-9: color-only encodings + raw enums.** Text legend or on-card counts for SourceBar; friendly source-type labels in ClassificationSampleCard; drop "DOC #" footers from end-user surfaces (keep ids in admin Review only). Heatmap shade legend gets value ranges if the heatmap survives Phase B.

## Exit criteria

`npm run typecheck` + build green per PR; each phase gets an audit-trail entry under `ui/` naming the U-N/R-N ids closed (Phase B likely also one under `analysis/` for the bot-aggregator changes); a final pass re-reads the 2026-07-09 review doc and confirms every finding is either fixed or consciously waived in writing. When every box is ticked, delete this file.
