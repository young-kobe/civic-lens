# 2026-07-09 — Adversarial review: analysis layer

Point-in-time adversarial review of `analysis/src/` (etl, engine, llm, scheduler, aggregators, review) against the Part-B invariants in `docs/INVARIANTS.md` and the media-analysis rules. Findings only — no fixes shipped in this entry. Method: full-code adversarial pass by a review agent; all HIGH findings independently re-verified against source before recording. Companion entries: `../ingestion/2026-07-09-adversarial-review.md`, `../ingestion/2026-07-09-adversarial-review-data-layer.md`, `../ui/2026-07-09-adversarial-review.md`. The new eval harness (`analysis/evals/`) was excluded — it shipped yesterday with its own tests.

Severity: HIGH = fabricated/mislabeled data or invariant violation; MEDIUM = correctness under realistic conditions; LOW = robustness. CONFIRMED = failing path fully traced.

## Findings

### A-1 HIGH CONFIRMED — Confidence-range guard is dead code; out-of-range confidences stored verbatim

`analysis/src/llm/base.py:118-158`: `_coerce_numeric_scales` only fires when a schema field has `maximum == 1`, and the validator's range checks need `minimum`/`maximum` present — but `schemas.py` deliberately omits them (Gemini rejects those keywords) on the promise that "the engine code clamps when it reads them". Sentiment, favorability, and bot do NOT clamp: `analyzer.py:292,320,333` and `bot.py:369` store `float(response[...])` raw. Claims and propaganda do clamp (`claim_extractor.py:80`, `propaganda_detector.py:99`). With the default backend `ollama`/`qwen2.5:3b` — the exact model class the coercion docstring says emits percentages — a response of `"confidence": 85` is stored as 85.0, sails past `aggregation_min_confidence=0.5` and every "strong confidence" threshold, and renders as nonsense in the UI and review queue. Fix shape: clamp at the three read sites (or coerce/validate against an internal schema with bounds, keeping the Gemini-facing schema stripped).

### A-2 HIGH CONFIRMED — Propaganda rate computed over a biased denominator

`analysis/src/engine/propaganda_detector.py:140-145` short-circuits docs with no loaded vocabulary to `PropagandaResult(inference_method="deterministic")` — a real "clean" verdict (added by the walkthrough-048 cost controls). But `analysis/src/reporting/aggregators/propaganda.py:199-201` filters `inference_method != 'deterministic'` out of `total_eligible_docs`, on the stale docstring assumption (propaganda.py:12-15) that such rows "do not exist for propaganda today". Headline propaganda rate and mean are therefore computed only over docs that already contained charged vocabulary: 1000 docs scored, 600 pre-filtered clean, 100 flagged shows 25% instead of 10%. `narrative.py:274` repeats the exclusion in per-narrative propaganda means. This is a systematically inflated headline number on an honesty-first dashboard.

### A-3 HIGH CONFIRMED — Transient LLM failures persisted as permanent "nothing found" LLM verdicts

`claim_extractor.py:113-115` catches any exception and returns an empty result; `job_runner.py:482-492` saves it with hardcoded `inference_method="llm"`. `propaganda_detector.py:153-155` returns bare `PropagandaResult()` on exception, whose dataclass default is `inference_method="llm"` (`engine_models.py:111`) — contradicting the detector's own docstring ("job_runner skips the doc"). Because an `ai_outputs` row now exists, `get_unprocessed_docs` never re-queues the doc. One Ollama outage during a cron fire permanently stamps up to a full batch of docs as "LLM found no claims / no propaganda", silently depressing narrative support counts and the propaganda mean over time. Fix shape: raise or return a sentinel on transport failure and have job_runner skip (not save) those docs.

### A-4 MEDIUM CONFIRMED — GOP favorability mover permanently dead

`movers.py:199-207` filters on `payload.get("target")`/`payload.get("label")` — keys the favorability writer never emits (`FavorabilityResult.to_dict()` produces `overall_gop_stance`/`entity_stances`). Volume is always 0; `/api/movers` serves `favorability_mover: null` unconditionally.

### A-5 MEDIUM CONFIRMED — Movers ignores the bot and confidence filters its sibling aggregators apply

`movers.py:83-98` selects sentiment rows with no `get_bot_flagged_doc_ids` exclusion and no `aggregation_min_confidence` floor (contrast `sentiment.py:99-118`). The "biggest mover" ticker can be driven by bot-flagged and sub-threshold rows that the Overall Tone chart on the same page excludes — two surfaces disagreeing on the same window.

