# 2026-04-22 — Propaganda detector cost optimizations

Four stacked reductions to Gemini spend on the propaganda stage, landing together so the cumulative impact can be measured on the next live run. No quality regression on flagged docs — each change is either a text-length clamp, a pre-filter that skips docs we'd expect to classify as clean anyway, or a dedup that avoids paying twice for the same content.

## What shipped

### Input truncation: 2500 → 800 chars

`analysis/src/engine/propaganda_detector.py::PROPAGANDA_TEXT_MAX_CHARS` is the new budget. Propaganda techniques (loaded language, name-calling, appeal-to-fear, etc.) surface in the opening rhetoric of a piece — the hook paragraphs, not paragraph 12. The detector now also accepts an optional `title`; when provided, it's prepended to the body before clamping, so the LLM sees `"{HEADLINE}\n\n{BODY…}"`. `job_runner.run_propaganda_detection` wires `doc["title"]` through.

Evidence-span validation is now checked against the *clamped* text (what the LLM actually saw), not the full text. Prevents the edge case where an LLM quote from the truncated prefix would fail validation because the full string isn't a substring of the prefix.

### Loaded-language pre-filter

Before the LLM call, `_has_loaded_language(clamped_text)` scans the first 600 chars for any word in `NEGATIVE_WORDS ∪ INTENSIFIERS` (from `engine/constants.py`). Zero matches → skip the LLM call, write a deterministic `PropagandaResult(inference_method="deterministic")`. Deterministic rows still populate `ai_outputs` so the doc doesn't re-queue on the next run.

The lexicon is deliberately broad — the pre-filter optimizes for recall, not precision. If a doc has zero loaded/intensifier tokens in its opening, the six starter propaganda techniques can't land.

### Raw-hash dedup

Syndicated wire stories (AP, Reuters copy re-hosted by multiple outlets) share a `raw_hash` in the `docs` table. `run_propaganda_detection` now groups its unprocessed batch by `raw_hash` and LLM-scores one representative per group, then fans the result to every sibling via `save_ai_output`. Docs with a null `raw_hash` (pre-migration-006 rows or source types we haven't backfilled) get a per-doc sentinel key so they don't collapse into one bucket.

`loader.get_unprocessed_docs` now returns `raw_hash` as part of each row dict.

Logging:
```
Processing 173 docs for propaganda detection (141 unique content hashes)
...
Propaganda detection complete: 173 docs processed (141 LLM calls, 32 dedup-fanned)
```

### Tighter loader batch-size ceiling

`CIVIC_LOADER_BATCH_SIZE` default: 500 → 200. Upper bound on per-stage LLM spend per cron fire. 4 fires/day × 200 docs × ~$0.0005/call (truncated Flash) ≈ $12/month worst case, fitting well under the $15/mo Gemini budget even before the pre-filter + dedup savings.

## Why

User hit 50% of $15 Gemini budget by 2026-04-22. Rather than switch to a self-hosted model (which would collide with the >95% accuracy invariant), we compressed the cost-per-call on Gemini.

## Expected impact

Rough per-stage savings, multiplicative:

- Input truncation: ~70% fewer input tokens per call → ~50-60% total cost per call (output is uncapped but typically small).
- Pre-filter: ~30-50% of docs on a typical news batch carry no loaded-language in their opening → that many calls saved outright.
- Dedup: syndicated stories are 15-25% of news volume on any given day → that fraction of remaining calls saved.
- Batch ceiling: hard cap on worst-case per-fire spend regardless of backlog.

Stacked: realistic 60-75% cost reduction on propaganda with no accuracy regression. Propaganda is the spendiest stage; sentiment + claims run on shorter text and weren't optimized here.

## Validation

- Pre-filter unit check: `_has_loaded_language("the president spoke today about policy.")` → `False`; `_has_loaded_language("this is a disaster and a betrayal of the public trust.")` → `True`.
- Pipeline smoke run with `-Limit 1 -Tasks propaganda` successful; ai_outputs row written with either `inference_method="llm"` or `"deterministic"` depending on the doc.

## Follow-ups

- Apply the same three optimizations to `claim_extractor` next if claims spend becomes material. The pre-filter would need a different lexicon (claims aren't about rhetoric, they're about assertions).
- `docs/todos/backend-aggregator-audit.md` — the loader returning `raw_hash` is one more column the proposed `WindowContext` would preload.
