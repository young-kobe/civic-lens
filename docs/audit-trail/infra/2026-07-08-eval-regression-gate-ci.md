# 2026-07-08 — Eval regression gate in CI and pre-deploy

CI now runs the claim-extraction golden-set eval as a gating step. The gate replays committed model recordings through the live pipeline code — deterministic, no API keys in CI — and fails the build when micro-F1 on hand-verified examples drops below the committed baseline, when a verified example lacks a recording, or when recordings are fingerprint-stale (the prompt/schema changed without re-recording). The harness itself is recorded in the analysis entry of the same date (`../analysis/2026-07-08-golden-set-eval-harness.md`); operator docs in `docs/EVALS.md`.

## What shipped

- `.github/workflows/ci.yml` — new `Eval regression gate` step in the `python` job: `python -m analysis.evals.run_eval --gate`, after unit tests.
- `.github/workflows/deploy.yml` — same command appended to the pre-deploy gate job, keeping it a true mirror of CI (its stated purpose).
- Gate semantics (`analysis/evals/run_eval.py::evaluate_gate`): inactive (warn, exit 0) while `baseline_claims.json` has `baseline: null`; once a baseline is committed, exit 1 on F1 below `baseline - tolerance` (0.02), missing recordings, or stale prompt fingerprints.

## Why

- The repo's CI verified code correctness (unit tests, vet, typecheck, dependency audits) but nothing verified LLM output quality — a prompt or schema edit could merge and deploy with zero signal about extraction regressions.
- The gate had to run without secrets: CI executes on every PR, and LLM keys must not be required (or spent) there. Replay mode plus prompt fingerprinting gives determinism while still forcing a live re-record whenever the prompt actually changes.

## Follow-ups

- Gate goes active only after the golden labels are hand-verified and the first baseline is written (see analysis entry follow-ups).
