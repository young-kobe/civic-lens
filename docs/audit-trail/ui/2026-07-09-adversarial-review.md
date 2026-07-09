# 2026-07-09 — Adversarial review: UI layer (correctness + readability)

Point-in-time adversarial review of `ui/src/` with two mandates: code correctness against the backend contract, and whether a first-time non-technical reader can actually understand what each surface shows (invariant C1 and the media-analysis labeling rules). Findings only — a UI rework will be planned separately. Method: static read of all pages/components/services plus the backend aggregator models; HIGH findings independently re-verified. Layout/visual conjectures that require a running browser are marked SUSPECTED. Companion entries: `../ingestion/2026-07-09-adversarial-review.md`, `../analysis/2026-07-09-adversarial-review.md`, `../ingestion/2026-07-09-adversarial-review-data-layer.md`.

## Part 1 — Correctness findings

### U-1 HIGH CONFIRMED — Bot Detector relabels unwindowed data with the selected window

`ui/src/pages/BotActivityProfiler.tsx:613-617` stamps the page with `asOfTodayEyebrow(filters.timeRange)` ("As of last 24 hours"), and the global window pills render on the tab — but `fetchBotActivity()` (`services/api.ts:106`) takes no window parameter and `get_bot_activity()` (`aggregators/bot.py:61`) applies no time cutoff. Clicking "24 hours" relabels the same all-sample numbers as 24-hour numbers: a false sample-scope claim, the exact thing the labeling rules exist to prevent.

### U-2 HIGH CONFIRMED — Bot Detector renders fabricated findings as static copy

`BotActivityProfiler.tsx:541-544`: the card note "Concentrated activity during off-hours (02:00-05:00 UTC) was detected in this window." is a hardcoded string rendered unconditionally, whatever the data shows. Same at line 486: "Young accounts (<30 days) are over-represented in suspected bot activity." Additionally the heatmap legend claims "Rows are days of the week, columns are hours (0-23, UTC)" while the backend emits every cadence point with `day: 0` (all volume renders in the "Sun" row; "Peak: Sun 14:00") and buckets by server-local hour, not UTC (`bot.py:147,356-358`). Direct no-fabrication violations.

### U-3 MEDIUM CONFIRMED — MetricCard drops subtitles when there is no delta

`ui/src/components/common/MetricCard.tsx:88-95`: `subtitle` renders only inside the `delta !== undefined` block. Bot overview passes `subtitle="0 = none, 1 = highly coordinated"` with no delta — the Coordination Index displays as a bare "0.34" with its only scale explanation silently dropped.

### U-4 MEDIUM CONFIRMED — Posts labeled as accounts across the Bot Detector

`BotActivityProfiler.tsx:158-172` vs `bot.py:349,366`: `automation_rate` and flagged counts are per-doc numbers, but the UI captions them "of analyzed accounts" / "Flagged Accounts" (ticker + metric cards).

### U-5 MEDIUM CONFIRMED — One metric presented as two independent signals

`bot.py:351,374` assigns `burstTimingSimilarity` the same value as `coordination_index`; the UI shows "Coordination Idx 0.34" and "Burst timing similarity 34%" as separate indicators — a reader counts one signal twice.

### U-6 MEDIUM CONFIRMED — Hardcoded "medium confidence" rendered as a live trust signal

`aggregators/sentiment.py:235` hardcodes `coverage="medium", confidence="medium"`; `PublicSentiment.tsx:113-114,588` and the ticker display it as if computed ("Confidence medium", "· medium confidence", stamped on every entity card). The most prominent trust indicator on the site never changes.

### U-7 MEDIUM CONFIRMED — Review queue: skipped items resurface; loading and card render together

`ui/src/pages/Review.tsx:115-130`: `skip` is a local `slice(1)`, but the background refetch replaces the queue wholesale, so skipped items reappear; during refetch `loading` and `current` are both truthy and `<LoadingCard/>` + `<ReviewItemCard/>` render simultaneously (lines 205, 212). Admin-only surface, so severity stays MEDIUM.

### U-8 LOW CONFIRMED — MIXED sentiment labels silently render as "Neutral"

`ui/src/components/common/SupportingDocsTable.tsx:113-115` maps MIXED into the Neutral bucket — the table shows a label the model did not produce, next to that model's confidence.

### U-9 LOW CONFIRMED — Cross-tier chips can contradict the panel's own inclusion rule

