# 2026-07-25 — LLM client stops retrying errors that cannot succeed on retry

`analysis/src/llm/client.py`'s `LLMClient.complete()` used to retry every
exception from `complete_once()` identically — a network blip, a 5xx, a
malformed prompt, an expired API key, and a fully depleted Gemini prepayment
balance all got the same `max_retries` (3) attempts with backoff. Layered
under `scheduler/stages.py`'s own requeue (`MAX_TASK_ATTEMPTS = 3`), one
document could burn up to 9 fully-billed Gemini calls during a credit
outage — the owner exhausted prepayment credits three times during a
full-corpus recompute this way. `complete()` now classifies the exception
before deciding to retry: authentication/authorization failures, quota/
billing exhaustion, and malformed requests fail on the first attempt.

## What shipped

- `analysis/src/llm/constants.py`: `AUTH_STATUS_CODES` (401, 403),
  `MALFORMED_REQUEST_STATUS_CODE` (400), `QUOTA_STATUS_CODE` (429),
  `QUOTA_MESSAGE_KEYWORDS` (`"quota"`, `"billing"`, `"credit"`).
- `analysis/src/llm/client.py`: `_status_code(exc)` duck-types a status code
  off an exception; `_non_retryable_reason(exc)` maps that code (plus, for
  429s, message-content keywords) to a reason string or `None`.
  `LLMClient.complete()`'s except clause calls it first — a non-`None`
  reason logs plainly that the call was **not retried** and why, then raises
  immediately without consuming another attempt. Everything else falls
  through to the existing retry/backoff loop unchanged.
- `analysis/tests/test_llm_client.py`: 6 new tests. Quota-exhaustion and
  auth errors each cause exactly one `complete_once()` call before raising;
  a malformed-request error does the same; a genuine 429 rate limit
  (no quota/billing/credit wording), a bare 5xx, and a status-code-less
  network error (`TimeoutError`) all still retry to `max_retries`. These
  exist so a future refactor that reintroduces blanket retry on billing
  errors fails the suite instead of shipping quietly.

## Why

