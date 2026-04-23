# Walkthrough 052: Backend source filter + label renames + intensity reframe

## Goal

Three tightly-related UX changes that shipped as one pass:

1. **Make the source filter actually work on Sentiment and Propaganda.**
   The `source` pill was rendering in the filter bar but never reached any
   fetch, so it was a dead control for users. Client-side workarounds
   landed in walkthrough 051 for Narratives; this pass wires the proper
   backend path so the overview metrics (flagged rate, mean propaganda
   score, net tone, total volume) honor the filter instead of silently
   returning the all-source aggregate.
2. **Rename the top-line metrics to match what the reader actually sees.**
   "Net Sentiment" carried polling-adjacent connotations that risked
   misreading; "Favorability" conflated the doc's stance toward a subject
   with the author's feelings. Renamed to **Overall Tone** and **Party
   Stance** respectively. Backend field names are unchanged to avoid
   cascading renames; only the UI labels shifted.
3. **Reframe the Distribution card around intensity.** The card was
   implicitly framed as "how many docs in each of 5 buckets". That
   surfaced detail at the expense of the takeaway. The new framing leads
   with the strong-vs-mild-vs-neutral split ("Intense / Measured /
   Neutral") as the primary stats, plus a plain-English "Reads as: ..."
   line that summarizes what the distribution implies.

## Backend: source filter

### New helper in `base.py`

```python
def source_filter_allowed(source_filter: str) -> Optional[FrozenSet[str]]:
    """Return allowed source_type values, or None when no filter applies."""
```

Returns `None` for `"all"` (or unknown values), letting callers
short-circuit the check. `"news"` → `{"news"}`, `"reddit"` →
`{reddit_post, reddit_comment}`, `"social"` → Reddit + `x_post`. Mirrors
the UI `Filters.sourceType` type one-for-one, keyed by strings so it can
live independently of either layer's enum constants.

### SentimentAggregator

`get_public_sentiment(time_window, bot_docs, source_filter="all")` now
threads the filter through the aggregation pass. Both the sentiment-row
loop and the favorability-row loop skip docs whose `source_type` isn't
in the allowed set. No new SQL — we filter at accumulation time, which
was already a single pass per task type.

Signatures changed:

- `_process_sentiment_data(rows, bot_docs, allowed_sources)`
- `_aggregate_sentiment_rows(rows, bot_docs, allowed_sources=None)`
- `_merge_favorability_data(result, fav_rows, bot_docs, allowed_sources=None)`
- `_parse_favorability_rows(fav_rows, bot_docs, allowed_sources=None)`

### PropagandaAggregator

`get_propaganda_overview(time_window, source_filter="all")` filters its
two fetched row sets (all rows + examples) in-memory post-fetch. The
SQL already selects `source_type`; we just drop rows that don't match.

### API routes (`analysis/src/api/routers/data.py`)

Both `/sentiment` and `/propaganda` now accept a `source` query param
(`SourceLiteral = "all" | "news" | "reddit" | "social"`). When `source
== "all"` we hit the snapshot cache as before; any other value bypasses
the cache and computes live. This is the right trade-off: the
unfiltered variant is what `job_runner` pre-builds on schedule (the
scheduler doesn't know about per-filter variants), and filtered
requests are cheap enough to compute live because the aggregator reads
rows already loaded into memory and just drops ones outside the filter.

Cache key intentionally doesn't include source — a 4× cache expansion
for an occasionally-used filter isn't worth the invalidation
bookkeeping.

## Frontend: UI wiring + cleanup

### `services/api.ts`

`fetchSentiment(window, source)` and `fetchPropaganda(window, source)`
now append `&source=` to the URL when source ≠ `"all"`. A new exported
`SourceFilter` type mirrors the backend literal.

### Pages

`PublicSentiment` and `Propaganda` pass `filters.sourceType` into their
fetch call and include it in both `useFetch` deps and the cache key
(`sentiment:7d:news`, etc.). `App.tsx`'s `showSourceType` guard relaxed:
the source pill now renders on every data page except Bot (where source
filtering isn't meaningful — bot detection is social-only by
definition).

### Dead code removed

- Propaganda had a client-side filter block (walkthrough 051 workaround)
  that re-filtered `by_source` and `examples` after fetch. Removed —
  the backend now returns the already-filtered data, so re-filtering
  client-side would be redundant and confusing.
- Narratives still does client-side filtering on
  `first_seen_source_type` because narrative clustering isn't
  re-aggregated per filter at the backend. Left alone; works correctly
  for that data shape.

## Label renames

UI-only. Backend field names (`gopFavorability`, `favorable`,
`unfavorable`, `netScore`, etc.) are untouched.

| Before                      | After                       | Where                              |
|-----------------------------|-----------------------------|------------------------------------|
| Net Sentiment               | Overall Tone                | Overview strip                     |
| GOP Favorability            | GOP Party Stance            | Card title                         |
| Net Favorability            | Net Stance                  | Hero metric inside GOP card        |
| Favorability Trend          | Stance Trend                | Trend section inside GOP card      |
| Polling vs Online Sentiment | Polling vs Online Stance    | Comparison section heading         |
| Online Sentiment            | Online Stance               | Badge in polling comparison        |
| sentiment / 'sentiment' bar | Uses `political` color scheme on GOP card | SentimentBar colorScheme prop in GOP contexts |

The GOP card's `SentimentBar` instances now use `colorScheme="political"`
(blue / grey / red) rather than the default sentiment scheme (green /
grey / red). Political-stance bars reading blue-vs-red matches the
convention; green-vs-red for a partisan stance read as an endorsement
cue.

## Distribution reframe

Card title: `Sentiment Distribution` → **`Tone Intensity`**.
Subtitle: `5-point intensity scale across N scored docs` →
**`How strongly N docs lean in each direction`**.

New primary stats row: three big inline numbers — **Intense / Measured
/ Neutral** — computed from the 5-bucket distribution:

- Intense = strongPositive + strongNegative
- Measured = mildPositive + mildNegative
- Neutral = neutral

"Intense" goes red when ≥ 30% (a polarized sample is a signal worth
highlighting). The existing chips (lean / polarized / confidence /
most-volume platform) remain, now acting as qualifiers on the big
numbers rather than the primary takeaway.

New **"Reads as: ..."** interpretive line — a single sentence,
picked at render time from the strongest signal in the data:

- `>= 45% intense` → "heavily polarized"
- `>= 30% intense` → "intense and leans {positive|negative}"
- `netPct <= -15` → "leans clearly unfavorable, mostly mild-negative"
- `netPct >= +15` → "leans clearly favorable, mostly mild-positive"
- else → "near-even tone with N% straight neutral reportage"

This is the template for the broader "reads as" treatment the user
asked for across the dashboard; subsequent passes can apply the same
pattern to SocialVsNewsCard, GOP stance, narratives, and topic rows.

## Verification

- `analysis.tests.test_rich_aggregators` — pass (8 tests).
- `analysis.tests.test_aggregation_confidence_filter` — pass (4 tests).
- `analysis.tests.test_propaganda` — pass (9 tests).
- `ui/ npm run typecheck` — clean.
- `ui/ npm run build` — clean. Bundle unchanged.

## Follow-ups in scope for subsequent passes

- **Topic divergence overlay on `TopicSentimentCard`** — color-code each
  topic row by the news-vs-social tone gap. Requires the aggregator to
  emit per-topic news-vs-social splits (currently `byTopic` aggregates
  across sources). ~50 LOC backend + ~30 UI.
- **Publisher profile cards on Sentiment** — a grid of major news
  publishers (Fox, CNN, AP, Reuters, NYT, WSJ, …) each showing their
  own tone, volume, and drill-down-by-topic modal. Needs a new
  `byPublisher` aggregator branch grouping by `domain_or_subreddit`
  where `source_type == 'news'`, plus a curated partisan-lean map
  (probably a YAML file next to `known_accounts.yaml`).
- **Social profile cards** — major subreddits + classified X accounts
  (we already have `known_political_x_accounts.yaml`). Same pattern.
- **Reads-as treatment elsewhere** — SocialVsNewsCard, GOP stance card,
  narrative rows, bot amplification card.
- **Confidence adjacent to every big number** — polish pass across
  cards that surface a large metric without visible confidence.
- **Backend sanitization of bot signals** — the UI guard (strip
  `=None`, `=0` whyFlagged entries) works but belongs in the bot
  detector's signal generator, not the UI.