### A-6 MEDIUM CONFIRMED — Narrative net-sentiment contradicts its documented formula

`narrative.py:464-483`: NEUTRAL/MIXED rows are skipped before the counter increments, so the "average over docs with NEUTRAL=0" contract in the docstring is false — the mean runs over polarized docs only. A narrative with 49 NEUTRAL + 1 POSITIVE(0.9) reports net sentiment +90 where the sentiment page's formula would say ~+2.

### A-7 MEDIUM CONFIRMED — Invariant C1 violation: X classification samples on the sentiment page carry no source link

`sentiment.py:314-320`: `_build_sample_dict` synthesizes URLs for news and Reddit but never the X permalink, although `x_handle` is available from `X_AUTHOR_JOIN_SQL`. Narrative/bot/review builders do synthesize `https://x.com/{handle}/status/{id}`. Every X evidence sample in sentiment topic/strength/entity drill-downs is unauditable; INVARIANTS.md C1 calls a doc row without a link a bug.

### A-8 MEDIUM CONFIRMED — NULL published_at docs consume LLM spend, then vanish from every window

`loader.py:79-81` admits docs with NULL `published_at` ("assume recent"); every cached aggregate window appends `AND d.published_at >= ?` (`base.py:97-99`), which NULL never satisfies, and no "all" window is ever cached (`job_runner.py:642`). Such docs are bot-scored, sentiment-scored, and claim-extracted (paid calls) yet contribute to nothing the UI shows, with no warning.

### A-9 MEDIUM CONFIRMED — Invariant B1's ETL versioning is unimplemented

No ETL/logic version is logged or stamped on docs rows anywhere in `analysis/src` (the traceability half of B1 — `docs.raw_hash NOT NULL` — is sound). Filter keywords, the 30-day rule, and extraction logic have all changed with no way to tell which docs each version produced. Implement a stamped `etl_version`, or rewrite B1 to match reality.

### A-10 LOW CONFIRMED — Narrative clustering loses claims on mid-doc crash

`narrative_clusterer.py:182-185, 275-285`: docs are excluded from re-processing per-doc (any `narrative_docs` row) but committed per-claim. A crash between claim 1 and claim 3 permanently under-clusters the doc with no marker.

### A-11 LOW CONFIRMED — Supporting-docs drill-down can show duplicate rows

`narrative.py:497-517` joins sentiment without deduping by doc_id; `ai_outputs` has no UNIQUE(doc_id, task_type), and `propaganda.py:344-347` documents and dedupes exactly this case. Concurrent cron + admin-triggered runs double-write and the modal shows the doc twice.

### A-12 LOW CONFIRMED — Retry policy diverges between backends

`ollama.py:33` defaults `max_retries=1` (zero actual retries) vs Gemini's 3 attempts, despite the "one shared retry policy" comment; the factory never overrides. Gemini also sleeps its backoff once more after the final failed attempt. A transient Ollama hiccup falls to heuristics (or, per A-3, a permanent empty verdict) where Gemini would have retried.

### A-13 LOW CONFIRMED — prompt_versions.task_type flip-flops per doc

Sentiment and favorability share one `prompt_version` key but save with different `task` values; the upsert (`loader.py:416-433`) rewrites `task_type` on every conflict, so the audit table's column alternates twice per doc. Reconstruction survives only because the prompt text is identical.

## What held up

Verbatim evidence-span validation (sentiment/claims/propaganda) is real and enforced; citation extraction commits edge+marker atomically; `SnapshotCache` writes are atomic and traversal-guarded; job_runner's per-stage failure isolation, budget guard, and snapshots-in-finally are well built; review.py's `INSERT OR REPLACE` is safe under the `ai_output_id UNIQUE` constraint; polling validates rather than fabricates; no SQL injection found (all variable values parameterized).

## Recommended fix order

A-1 and A-3 first (both let fabricated-or-broken numbers into `ai_outputs`, the table everything trusts), then A-2 (headline-number bias), A-7 (C1 violation), A-5/A-6 (aggregator consistency), A-8/A-9 (ETL honesty), then the LOWs. A-2's fix must decide whether deterministic-clean rows count in the denominator (they should) and update the stale docstrings both sides.
