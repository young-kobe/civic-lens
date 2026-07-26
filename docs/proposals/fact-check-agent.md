# Fact-check agent — feasibility note

> Draft (pre-Postgres-rewrite, 2026-04); machinery references are stale.

Status: **proposal, not for implementation in this branch.** Author: pipeline team. Date: 2026-04-25.

## What it would do

Given a claim already extracted by `analysis/src/engine/claim_extractor.py`, a fact-check agent would compare the claim to the seeded reference registry (`data/references/*.md` + the larger evidence base we'd build out) and emit a verdict + citation for each claim, instead of just storing the bare claim string.

```
input  : ExtractedClaim {claim, evidence_span, confidence, doc_id}
process: 1. retrieve relevant references (keyword + embedding search over
            data/references/ and any future evidence base)
         2. LLM call: "given these references, does the claim contradict,
            align with, or fall outside the scope of the cited sources?"
         3. enforce a verbatim-citation rule: every verdict must quote the
            specific reference passage that justifies it (same invariant
            as evidence_span on claim extraction)
output : FactCheckVerdict {
           claim_id,
           verdict in {supported, contradicted, partial, out_of_scope, insufficient_evidence},
           confidence in [0,1],
           citation_source_url,
           citation_passage (verbatim),
           reasoning
         }
```

The agent would persist to a new `claim_fact_checks` table joined back to `ai_outputs` (task_type = `fact_check`), with the same `prompt_version` / `model_id` audit columns the rest of the pipeline already uses.

## Cost estimate

Volume from current pipeline (April 2026 sample — see `data/civic_lens.db` weekly counts in the analysis log):

- ~250 docs/day analyzed, of which ~60% pass the political-content filter ≈ 150 LLM-eligible docs/day.
- Claim extraction yields ~1.2 claims/eligible doc on average ≈ **180 claims/day**.

Per-call token budget for a fact-check pass:

- System prompt (rubric + citation rules): ~600 tokens.
- Reference passages retrieved (top-k=3, ~250 tokens each): ~750 tokens.
- Claim + surrounding doc excerpt: ~200 tokens.
- Output (verdict + verbatim citation + reasoning): ~250 tokens.
- **Total per claim: ~1,800 tokens** (1,550 in / 250 out).

At Gemini 2.5-flash retail prices (April 2026 published card, $0.075/M input + $0.30/M output):

- Per claim: 1,550/1M × $0.075 + 250/1M × $0.30 ≈ $0.000191 ≈ **0.019¢/claim**.
- Per day: 180 × $0.000191 ≈ $0.034/day.
- **Per month: ≈ $1.03/month.**

That sits comfortably under the existing $15/mo Gemini line item, even with 3× headroom for retries and prompt iteration. Switching to Gemini Pro for higher accuracy would bump to ≈ $5–7/mo. Running on Ollama/Orin Nano locally is free but ~30s/claim instead of ~2s.

The cost ceiling is therefore **not** the blocker — building it is technically affordable. The blockers are below.

## What we'd need before building it

These are non-negotiable. Skipping any one of them turns the agent into a confidence-laundering machine.

1. **Human review queue for verdicts.** The pipeline already has `analysis/src/reporting/review.py` writing `ai_output_evals` with golden-set + correctness markers. A fact-check agent must feed the same surface so a reviewer can mark each verdict supported / contradicted / out-of-scope, *before* the verdict surfaces in any aggregated UI. No verdict ships unreviewed for at least the first ~500 calls (calibration period). After calibration, sampling-based review (~10% of verdicts) is the floor.

2. **Appeal / correction mechanism.** A verdict is a public-facing claim by Civic Lens about somebody else's claim. We need a documented way for a subject to flag an error, a tracked turnaround on those flags, and a visible correction trail when we change a verdict. Without this, the first wrong verdict becomes a reputational and possibly legal incident.

3. **Edge-case rubric for contested or evolving science.** Several real cases the rubric must handle without hand-waving:
   - **Contested-within-the-evidence-base** (e.g. specific dietary-fat guidance, or fast-changing public-health guidance during an outbreak): the rubric must permit a `partial` verdict and require the citation to acknowledge the contestation.
   - **Evolving consensus** (e.g. an old reference that's been superseded by newer findings): we need a `last_verified` cutoff in the reference registry and a process for dropping references when they go stale, not just adding new ones on top.
   - **Out-of-scope claims** (the claim is about something we have no authoritative reference for): the agent must return `out_of_scope` rather than guess. This is the failure mode that makes the difference between a fact-checker and a hallucinator.
   - **Adversarial inputs** (claims crafted to extract a misleading verdict by exploiting the rubric): we need a small adversarial test set baked into CI.

4. **Reference base coverage audit.** Five seed files cover scientific consensus. A real fact-check agent needs much more — voting records (congress.gov), economic baselines (BLS / FRED), international-affairs primary sources (UN / IAEA / official statements). Each new reference is editorial work and a potential lever for partisan capture; a written rubric for what counts as a permissible reference, who can add one, and how additions are reviewed must exist before the agent reads from anything beyond the current seed set.

5. **Confidence calibration.** The accuracy-infrastructure-gaps memory flags this as a hard prerequisite for any >95% accuracy claim. A fact-check agent without calibrated confidences produces verdicts that look authoritative but aren't; we'd need a held-out verification set with known-truth labels and a measured accuracy/confidence curve before the pipeline acts on `verdict.confidence` (e.g. for filtering low-confidence verdicts out of the UI).

6. **Versioned reference snapshots.** When a reference is updated, every prior verdict citing it must be re-evaluable against the historical version the model originally saw. Either the reference registry is content-addressed (commit SHA per pull) or each verdict captures the exact passage at verdict-time. The seed files already include `last_verified` dates; this is the natural spot to anchor the snapshot.

## Recommended next step

Stand up the human-review surface (1) and the appeals process (2) as plain operational workflows first — those are organizational, not technical, and they gate everything else. The technical implementation is the easy part once the review pipeline exists; without it, building the agent is irresponsible.
