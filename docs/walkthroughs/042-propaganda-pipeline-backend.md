# 042 — Propaganda Pipeline Backend

## Context

Invariant B3 in `docs/INVARIANTS.md` has always required that "propaganda" be defined as **measurable techniques with cited spans**, not a subjective label. It was aspirational until now — walkthrough 035 explicitly flagged it as "planned, not yet implemented" and pointed at this walkthrough.

042 lands the backend: a per-doc LLM classifier that flags one or more of six starting propaganda techniques, each with a verbatim evidence span. Each inference becomes an `ai_outputs` row with `task_type='propaganda'` — same shape as sentiment/claims, so existing audit and calibration surfaces (Review tab, `ai_output_evals`) pick it up for free.

043 will surface this in an aggregator + UI tab; 044 will calibrate it against a golden set. 042 is strictly the backend — detector, prompts, schema, orchestration, tests.

## Taxonomy — six starting techniques

Each has a tight operational definition documented in the system prompt:

| Technique | What it catches |
|---|---|
| `loaded_language` | Emotionally charged framing words: "tyrannical regime", "radical mob", "woke agenda" |
| `name_calling` | Dismissive labels: "grifter", "RINO", "fascist" used as insult |
| `ad_hominem` | Attacks on the speaker's character, not the argument |
| `appeal_to_fear` | Catastrophic imagery to bypass reasoning: "the country is finished if X wins" |
| `whataboutism` | Deflecting with unrelated alleged misconduct by the other side |
| `doubt_casting` | Insinuation without evidence: "many people are saying", "questions are being raised" |

These are standard academic propaganda techniques (informed by SemEval-2020 Task 11). The six chosen strike a balance between coverage and operational clarity — each has unambiguous examples the prompt can teach. Future expansion can add techniques without breaking the output schema.

## Changes

### Prompts (`analysis/src/engine/prompts.py`)

- New `PROPAGANDA_PROMPT_VERSION = "propaganda-v1"`.
- New `PROPAGANDA_SYSTEM_PROMPT` — defines each technique, requires a **verbatim 4+ word evidence span** per flagged technique, caps `techniques` at 5, explicitly tells the model NOT to flag:
  - Strong opinions expressed with measured language.
  - Factual statements about controversial topics.
  - Direct disagreement that engages the argument on its merits.
  - Quoted-propaganda-as-reporting (when clearly attributed).
- New `PROPAGANDA_USER_PROMPT_TEMPLATE` — passes just the doc text (no signal injection, per 038's cleanup pattern).

### Schema (`analysis/src/llm/schemas.py`)

- `PROPAGANDA_TECHNIQUE_ENUM` — the six technique strings (shared with the detector for runtime validation).
- `PROPAGANDA_TECHNIQUE_SCHEMA` — `{technique enum, confidence 0-1, evidence_span string}`.
- `PROPAGANDA_SCHEMA` — `{techniques: [...], overall_propaganda_score 0-1, reasoning string}`.

### Engine models (`analysis/src/engine/models/engine_models.py`)

- New `PropagandaTechnique(technique, confidence, evidence_span)` dataclass.
- New `PropagandaResult(techniques, overall_propaganda_score, reasoning, inference_method)` dataclass with stable `to_dict()` for JSON serialization into `ai_outputs.output_json`.
- Re-exported from `analysis.src.engine.models.__init__`.

### Engine — `propaganda_detector.py` (new)

`PropagandaDetector.detect(text) -> PropagandaResult`:

- **LLM-only**. No deterministic fallback. Propaganda-technique detection is a language-understanding task that heuristics cannot do honestly; a fabricated verdict is worse than an empty result. When LLM is disabled or fails, the detector returns an empty `PropagandaResult` and the job-runner simply skips the doc.
- **Evidence validation** (invariant B2): each flagged technique whose `evidence_span` is under 4 words, not a case-insensitive substring of the source, or references an unknown technique name, is dropped via `_validate_technique`.
- **Three-way `overall_propaganda_score` handling**:
  - At least one technique validated → trust the LLM's `overall_propaganda_score`.
  - LLM returned techniques but **none** validated → cap score at `UNVERIFIED_EVIDENCE_CAP = 0.2`. Prevents a hallucinating model from moving the headline.
  - LLM returned zero techniques → force `overall_propaganda_score = 0.0` regardless of what the model returned.
- **5-technique cap** enforced defensively in addition to the schema constraint.

### Orchestration (`analysis/src/scheduler/job_runner.py`)

- New `run_propaganda_detection(limit)` method, inserted between `run_text_analysis` and `run_citation_extraction` in the pipeline.
- Pipeline grows from 9 → **10 steps**. All log labels renumbered. Pipeline order:
  1. ETL → 2. Bot → 3. Text (sentiment + favorability) → 4. **Propaganda (new)** → 5. Citations → 6. Claims → 7. Narratives → 8. Accounts → 9. Bot rollup → 10. Snapshots.
- Propaganda runs over the same scope as sentiment (`CIVIC_RUN_ANALYSIS_ON`) — news + social, deliberately. 043's aggregator will split the surfaces by source type.
- `save_ai_output` call includes the new user-template arg (041 contract), the system prompt, `prompt_version='propaganda-v1'`, and `inference_method=result.inference_method`.
- Added `propaganda` as a valid `--tasks` CLI value.
- Summary dict gains a `"propaganda": int` (docs processed) entry.

### Tests (`analysis/tests/test_propaganda.py`) — 13 new

- `TestValidateTechnique` (6): verbatim accepted; fabricated rejected; short-span rejected; unknown-technique rejected; bad-confidence rejected; over-1 confidence clamped.
- `TestPropagandaDetector` (7): empty when LLM disabled; happy path with 2 validated techniques; fabricated-evidence flag dropped; score capped at 0.2 when all invalid; score forced to 0.0 when no techniques; 5-cap enforced on excess techniques; `to_dict` stable.

## Verification

- 13/13 new tests pass.
- Affected-module bundle (propaganda + cache_and_versioning + engines + bot_rework + inference_method + aggregation_confidence_filter + propagation + account_classifier + review + refresh_accounts + rich_aggregators) — **119/119 pass**.

## Not in scope (deferred)

- **Aggregator / API / UI** — walkthrough 043. That will introduce a `PropagandaAggregator`, the `/api/propaganda` endpoint, and a UI tab that shows per-source-type technique-rate breakdowns and correlates with `author_bot_scores` (walkthrough 040's output) so we can surface *"narratives heavily pushed by bot-looking accounts AND heavily using propaganda techniques"*.
- **Calibration** — walkthrough 044. The Review tab (034) can begin accepting `task_type='propaganda'` labels after 043 wires it up; once there are ~100 golden rows the calibration harness will produce the accuracy curve.
- **Additional techniques.** The schema enum is a known expansion point; techniques like `strawman`, `flag_waving`, `thought_terminating_cliche` can be added without breaking anything.

## Deploy

```powershell
# No migration needed — ai_outputs accepts any task_type string.
.\run.ps1 analyze -Tasks propaganda
# Or as part of the full pipeline:
.\run.ps1 analyze
```

Every subsequent `analyze` run will flow unprocessed political docs through propaganda detection. Note: this is an LLM-gated step — check `CIVIC_LLM_ENABLED=true` and the configured backend is reachable.

## Remaining roadmap

| # | Scope |
|---|---|
| 043 | Propaganda pipeline — surfaces (aggregator, API, UI tab, correlation with `author_bot_scores`, review-task extension) |
| 044 | Calibration harness — `ai_output_evals WHERE is_golden=1`, per-task accuracy curves (now includes `propaganda` + `bot_detection`) |
