"""
Constants for analysis/src/llm/client.py's retry/backoff loop.
"""

from __future__ import annotations

DEFAULT_MAX_RETRIES = 3

# Backoff wait (seconds) is (2 ** attempt) + BACKOFF_BASE_SECONDS; no sleep
# after the final attempt.
BACKOFF_BASE_SECONDS = 0.5
