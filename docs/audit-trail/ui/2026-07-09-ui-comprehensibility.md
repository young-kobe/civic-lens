# 2026-07-09 — UI comprehensibility pass

A full four-agent audit of `ui/src` (~85 findings) surfaced labels, metrics, and terms that a
casual observer could not read without insider context, plus several that overstated what the
data supports. This pass makes every user-visible label self-explanatory or backed by a
discoverable in-UI explanation, and hardens the labeling conventions so the dashboard reads as a
sample — never a poll, a verdict, or an origin claim. Wording and small presentational changes
only; no component restructuring. Cross-links the analysis entry
[`analysis/2026-07-09-flagged-posts-field-rename.md`](../analysis/2026-07-09-flagged-posts-field-rename.md)
for the one item that reached into the aggregator.

## What shipped

### Labeling conventions now enforced across pages

- **Net tone in points.** New `formatPts()` helper (`services/format.ts`) renders the signed
  -100..+100 net-tone quantity as `"+12 pts"` everywhere it appears (tone ticker, tier rows,
  entity cards, narrative cards/modals, movers, topic divergence). A bare `%` read as a share and
  collided with the confidence/rate percents on the same cards; net tone is a difference of
  shares, i.e. percentage *points*. `formatPct` is unchanged and still owns true percentages.
- **"First seen in our sample," never "originated."** Every "originated" / "first surfaced"
  string on Narratives and Home now says we first *saw* the claim; card origin lines and the
  ticker legend spell out that "first seen" = the earliest post we collected, which may post-date
  where the claim actually started.
- **Suspected / flagged hedges.** Bot page: "Automation Rate" -> "Suspected automation",
  "Bot rate" -> "Suspected bot rate", "Top Amplified" -> "Most repeated", entity/byline copy says
  "our detector flags as likely automated." Narrative amplification "Primary Targets" ->
  "Who this narrative targets" with a neutral badge tone.
- **Ticker legends on every data page.** A `MethodPopover` now rides the `GlobalTicker` `legend`
  slot on Tone (pre-existing), Narratives, Propaganda, and Bots, defining that page's headline
  numbers in place. Long-form `CollapsibleInfo` blocks stay as the bottom-of-page backup.
- **Scales are stated.** Propaganda mean score renders `"0.62 / 1"` with a shared tooltip;
  tier-row axes carry faint endpoint labels (defaulting to `-100`/`+100` on tone axes, `0%`/`100%`
  passed by rate callers); confidence percents carry `title="Model confidence in this label"`.

### Data-level corrections

- `totalFlaggedAccounts` counted posts, not accounts — renamed to `totalFlaggedPosts` end to end
  (see the analysis entry). The Bot ticker/card labels that already said "posts" are now honest.
- Bot coordination stat relabeled "Suspected accounts posting more than once" and rendered as a
  percentage (the metric is a 0-1 share, not a count).
- Review queue favorability rows now show the stance target ("stance toward GOP: …") in the
  model-output line and the correction dropdown, verified against `overall_gop_stance` in
  `llm/schemas.py` so a reviewer cannot grade against the wrong entity.
- Topic tab "All Topics" count relabeled "N topic-matched" with a tooltip, since it sums
  per-topic volumes and so omits posts that match no topic keyword — reconciling it with the
  ticker's "Posts scored" total.
- Reddit added to the corpus explainers on Home and Tone (it was in the live pipeline but omitted
  from the copy).

### Removed / de-jargoned

- Deleted the unconditional "LIVE" badge (`App.tsx`) on a snapshot-cached product; the mobile
  mini-strip now shows the freshness text instead of a pulsing dot.
- Partisan lean given its own CSS token pair (`--lean-left` / `--lean-right`) so a right-leaning
  chip/border is no longer the same red that means "negative tone."
- Jargon swept from user-visible copy: "docs" -> "posts/articles", "catch-all" -> "sources we
  don't track individually", "evidence span" -> "verbatim quote", "ingested" -> "collected",
  "Clear filters" -> "Reset to 7 days", ungrammatical "As of last 7 days" -> "Last 7 days".
- Dead components removed (imported by no page): `charts/Heatmap`, `charts/SentimentBar`,
  `charts/TrendStrip`, `publicSentiment/MiniDonut`, `publicSentiment/SentimentDistributionCard`,
  and `common/ClassificationSampleCard` (orphaned once SentimentDistributionCard went), with the
  `charts` and `common` barrels cleaned up. `Sparkline` is kept (used by Narratives).

## Why

- The audit found the dashboard was legible to its author but not to a first-time reader: the same
  quantity rendered in two units, "lean" meant two different things on one card, definitive verbs
  ("originated", "LIVE", "Amplified") overstated the data, and buried methodology never surfaced.
- The project invariants (sample not poll; leads not verdicts; "first seen" = first-ingested-by-us)
  were correct in the code and the long-form copy but leaked in the glanceable labels.

## Follow-ups

- `docs/todos/ui-rework.md` still tracks API windowing for the Bot page and the posting-cadence
  heatmap re-introduction — untouched here.
- `typecheck` and `build` both pass.
