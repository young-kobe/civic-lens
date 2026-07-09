# LLM Evals: Golden Set + CI Regression Gate

How Civic Lens verifies that a prompt, schema, model, or pipeline-code
change did not regress LLM output quality before it ships.

## What is measured

The first evaluated stage is **claim extraction** (`analysis/src/engine/claim_extractor.py`).
It was chosen over sentiment/favorability because:

- Claims feed the narrative layer — the system's flagship overlay. A quality
  regression here silently degrades narratives, clustering, and citations
  downstream, whereas a per-doc sentiment error is visible and averaged out.
- Claims are anchored to **verbatim evidence spans** (invariant B2), which
  makes scoring deterministic: predicted and golden claims are matched by
  character-level overlap of their spans in the source text — no LLM judge,
  no fuzzy text similarity in the score.
- The stage has no heuristic fallback: with the LLM disabled it returns zero
  claims, so output quality is entirely prompt/model-determined — exactly
  what a regression gate needs to watch.

**Metric:** micro-averaged precision / recall / F1 over the golden set.
A predicted claim is a true positive when its evidence span overlaps a golden
claim's span with IoU >= 0.3 (`analysis/evals/scoring.py`); each claim can
match at most once. Unmatched predictions are false positives (the model
invented or over-extracted); unmatched golden claims are false negatives
(the model missed a claim a human judged extractable).

Known limitation: two different assertions citing the same span would count
as a match. The extractor's validation rules anchor claims 1:1 to spans in
practice, and per-example diffs surface any such case to a reviewer.

## Layout

```
analysis/evals/
  golden/claims/claims-NNN.json   one golden example per file
  recorded/claims/claims-NNN.json recorded model responses (replay inputs)
  baseline_claims.json            committed score floor for the CI gate
  harness.py                      replay/recording clients + prompt fingerprint
  scoring.py                      span matching + micro P/R/F1
  run_eval.py                     runner CLI + gate
analysis/tests/test_eval_runner.py
```

### Golden example schema

```json
{
  "id": "claims-001",                  // must equal filename stem
  "task": "claim_extraction",
  "input_text": "...",                 // the text fed to the extractor
  "input_provenance": "synthetic-draft",  // or "pipeline:docs.id=<N>"
  "expected_claims": [
    {"claim": "<canonical 5-15 word form>",
     "evidence_span": "<verbatim substring of input_text>"}
  ],
  "review_status": "PENDING_HUMAN_REVIEW",  // or "VERIFIED"
  "reviewed_by": null,
  "reviewed_at": null,
  "notes": "what this example tests"
}
```

The loader hard-fails on any example whose evidence span is not a verbatim
substring of its input text — the golden set obeys the same invariant the
pipeline enforces on model output.

**Labeling contract:** only `VERIFIED` examples count toward scores, the
baseline, and the gate. Examples drafted by a model or copied from pipeline
output start as `PENDING_HUMAN_REVIEW` and are report-only
(`--include-pending`) until a human reviews the labels and flips the status.
The initial 50 examples are synthetic drafts awaiting review; the gate is
inactive until that review happens. Unreviewed labels are never presented as
ground truth.

## Two modes: replay (CI) and live (opt-in)

The runner always exercises the real pipeline code — `ClaimExtractor.extract`,
JSON-schema validation, evidence-span verification, claim-shape validation.
Only the network call is swapped:

- **Replay (default).** Recorded model responses are fed through the live
  code path. Deterministic, free, no API keys — this is what CI runs. It
  catches regressions in everything downstream of the network call
  (validation rules, parsing, prompt bookkeeping) and detects prompt/schema
  drift via fingerprinting (below).
- **Live (`--mode live`).** Calls the configured backend
  (`CIVIC_LLM_BACKEND`) per example. Costs money / needs keys, so it is
  opt-in. Required after any change to the prompt, schema, prompt version,
  or model: `--record` refreshes the recordings that CI replays.

### Prompt fingerprinting — how a prompt change is caught

Every recording stores a SHA-256 fingerprint of (system prompt + user
template + JSON schema + prompt version) at record time. In replay mode the
runner recomputes the fingerprint from the live code; a mismatch means the
recordings describe a pipeline that no longer exists, and an **active gate
fails** with instructions to re-record. A prompt edit therefore cannot pass
CI by silently replaying stale outputs — the editor must re-run live,
review the new scores, and recommit recordings + baseline.

## The gate

`python -m analysis.evals.run_eval --gate` (wired into
`.github/workflows/ci.yml` and mirrored in `deploy.yml`'s pre-deploy gate):

1. **No baseline committed** (`baseline_claims.json: baseline == null`) —
   gate is INACTIVE: warns loudly, exits 0. This is the honest state while
   golden labels await human verification.
2. **Baseline committed** — the gate fails (exit 1) if any verified example
   has a missing or fingerprint-stale recording, or if micro-F1 drops below
   `baseline.f1 - tolerance` (tolerance currently 0.02).

The baseline is written only from verified examples with complete, current
recordings: `--write-baseline` refuses anything else.

## Workflow: verifying a change before prod

> "How do you know a prompt/embedding/model change didn't regress quality
> before rolling it out?"

1. Make the change (edit `prompts.py` / `schemas.py`, bump the prompt
   version constant — the AI-output contract already requires this).
2. `python -m analysis.evals.run_eval --mode live --record` — re-runs the
   golden set against the changed pipeline and refreshes recordings.
3. Read the printed per-example diffs: every false positive and false
   negative is shown as claim text, so a quality drop is inspectable, not
   just a number.
4. If the scores hold, `--write-baseline` (or keep the old baseline if it
   should still bind), commit recordings + baseline with the prompt change.
5. CI replays the recordings deterministically on every subsequent PR; any
   code change that alters extraction behavior (validation rules, parsing,
   truncation) moves the score and trips the gate. Any prompt change
   without step 2 trips the fingerprint check.

The same pattern extends to other stages (sentiment label agreement,
propaganda technique detection): add a golden directory, a recording
fingerprint for that stage's prompt + schema, and a task-appropriate metric.

## Bootstrapping / adding examples

- Add a file under `analysis/evals/golden/claims/` following the schema
  above; the test suite (`test_eval_runner.py`) validates every committed
  example, so a malformed one fails CI immediately.
- Prefer real pipeline inputs: pull doc texts from the production DB, e.g.
  `SELECT id, text FROM docs ORDER BY RANDOM() LIMIT 20;`, set
  `input_provenance` to `pipeline:docs.id=<N>`, draft labels, and leave
  `review_status` as `PENDING_HUMAN_REVIEW` until hand-checked.
- Keep negatives (texts with no extractable claims) in the set — they are
  what makes precision meaningful.
- Once labels are verified: record live outputs, inspect diffs, write the
  baseline. From then on the gate is active.