**429 is ambiguous, so it needs a second signal.** Gemini returns HTTP 429
for both a short-term rate limit (retryable — the same request likely
succeeds seconds later) and quota/billing exhaustion (not retryable —
nothing changes until the owner tops up credits). The status code alone
can't distinguish them; Google's quota/billing errors name the cause in
prose (e.g. "You exceeded your current quota, please check your plan and
billing details"), so the classifier only treats a 429 as non-retryable
when that wording is present. An unadorned 429 still retries — this
protects the transient-rate-limit case the owner did not ask to change.

**SchemaValidationError stays retryable, deliberately.** It's not an
HTTP/API error at all — it's raised locally by `parse_json_response()` when
a response fails schema validation, and has neither a `.code` nor a
`.response.status_code`, so `_non_retryable_reason` returns `None` for it
without any special-casing. This was a deliberate choice, not an oversight:
`client.py`'s existing docstring already documents retrying it as
intentional (a re-prompt can produce valid JSON), and the owner's stated
concern is auth/quota cost, not schema noise. Nothing about schema
validation failures suggests the same request would keep failing, so
folding it into "retryable" needed no new reasoning — it already matched
the contract.

**The classifier is written against the SDK's real error surface, not
guessed names.** `google.genai.errors.APIError` (raised by `gemini.py`'s
`complete_once()` via the `google-genai` SDK,
`analysis/.venv/lib/python3.10/site-packages/google/genai/errors.py`) has
real `.code` (int), `.status` (string), and `.message` attributes, with
`ClientError`/`ServerError` subclasses split on 4xx/5xx. Ollama/OpenAICompat
raise `requests.exceptions.HTTPError` via `response.raise_for_status()`,
which exposes `.response.status_code`. `_status_code()` duck-types both
shapes (`getattr(exc, "code", ...)` then `getattr(exc.response,
"status_code", ...)`) rather than importing `google.genai.errors` directly,
so `client.py` — which wraps all three backends through the same
`TransportBackend` protocol — stays decoupled from any one SDK. Nothing here
is a guess: both attribute shapes were confirmed by reading the installed
`google-genai` package and the two `requests`-based backends before writing
the classifier.

## The dead-retry-loop premise was wrong — it's live in production

The task assumed `GeminiClient.complete()`'s own retry loop (`gemini.py`
lines ~129-176) was dead code, unreachable because "the live path goes
through `LLMClient.complete` -> `complete_once`." That's true only for the
Postgres-redesign engines (`engine/text.py`, `targets.py`, `claims.py`,
`propaganda.py`, `bot_detection.py`, wired through `scheduler/stages.py`,
which builds `LLMClient(backend)`). It is false for the retired
`scheduler/job_runner.py` stack, which **still runs in production**
(confirmed against its own docstrings, e.g.
`docs/audit-trail/analysis/2026-07-25-llm-only-judgments.md`: "the retired
`engine/propaganda_detector.py` and `engine/analyzer.py` ... still run in
production until the Phase 11 timer flip"). `job_runner.py` constructs the
old engine classes (`bot.py`'s `HybridBotDetector`, `analyzer.py`'s
`Analyzer`, `claim_extractor.py`'s `ClaimExtractor`,
`target_extractor.py`'s `TargetSentimentExtractor`,
`propaganda_detector.py`'s `PropagandaDetector`), each of which calls
`from analysis.src.llm import get_llm_client; self._llm_client =
get_llm_client()` — `llm/factory.py`'s function, which returns the **raw**
`GeminiClient`/`OllamaClient`/`OpenAICompatClient`, not the `LLMClient`
wrapper. These engines then call `self._llm_client.complete(...)` directly
(e.g. `bot.py:355`), which for the Gemini backend is exactly
`GeminiClient.complete()` — the loop the task called dead.

Per the task's own fallback instruction ("If job_runner reaches it, LEAVE
IT and say so"), the loop in `gemini.py` was left untouched — removing it
would break a stack that is genuinely in production. It was **not**
extended with the new classifier either: that would mean rewriting
`GeminiClient.complete()`'s retry loop, which sits one level below the
old engine classes in `engine/**` — out of this task's assigned files, and
those old engines are an explicit no-edit zone here. This is reported
rather than silently fixed or silently left broken:

**Residual risk**: as things stand, `job_runner.py`'s stack (bot detection,
sentiment/favorability, claims, targets, propaganda — whichever of those
five haven't yet been cut over to `stages.py`) still blindly retries
auth/quota/malformed-request errors 3x per call via `GeminiClient.
complete()`'s untouched loop, with no requeue multiplier on top (unlike
`stages.py`, `job_runner.py` doesn't call `LLMClient.complete()` at all, so
today's fix does not reach it). A future credit outage during a
`job_runner.py`-driven run would still see the pre-fix multiplier for those
five engines. This is a `scheduler/**`/`engine/**` concern per this task's
boundaries and is not fixed here.

## Follow-ups

- Whoever owns `scheduler/**`/`engine/**`: either port the five retired
  engines (`bot.py`, `analyzer.py`, `target_extractor.py`,
  `claim_extractor.py`, `propaganda_detector.py`) onto the wrapped
  `LLMClient` (as the Phase-6 engines already are), or apply the same
  retryable/non-retryable classification to `GeminiClient.complete()`'s own
  loop directly. Until the Phase 11 timer flip retires `job_runner.py`
  outright, this is a live cost-exposure gap for the same failure mode this
  change closes on the new stack.
- `ollama.py`/`openai_compat.py`'s own `complete()` retry loops have the
  identical blanket-retry shape (unreachable from `stages.py`, same
  reachability question as `gemini.py`'s loop, for whichever old engines run
  with `CIVIC_LLM_BACKEND=ollama`/`openai_compat`). Not investigated here —
  the owner's incident was specifically Gemini billing.
