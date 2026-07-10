"""
Text preparation for LLM prompt windows.

Two deterministic gates that run before an engine spends an LLM call:

- ``truncate_at_sentence`` clamps doc text to a stage's character budget at
  a sentence (or word) boundary instead of mid-token. A hard ``text[:N]``
  slice leaves a dangling fragment that invites the model to hallucinate a
  completion, and an evidence span straddling the cut fails the verbatim
  check downstream — the call is paid for and the output dropped.
- ``is_trivial_content`` answers prompt rule 6 ("@-mentions / links /
  hashtags only = NEUTRAL, low confidence") in code. If code can answer,
  code answers — the LLM call is skipped and the engine writes its
  deterministic result, which is also more consistent than the model's.
"""

from __future__ import annotations

import re

# Only accept a sentence boundary in the tail of the budget window —
# truncating a 2000-char budget down to a 300-char first sentence would
# throw away context the stage paid its budget for.
_MIN_BOUNDARY_FRACTION = 0.6

_SENTENCE_END_RE = re.compile(r"[.!?][)\"']?\s")

# Tokens that carry no analyzable stance on their own.
_URL_RE = re.compile(r"https?://\S+|www\.\S+", re.IGNORECASE)
_MENTION_RE = re.compile(r"@\w+")
_HASHTAG_RE = re.compile(r"#\w+")
# A substantive word: 2+ letters, so bare "RT", stray punctuation, and
# single-letter tokens don't count toward "has real content".
_SUBSTANTIVE_WORD_RE = re.compile(r"[a-zA-Z']{2,}")
_MIN_SUBSTANTIVE_WORDS = 3


def truncate_at_sentence(text: str, max_chars: int) -> str:
    """Clamp ``text`` to at most ``max_chars``, preferring a sentence
    boundary, then a whitespace boundary, then a hard cut.

    Boundaries are only honored past ``_MIN_BOUNDARY_FRACTION`` of the
    budget so a boundary-poor text still keeps most of its window.
    """
    if len(text) <= max_chars:
        return text
    window = text[:max_chars]
    floor = int(max_chars * _MIN_BOUNDARY_FRACTION)

    last_end = None
    for m in _SENTENCE_END_RE.finditer(window):
        last_end = m
    if last_end is not None and last_end.end() > floor:
        return window[: last_end.end()].rstrip()

    last_space = window.rfind(" ")
    if last_space > floor:
        return window[:last_space].rstrip()
    return window


def is_trivial_content(text: str) -> bool:
    """True when ``text`` has no substantive content once URLs, @-mentions,
    and hashtags are stripped — fewer than ``_MIN_SUBSTANTIVE_WORDS`` real
    words remain. Long-form docs are never trivial by construction."""
    if not text:
        return True
    stripped = _URL_RE.sub(" ", text)
    stripped = _MENTION_RE.sub(" ", stripped)
    stripped = _HASHTAG_RE.sub(" ", stripped)
    return len(_SUBSTANTIVE_WORD_RE.findall(stripped)) < _MIN_SUBSTANTIVE_WORDS
