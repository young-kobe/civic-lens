# Bot + propaganda signal calibration

Two live design questions salvaged from a retired todo (the pre-rewrite
registries/exclusion-gate design it described no longer exists in the code).

- [ ] **Surgical registry-based bot signal instead of a blanket cap.**
      `analysis/src/engine/bot_detection.py` measures `verified_type` and
      passes it to the LLM prompt, but nothing derived from it changes the
      score (the old hard-zero/cap gate was removed 2026-07-25 as a de-bias
      fix). Decide whether a narrower registry signal — downweight rather
      than exclude, or a separate calibration cohort for verified
      institutional accounts — is worth reintroducing, and if so where it
      should apply relative to `analysis.runs.confidence`.
- [ ] **Propaganda-rate-by-lean calibration.** `analysis/src/engine/propaganda.py`
      classifies each doc independently with no entity context in the
      prompt. Add a post-hoc, report-only view that joins
      `analysis.propaganda_results` (via `analysis.runs`) to `corpus.entities.lean`
      and tracks flag rate by lean bucket — surfacing detector bias is the
      goal, not feeding lean into the prompt (CLAUDE.md: political lean is
      never fed into an LLM prompt).
