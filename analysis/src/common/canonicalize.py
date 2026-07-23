"""
Shared NEW-STACK canonicalization functions: URL, news domain, subreddit,
and entity-name normalization. Consolidated from engine/citations.py,
common/registry.py, and common/entity_resolver.py so callers share one
definition each instead of parallel copies.
"""

from __future__ import annotations

from typing import Optional
from urllib.parse import urlparse, urlunparse

# Trailing punctuation that often bleeds into URLs captured from prose.
_URL_TRAILING_PUNCT = ".,;:!?"

# Leading title tokens stripped before entity-name lookup
# ("President Trump" -> "trump").
_TITLE_PREFIXES = (
    "president", "vice president", "vp", "senator", "sen.", "sen",
    "rep.", "rep", "representative", "speaker", "secretary", "sec.", "sec",
    "leader", "majority leader", "minority leader", "governor", "gov.", "gov",
    "attorney general", "director", "chairman", "chair", "dr.", "dr",
)


def canonicalize_url(url: str) -> str:
    """Normalize a URL for matching against corpus.documents.natural_key
    (news natural_key is url_canon). Lowercases scheme/host, strips a
    leading 'www.', drops trailing slash and fragment, keeps query."""
    raw = url.strip().rstrip(_URL_TRAILING_PUNCT)
    try:
        parsed = urlparse(raw)
    except ValueError:
        return raw
    if not parsed.scheme or not parsed.netloc:
        return raw

    host = parsed.netloc.lower()
    if host.startswith("www."):
        host = host[4:]
    path = parsed.path.rstrip("/") or "/"
    return urlunparse((parsed.scheme.lower(), host, path, parsed.params, parsed.query, ""))


def canonicalize_news_domain(raw: Optional[str]) -> Optional[str]:
    """``www.NYTimes.com`` -> ``nytimes.com``."""
    if not raw:
        return None
    d = raw.strip().lower()
    if d.startswith("www."):
        d = d[4:]
    return d or None


def canonicalize_subreddit(raw: Optional[str]) -> Optional[str]:
    """``r/Politics`` / ``/r/Politics`` / ``Politics`` -> ``politics``."""
    if not raw:
        return None
    s = raw.strip().lower()
    if s.startswith("/r/"):
        s = s[3:]
    elif s.startswith("r/"):
        s = s[2:]
    return s or None


def canonicalize_entity_name(raw: Optional[str]) -> Optional[str]:
    """Canonicalize a free-text entity name for lookup: lowercase, strip a
    leading '@', trailing punctuation, a possessive 's, a leading "the ",
    and a leading title token. Same normalization rules as
    reporting/entity_registry.py::normalize_target_name, ported (not
    imported -- that module is old-stack/YAML-tied)."""
    if not raw:
        return None
    s = raw.strip().lower().lstrip("@")
    s = s.rstrip(".,:;!?")
    if s.endswith("'s") or s.endswith("’s"):
        s = s[:-2]
    if s.startswith("the "):
        s = s[4:]
    for prefix in _TITLE_PREFIXES:
        if s.startswith(prefix + " "):
            s = s[len(prefix) + 1:]
            break
    s = " ".join(s.split())
    return s or None
