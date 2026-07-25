"""
Constants for analysis/src/llm/client.py's retry/backoff loop and its
retryable-vs-non-retryable error classification.
"""

from __future__ import annotations

DEFAULT_MAX_RETRIES = 3

# Backoff wait (seconds) is (2 ** attempt) + BACKOFF_BASE_SECONDS; no sleep
# after the final attempt.
BACKOFF_BASE_SECONDS = 0.5

# HTTP/API status codes that can never succeed by retrying the same request:
# retrying does not refresh a credential, replenish quota, or fix a request
# body. Sourced from what TransportBackend implementations actually raise --
# google-genai's APIError.code (gemini.py) and requests.HTTPError.response.
# status_code (Ollama/OpenAICompat's response.raise_for_status()).
AUTH_STATUS_CODES = frozenset({401, 403})
MALFORMED_REQUEST_STATUS_CODE = 400

# 429 is ambiguous on its own: Gemini returns it both for a genuine
# short-term rate limit (retryable -- the request will likely succeed a few
# seconds later) and for quota/billing exhaustion (not retryable -- nothing
# changes until the owner tops up credits or a billing period rolls over).
# The status code can't tell these apart; the error message can, since
# Google's quota/billing errors name the cause in prose (e.g. "You exceeded
# your current quota, please check your plan and billing details").
QUOTA_STATUS_CODE = 429
QUOTA_MESSAGE_KEYWORDS = ("quota", "billing", "credit")
