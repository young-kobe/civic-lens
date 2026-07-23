"""
analysis/src/common/registry.py — domain/subreddit canonicalizers used by
`analysis/src/etl/documents.py` to resolve `corpus.entities` FKs
(outlet_entity_id / subreddit_entity_id).

The entity registry itself (`corpus.entities`, `corpus.entity_aliases`) is
curated directly in the database — seeded once by
`data/pg-migrations/0002_entity_registry_seed.sql` (2026-07-22) from the
retired YAML registries. See
docs/audit-trail/analysis/2026-07-22-db-native-entity-curation.md.
"""

from __future__ import annotations

from typing import Optional


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
