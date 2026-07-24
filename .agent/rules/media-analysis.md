---
trigger: always_on
---

1. Do not assign moral judgment or intent to authors or publishers.
2. Do not label content as propaganda unless operational indicators are explicitly defined and cited.
3. "Reach" must be labeled as a proxy unless verified audience metrics are available.
4. Reddit-based analysis must be labeled as "sampled Reddit discourse."
5. Avoid universal claims about national sentiment or voter behavior.

## Phase 9 (strictly-live API) — lean presentation and range honesty

Codified 2026-07-24 (owner decisions 2026-07-22 and 2026-07-24). See
`docs/audit-trail/analysis/2026-07-24-phase9-prewave.md` and
`docs/audit-trail/api/2026-07-24-phase9-wiring-review-docs.md`.

6. A political lean is one of three epistemic kinds, and no surface may
   blur them: `fact` (an official's stated party registration), `curated`
   (an outlet's or subreddit's editorial lean, a human judgment call), or
   `derived` (a statistical estimate — `analysis.author_leans` /
   `analysis.narrative_leans`). A `derived` lean must never render without
   the evidence backing it (`lean_share`, `confidence`, `sample_count`);
   `fact`/`curated` leans must never carry those fields, since they aren't
   estimates. Encoded in `analysis/src/api/models/common.py`'s `LeanLabel`.
7. Political lean — of any of the three kinds — is never fed into an LLM
   prompt. Lean is a presentation-layer label applied to model output, not
   an input to it.
8. `sampled` and `official_record` documents (`corpus.documents.
   admission_class`) must be labeled distinctly on every surface that shows
   both — an `official_record` post (a tracked official's own words,
   admitted regardless of age) is not "sampled discourse" and must not be
   captioned as if it were.
9. A time window scopes which documents count toward an aggregate's
   denominator; it never gates whether a document exists. A citation
   target, a document drill-down, or any other resolved reference must
   render in full regardless of how far outside the requested window it
   falls.
10. Every aggregate API response carries a `RangeMeta` block (resolved
    bounds, the `sampled`/`official_record` split, and the distinct
    `model_ids` behind the numbers) — so a long historical range is
    visibly labeled as potentially spanning a model/prompt/source-mix
    change, not presented as one continuous, directly-comparable series.
