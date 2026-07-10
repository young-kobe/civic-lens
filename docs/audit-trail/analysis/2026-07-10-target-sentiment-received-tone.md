# 2026-07-10 — Target-sentiment stage: received vs. expressed tone

The pipeline now extracts WHO each doc takes a stance toward, separately
from how the doc sounds. A new LLM stage (`targets`, task_type
`target_sentiment`) records per-target stances; the sentiment aggregator
fans them out to (doc, target) pairs and attaches a **received tone**
(how sampled posts talk ABOUT a tracked official) to each official's
entity item, distinct from `netScore`, which remains the **expressed
tone** (the tone of their own posts). Previously the officials column
showed expressed tone labeled "Net tone" — Schumer read -66.7 not
because anyone was negative about Schumer, but because Schumer posts
negatively about Trump, ICE, and the ACA repeal. The two numbers are now
orthogonal and separately labeled.

## What shipped

- `analysis/src/engine/target_extractor.py` — `TargetSentimentExtractor`,
  following the claim-extractor pattern: trivial-content short-circuit
  (no LLM call), `extraction_failed` flag on transport errors so
  job_runner skips persisting and the doc re-queues (audit A-3),
  verbatim-evidence validation with the sentiment engine's
  0.3 confidence cap on unverifiable spans (invariant B2), stance/topic
  enum validation, per-doc target de-dup.
- `analysis/src/llm/schemas.py` — `TARGET_SENTIMENT_SCHEMA` +
  `TARGET_TOPIC_ENUM` (mirrors aggregator `TOPIC_KEYWORDS` keys plus
  "Other"; a test pins the sync).
- `analysis/src/llm/prompts.py` — `target-sentiment-v1` system prompt +
  user template. A TRACKED TARGETS block (16 registry officials + the
  two party collectives, injected by job_runner) nudges the model toward
  canonical names; resolution never depends on it.
- `analysis/src/reporting/entity_registry.py` — `TargetResolver`:
  deterministic resolution of raw target names to registry officials
  (handles, aliases, display names, unambiguous last names, title-prefix
  stripping) or the `gop_collective` / `dem_collective` sentinels with
  party attribution. Resolution runs at aggregation time, so registry
  edits retroactively resolve stored rows without re-running the LLM.
- `analysis/src/scheduler/job_runner.py` — `run_target_extraction`
  (Step 4/11, `-Tasks targets`), budget-guarded like the other LLM
  stages; row confidence is the mean of per-target confidences (claims
  pattern). Steps renumbered N/11.
- `analysis/src/reporting/aggregators/sentiment.py` —
  `_merge_target_tone`: fans `target_sentiment` rows out per (doc,
  target); filters per-target confidence against the aggregation floor
  and bot-flagged docs; attaches `received` (net, volume, lowSample,
  byTopic cells, samples) to each official's `EntitySentimentItem`,
  creating a zero-expressed-volume item for officials who are discussed
  but never posted; accumulates per-speaker same-party / cross-party
  `expressed_alignment` cells (self-references excluded) plus global
  baselines; emits `targetTone` metadata (suppression threshold,
  resolution coverage, collectives) on `PublicSentimentResult`.
- **Small-n suppression**: any received net (overall or per-topic cell)
  from fewer than `MIN_TARGET_SAMPLE_N = 5` pairs is withheld
  (`net=None`, `lowSample=true`) with the honest volume emitted — one
  classified tweet can no longer render as +100.0 (the Rubio/Thune
  failure mode).
- Tests: `analysis/tests/test_target_extractor.py` (validation,
  resolver, topic-enum sync), `analysis/tests/test_target_tone_aggregation.py`
  (fan-out, suppression, alignment, bot/confidence exclusion,
  serialization).

## Why

- "Net tone" collapsed speaker and subject into one scalar, so an
  official's card number measured their rhetorical posture while reading
  as their reputation — a semantics bug, not a classifier bug.
- Per-official topic breakdowns and the officials column were showing
  ±100.0 headline numbers off single classified posts.
- Backfill is a migration, not a re-crawl: the stage is keyed on its own
  task_type, so `get_unprocessed_docs("target_sentiment")` re-queues the
  entire stored corpus without touching existing sentiment rows. No DB
  migration — output lives in `ai_outputs` JSON like every other task,
  and the fan-out happens in the aggregator.

## Follow-ups

- Rewire the "What each group is saying" topic-divergence panel onto
  target-resolved sentiment (per-topic tier nets currently still use
  title-keyword topics on overall sentiment).
- Alignment residual display: baselines are cached under
  `targetTone.baselines`, but per-official deviation-from-baseline needs
  more volume before it renders un-suppressed; revisit after the corpus
  backfills.
- Consider folding target extraction into the unified text-analysis call
  once the corpus is fully backfilled (saves one LLM call per doc going
  forward; costs the clean re-queue path).

Cross-link: `docs/audit-trail/ui/2026-07-10-received-vs-expressed-tone.md`.
