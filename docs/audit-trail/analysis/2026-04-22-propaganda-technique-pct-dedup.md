# 2026-04-22 — Propaganda technique % no longer exceeds 100

`pct_of_flagged_docs` in the propaganda aggregator now counts each technique *at most once per flagged doc*, so values are bounded by 100. Previously a post with two loaded-language evidence spans incremented the counter twice, and the banner could render "Loaded language is the most common, appearing in 197% of flagged posts." — nonsense to an everyday reader.

## What shipped

`analysis/src/reporting/aggregators/propaganda.py::PropagandaAggregator._aggregate_from_outputs` — inside the `if is_flagged:` branch, the per-technique increment now goes through a local `Set[str]` so each of the six enum values can contribute at most once per doc:

```python
doc_techs: Set[str] = set()
for t in techs:
    if not isinstance(t, dict):
        continue
    name = t.get("technique")
    if name in PROPAGANDA_TECHNIQUE_ENUM:
        doc_techs.add(name)
for name in doc_techs:
    technique_counts[name] += 1
```

Added `Set` to the `typing` import.

The downstream formula `(count / flagged * 100)` is unchanged — it now represents what the field name already claimed ("% of flagged docs that used this technique"), bounded by 100.

## Why

User saw the Propaganda banner render *"News leans on these techniques more than social media (34.5% vs 0.0% flagged). Loaded language is the most common, appearing in 197% of flagged posts."* The 197 is a bug: the detector emits one evidence span per technique-*instance*, so a single post with two separate loaded-language phrases counted twice against a base of "flagged docs." The field name already said "of flagged *docs*" — the implementation just didn't match.

## Follow-ups

- If we ever want the total-instance view, it should be a separate field (`total_technique_mentions` or similar) — not overload `pct_of_flagged_docs`.
- Cached snapshots carry the old (inflated) numbers until the next pipeline run. `analyze -Tasks snapshots` rewrites them.
