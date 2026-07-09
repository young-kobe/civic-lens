# 2026-07-09 — Deterministic gates and boundary-aware truncation for LLM context

The three LLM-driven engines (text analysis, claim extraction, propaganda
detection) now share deterministic text-preparation gates in
`analysis/src/engine/text_prep.py`: character budgets are clamped at
sentence boundaries instead of hard `text[:N]` slices, and docs whose
content is only @-mentions, links, or hashtags are classified in code
without spending an LLM call. Reasoning fields in the text-analysis output
are bounded to one sentence each, cutting output-token cost.

## What shipped

- `analysis/src/engine/text_prep.py` — new module with two functions:
  - `truncate_at_sentence(text, max_chars)`: clamps to the stage budget at
    a sentence boundary (fallback: word boundary, then hard cut), honoring
    boundaries only past 60% of the budget so boundary-poor text keeps its
    window.
  - `is_trivial_content(text)`: True when fewer than 3 substantive words
    remain after stripping URLs, @-mentions, and hashtags.
- `analyzer.py` — trivial docs short-circuit to the heuristic path
  (`inference_method='heuristic'`, NEUTRAL, reasoning states the skip);
  LLM input is `truncate_at_sentence(text, TEXT_ANALYSIS_MAX_CHARS=2000)`.
- `claim_extractor.py` — trivial docs return a clean empty result (an
  `ai_outputs` row is written; not flagged `extraction_failed`, so no
  re-queue); LLM input clamped via `CLAIM_TEXT_MAX_CHARS=2000`.
- `propaganda_detector.py` — the existing 800-char clamp now cuts at a
  sentence boundary. The loaded-language pre-filter is unchanged and runs
  after the clamp, as before.
- `llm/prompts.py` — `TEXT_ANALYSIS_SYSTEM_PROMPT` rule 5 bounds
  `sentiment_reasoning` and `favorability_reasoning` to one sentence each.
  `TEXT_ANALYSIS_PROMPT_VERSION` bumped `text-analysis-v4` ->
  `text-analysis-v5` per the AI-output contract.
- `analysis/tests/test_text_prep.py` — 9 tests pinning: no mid-word cuts,
  boundary-fraction behavior, triviality classification, and that trivial
  content produces zero LLM calls (exploding-client stand-in).

## Why

- A hard `text[:2000]` slice leaves a dangling fragment the model may
  hallucinate a completion for, and an evidence span straddling the cut
  fails the verbatim validator (invariant B2) — the call is paid for and
  the output dropped. Sentence-boundary clamping removes the straddle
  class entirely.
- Prompt rule 6 in both text-analysis and claims instructed the LLM to
  return NEUTRAL/empty for mention-and-link-only content — paying a model
  to compute what a regex answers. The propaganda stage already had this
  pattern (`_has_loaded_language`); the gate extends it to the other two
  LLM stages. On social-heavy ingests this eliminates a meaningful
  fraction of calls and is more consistent than the model.
- Reasoning fields were unbounded; output tokens are the expensive kind
  on the hosted backend. Audit value survives at one sentence.

## Caching note (no code change)

`gemini.py` concatenates `system_prompt + "\n\n---\n\n" + user_prompt`, so
the static system prompt is already the stable prefix of every request —
the shape implicit prefix-caching needs. The configured default
`gemini-2.0-flash` does not offer implicit caching; moving the configured
model to a 2.5-generation Flash (a config change, `CIVIC_` settings) picks
up the cached-input discount with no code change. Recorded here as an ops
lever, not a code task.

## Follow-ups

- Calibration report, metamorphic suite, cross-backend disagreement
  sampling, propaganda golden set: `docs/todos/eval-expansion.md`.
- Claim-extraction eval recordings remain fingerprint-valid: the claims
  prompt, schema, and version are untouched; truncation is code-side and
  golden inputs are shorter than the budget.
