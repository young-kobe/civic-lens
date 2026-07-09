"""
Golden-set evaluation harness for Civic Lens LLM stages.

Scores the live claim-extraction pipeline against hand-verified golden
examples (analysis/evals/golden/) and gates CI on a committed baseline
(analysis/evals/baseline_claims.json). See docs/EVALS.md.
"""
