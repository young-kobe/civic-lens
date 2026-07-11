"""
Shared evidence / document-presentation helpers.

Neutral home for the per-document presentation policy every aggregator needs:
external URL construction, human-readable source labels, body snippets, and
evidence-span sanitization. Previously these lived as private helpers in
``narrative.py`` (imported cross-module by bot / propaganda / review) and were
re-implemented inline in ``sentiment.py``. Centralizing here removes the
sibling-to-sibling import chain and keeps one implementation of each policy.
"""

import re
from typing import List, Optional


# Longest body snippet surfaced as the Headline-column fallback for social
# posts with no title. Shared so the narrative supporting-docs SQL substr and
# the snippet function agree on one bound.
SNIPPET_MAX_CHARS = 120

# Cap on evidence spans kept per sample after de-duplication.
MAX_EVIDENCE_PER_SAMPLE = 5


def build_doc_url(
    source_type: Optional[str],
    domain: Optional[str],
    ident: Optional[str],
    x_handle: Optional[str] = None,
) -> Optional[str]:
    """External URL for a document: news rows store the URL in ``ident``;
    reddit rows synthesize from subreddit + post id; x_post rows synthesize
    from handle + tweet id. Returns None when no auditable URL can be formed."""
    if not ident:
        return None
    if ident.startswith(("http://", "https://")):
        return ident
    if source_type and source_type.startswith("reddit"):
        post_id = ident.replace("t3_", "").replace("t1_", "")
        return f"https://reddit.com/r/{domain or 'all'}/comments/{post_id}"
    if source_type == "x_post" and x_handle:
        return f"https://x.com/{x_handle}/status/{ident}"
    return None


def build_source_label(
    source_type: Optional[str],
    domain: Optional[str],
    x_handle: Optional[str],
) -> str:
    """Human-readable source label for a document row (e.g. ``News · nyt.com``,
    ``X · @handle``, ``Reddit · r/politics``). Degrades to a bare platform name
    when the domain/handle is missing."""
    if source_type == "news":
        return f"News · {domain}" if domain else "News"
    if source_type == "x_post":
        return f"X · @{x_handle}" if x_handle else "X"
    if source_type in ("reddit_post", "reddit_comment") and domain:
        return f"Reddit · r/{domain}"
    if source_type in ("reddit_post", "reddit_comment"):
        return "Reddit"
    return source_type or "unknown"


def text_snippet(text: Optional[str], limit: int = SNIPPET_MAX_CHARS) -> Optional[str]:
    """One-line preview of a doc's body, used as the Headline-column fallback
    for social posts that have no title. Collapses whitespace and truncates."""
    if not text:
        return None
    collapsed = " ".join(text.split())
    if not collapsed:
        return None
    if len(collapsed) > limit:
        return collapsed[: limit - 3].rstrip() + "..."
    return collapsed


def sanitize_evidence(spans: list, max_count: int = MAX_EVIDENCE_PER_SAMPLE) -> List[str]:
    """Deduplicate + strip placeholders from an LLM's evidence_spans list.

    Placeholder text (``exact quote``-style) and duplicates are dropped;
    short spans (< 4 chars) are skipped. Single-word and ``@mention``
    spans are preserved — the LLM can legitimately point at a single
    loaded word like ``Satanists`` as evidence.
    """
    seen: set = set()
    result: List[str] = []
    placeholder_pattern = re.compile(r"^exact quote", re.IGNORECASE)
    for span in spans:
        if not isinstance(span, str):
            continue
        trimmed = span.strip()
        if not trimmed or len(trimmed) < 4:
            continue
        if placeholder_pattern.match(trimmed):
            continue
        key = trimmed.lower()
        if key in seen:
            continue
        seen.add(key)
        result.append(trimmed)
        if len(result) >= max_count:
            break
    return result
