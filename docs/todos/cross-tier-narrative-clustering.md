# Cross-tier narrative clustering

The `/narratives` page surfaces a "Top political narratives" card that lists stories whose supporting docs span at least two of `{news, officials, public}`. In the current dataset that list is reliably empty — every top narrative is single-tier. The 2026-04-25 UI change made the empty state honest, but the upstream reason matters: cross-tier overlap is the page's main differentiator, and it's not happening.

The cross-tier flag is computed in `analysis/src/reporting/aggregators/narrative.py::_is_cross_tier` and is already at the loosest possible threshold (≥2 of 3, not all 3). So the bottleneck is upstream — claims from different tiers aren't being clustered together.

## Hypotheses to check

- [ ] **Lexical-island problem.** Default similarity is Jaccard at threshold `0.3` (`engine/narrative_clusterer.py::JACCARD_THRESHOLD`). News headlines, Reddit comments, and X posts phrase the same proposition with very different vocabulary. Run a side-by-side with `CIVIC_NARRATIVE_SIMILARITY_MODE=embedding` (threshold `0.65`) on the same 30-day window and compare the count of cross-tier narratives.
- [ ] **Claim-extraction drift across source types.** The same backing event may be extracted as different claim strings depending on the input shape (long-form article vs. 280-char post vs. nested Reddit comment). Sample 10 same-event claim sets across tiers and check whether the extracted claim text actually overlaps in tokens.
- [ ] **Tier-volume skew.** If one tier (e.g., officials) contributes ≪ docs per window, no narrative will ever cross into it regardless of clusterer quality. Pull per-tier doc counts in a 7d / 30d window and confirm each tier has ≥ a couple hundred classified docs.
- [ ] **Anchor-on-first-seen rigidity.** `_NarrativeAnchor` locks a narrative's identity to its first-seen claim's tokens / embedding (intentional, for stability across runs). If the first claim is a news headline, later social-media phrasings may all fail Jaccard against the headline's specific noun phrases. Worth measuring how often the *second* candidate cluster would have matched if anchors moved with the centroid.

## Reproduction

1. `.\run.ps1 analyze -Tasks narratives` to re-cluster, then `.\run.ps1 analyze -Tasks snapshots` to refresh the cache.
2. Hit `/narratives?window=7d&limit=100` and count rows with `cross_tier: true`.
3. Repeat with `CIVIC_NARRATIVE_SIMILARITY_MODE=embedding` for comparison.

## Out of scope here

- The UI presentation. That was fixed 2026-04-25 — the empty state is honest, the ticker is honest. This todo is purely about whether we can make cross-tier overlap *happen* in the data.
- Schema changes. Walkthrough 035 already drew the line at "support / citation overlay over docs we own"; we are not extending into causal propagation.
