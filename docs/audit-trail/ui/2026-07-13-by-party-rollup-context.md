# 2026-07-13 — Ground the by-party propaganda bars in their sample

The "By party · tracked officials" section under the technique bars on the Propaganda page (`ui/src/pages/propaganda/TechniqueExplorer.tsx`, `ByPartySection`) now carries its own context instead of a bare percentage race. Each row shows the raw sample inline ("6 of 34 posts · 1 official"), bars sit on an absolute 0-100% scale, a visible sub-line states that the rate is an overall any-technique flag rate, and parties below a 30-scored-post floor get a low-sample caveat. The backend rollup it renders is unchanged (see [analysis/2026-07-11-propaganda-by-party.md](../analysis/2026-07-11-propaganda-by-party.md)).

## What shipped

- Bars width = `flagged_rate_pct` directly (0-100% scale). Previously widths were normalized to the leading party's rate, so a 13.3% vs 10.9% split rendered as full-bar vs ~82% — visually exaggerating a gap inside the noise of a tiny sample.
- Raw counts moved from the hover tooltip into the visible row meta: "N of M posts · k officials". The tooltip now carries only the mean score.
- A visible explainer line under the section title: the number is the share of each party's officials' scored posts flagged for *any* technique — not a breakdown of the technique bars above it. The card subtitle in `TechniqueExplorer` was reworded to match (it previously promised "which party leans hardest on these techniques").
- `LOW_SAMPLE_DOCS = 30`: any party whose windowed scored-post total is below the floor gets a footnote ("Low sample: <party> has only N scored posts — one or two posts can move the rate.") rather than silently implying a stable rate.

## Why

- In the 7d production window the Democratic bar was 2 flagged posts out of 15 across 3 officials, shown as a headline "13.3%" beating the Republican "10.9%" — arithmetically correct, statistically meaningless, and the counts were hover-only so screenshots carried none of the context.
- The section also sits inside the "Techniques being used" card while measuring a different thing on a different denominator (officials' own flag rate vs share-of-flagged-posts sitewide); the copy has to say so on screen, per the labeling-discipline rules in `.agent/rules/media-analysis.md`.

## Follow-ups

- None.
