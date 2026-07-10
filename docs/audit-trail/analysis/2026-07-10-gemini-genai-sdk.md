# 2026-07-10 — Gemini client on the google-genai SDK

`llm/gemini.py` now wraps the ``google-genai`` package instead of
``google-generativeai``, which Google archived (the upstream repo is
literally renamed deprecated-generative-ai-python — no fixes, and new
models land only in the new SDK). Everything upstream is unchanged: both
backends still sit behind ``factory.get_llm_client()`` and share the JSON
schemas in ``llm/schemas.py``.

## What shipped

- `GeminiClient` uses ``genai.Client`` /
  ``client.models.generate_content`` with ``GenerateContentConfig``. The
  system prompt is now a real ``system_instruction`` role instead of
  being concatenated into the user turn with a ``---`` separator —
  matching how ``prompt_versions`` stores system and user templates
  separately (invariant B1). The permissive BLOCK_NONE safety settings
  (political content must not be refused by default), JSON response mime
  type, ``response_schema`` pass-through, retry/backoff (audit A-12), and
  token accounting all carry over unchanged.
- `analysis/requirements.txt`: ``google-genai>=1.0.0,<2`` replaces
  ``google-generativeai``.
- `tests/test_gemini_client.py`: first direct client coverage (the SDK is
  mocked via sys.modules, so the suite runs without the package): system
  role split, schema + safety config pass-through, retry-then-succeed,
  exhausted-retry RuntimeError, missing-SDK degradation to
  ``is_available=False``.

## Behavior notes

- The system-role split is the one observable change to what the model
  receives. Prompt versions were NOT bumped for it: the stored templates
  are identical; only the transport framing improved. If output drift is
  observed after deploy, bump ``TEXT_ANALYSIS_PROMPT_VERSION`` (and
  siblings) to segment the corpus.
- Not verified against the live API from the dev box (no key here);
  first prod `analyze` run after the image rebuild is the real check.