`ui/src/pages/Narratives.tsx:563-578,607`: tier chips derive from `first_seen_tier_group` alone, so a story repeated by officials AND public shows only one chip, inside a panel whose subtitle says "the news, officials, and the public are all talking about them" (also true for 2-of-3 stories).

### U-10 LOW SUSPECTED — SourceBar widths and relative-date labels

`Narratives.tsx:122-151` divides `source_breakdown` counts by `supporting_doc_count` from a different query (segments may not sum to 100%); `SupportingDocsTable.tsx:127` `Date.parse` on date-only strings resolves UTC midnight, so "1 day ago" can be off by one near local midnight.

Checked and clean: admin gating leaks nothing to non-admins (render-gated, token scrubbed from URL, data server-side gated); `formatPct` fallback-not-clamp policy applied consistently; loading/empty/error states distinguished on the data pages.

## Part 2 — Readability findings (first-time non-technical reader)

- R-1 HIGH — GlobalTicker numbers are uninterpretable: "Overall tone +4.2%", "GOP stance -18.0%" — percent of what? The net-tone construction ((pos-neg)/total) is explained nowhere near the number, and "GOP stance" never says whose stance toward whom. This is the first number every visitor sees.
- R-2 MED-HIGH — Topic-filter honesty gap on PublicSentiment: with a topic active, the header counts topic posts, but "Tone intensity" and the ticker stay global with no marker (entity cards got a "scores are global" byline; these surfaces did not). A reader inside a topic reads global numbers as topic numbers.
- R-3 MED — Narratives leaks internals: chips like "prop 0.42", "bot-pushed 37%", unexplained 0-1 scales, "tiers" vs "groups" vocabulary split, the same count named "docs" / "posts" / "Supporting docs" on three surfaces; no confidence shown at narrative level (only the drill-down table is C1-clean).
- R-4 MED — Propaganda example rows render raw enums and ids ("x_post · unknown", "doc #4821") and confidence only in a hover `title` — invisible on touch, so per-flag predictions ship without visible confidence.
- R-5 MED — Bot Detector bylines editorialize ("where bot amplification actually lives", "should skew near 0%") — prescriptive claims, not measurements; "Coordination Idx" + "0-1" hint is jargon compressed past comprehension.
- R-6 MED — Windows behave differently per tab with no signal: Tone/Narratives/Propaganda refetch per window; Bot ignores it (U-1). Nothing tells the reader which tabs honor the pill.
- R-7 MED — Home overclaims: hero "See the shape of political media, unbiased." makes exactly the universal claim the media-analysis rules prohibit, and the "Favorability scores per political figure" bullet describes a surface that does not exist (only GOP-party stance ships).
- R-8 LOW-MED — Color-only encodings: SourceBar segment counts live in hover titles (no touch access, no legend); heatmap quartile legend is shade-only.
- R-9 LOW — ClassificationSampleCard (otherwise the best C1 surface: confidence bar + evidence spans + "View Original") shows raw enums ("X_POST") and "DOC #123" to end users.

Worth preserving in any rework: the plain-English banner pattern ("Most posts we scanned look like real people — only about 3% look automated."), TierRow verb phrasing ("clearly negative · 812 posts"), honest per-column empty states, the formatPct guard policy, and the SupportingDocsTable tone+confidence+link row shape.

## Part 3 — Rework candidates (to be planned, not designed here)

1. Bot Detector behavioral-signals block — densest cluster of fabricated/misleading claims in the app (U-1, U-2, U-4, U-5, R-5).
2. Bot Detector window handling — window the API or remove the pills/eyebrow from the tab.
3. GlobalTicker number literacy — one shared affordance explaining net tone / stance percentages, reused everywhere.
4. Sentiment confidence/coverage plumbing — compute it or stop rendering it (U-6).
5. Narratives vocabulary pass — unify docs/posts/cites and tiers/groups; labeled scales; narrative-level confidence treatment (R-3).
6. Propaganda example rows — align to the SupportingDocsTable pattern (friendly source labels, visible confidence, no raw enums/ids) (R-4).

## Overall

The honesty scaffolding is real: percent guards, evidence links, empty-state discipline, and plain-language banners are consistently applied, and the historically worst bug classes (percent overflow, missing source links) are genuinely fixed. The Bot Detector is the outlier — it fabricates conclusions in static copy, mislabels posts as accounts, double-counts a metric, misdescribes its heatmap axes, and ignores the window filter it displays — and should lead any rework. Elsewhere the defects are vocabulary and scale-literacy: the numbers are honest, but the reader is handed signed percentages, 0-1 scores, and internal enums with no on-surface key.
