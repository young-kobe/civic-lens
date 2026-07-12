# 2026-07-12 — Balanced "overall tone" + sample-composition graphic

The headline net tone no longer tracks whichever speaker tier we happened to sample most. A window
can carry ~900 news posts next to ~100 from the public, and the old volume-pooled net let news
dominate. The headline is now the **mean of the per-tier nets** (news / officials / public), and the
Tone page shows the sample composition so the imbalance is transparent.

## Backend (analysis)

- **`sentiment/aggregator.py`**
  - New `by_tier` accumulator: per-tier (news/officials/public) stance counts, filled in the row loop
    alongside `by_topic_tier` / `by_day_tier`.
  - `_balanced_net(by_tier, fallback)`: the mean of each tier's net (`_net_from_tier`); tiers with no
    posts drop out; falls back to the pooled net when no tier has data.
  - `_build_result` sets `SentimentOverview.netScore` from `_balanced_net` instead of the pooled
    `(pos-neg)/total`. Distribution, volume, and per-tier numbers are unchanged.
- **Test** (`test_balanced_overall_tone.py`) — pins the contract: 900 negative news vs 100 positive
  public nets to 0.0 (not ~-80); equal weight across three tiers; empty tiers excluded; pooled
  fallback when no tier has data.

## Frontend (ui)

- **`PublicSentiment.tsx`** `ToneDivergenceCard` — fills the left-column space that opened when the
  weekday strip was removed, and makes the tier gap scannable: a diverging bar per group showing how
  far its net tone sits from **the public's** (the baseline, center zero) — right = warmer, left =
  harsher. **Officials are split by party** (Dem vs GOP, `aggregateOfficialsByParty` over the
  per-official rollups' `entityProfile.party`), each in its lean hue, alongside News. New
  `.tone-divergence-*` CSS. (This replaced an earlier per-group sample-count bar graphic.)

## Notes

- Fixtures are static mocks: changing the time-range (or topic) filter does not fetch different mock
  data, so filtering looks inert in fixtures mode. Against the real API each window/topic is a
  distinct fetch, so it works there — this is expected, not a bug.

## Verification

- Backend tests green (42 across the sentiment suites incl. the new one); UI `typecheck` + `build`
  green. The balanced headline + sample bars reflect real data after the next `save_snapshots`.
