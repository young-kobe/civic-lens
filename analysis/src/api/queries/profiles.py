"""
Entity-profile assembly restoring the pre-Postgres UI's EntityProfile
contract: `fetch_entity_profiles()` batches corpus.entities rows into
`EntityProfileModel`s, plus the sampled-account and catch-all sentinel
profiles for docs with no registry entity. See
docs/audit-trail/api/2026-07-27-entity-profile-restoration.md.
"""

from __future__ import annotations

from datetime import date
from typing import Any, Dict, Iterable, Mapping, Optional

from analysis.src.api.models.common import EntityProfileModel

# --------------------------------------------------------------------------- #
#  Vocabulary mapping: corpus.entities.kind (+ editorial) -> old UI kind      #
# --------------------------------------------------------------------------- #

_LEAN_BEARING_KINDS = ("outlet", "subreddit")
_PARTY_BEARING_KINDS = ("official", "collective")

# Sentinel keys for docs that route to a tier but match no registry entity
# and no active author -- shared by every panel that needs a catch-all
# card, ported verbatim from the pre-Postgres
# reporting/entity_registry.py so the text can't drift across panels.
CATCH_ALL_OUTLETS = "other-outlets"
CATCH_ALL_X_USERS = "other-x-users"
CATCH_ALL_SUBREDDITS = "other-subreddits"

_CATCH_ALL_PROFILES = {
    CATCH_ALL_OUTLETS: (
        "Other news outlets",
        "News docs whose domain is not in the tracked outlet registry.",
    ),
    CATCH_ALL_X_USERS: (
        "Other X users",
        "X posts whose author is not in the tracked officials registry, the "
        "curated political-accounts list, or active enough in this window "
        "for an individual card.",
    ),
    CATCH_ALL_SUBREDDITS: (
        "Other subreddits",
        "Reddit posts whose subreddit is not in the tracked subreddit registry.",
    ),
}

_SAMPLED_BIO_MAX_CHARS = 140


def _entity_ui_kind(kind: str, editorial: bool) -> str:
    """corpus.entities (kind, editorial) -> the old UI's five-way kind
    vocabulary. editorial official -> 'official'; non-editorial official
    (promoted from known_political_x_accounts.yaml) -> 'account'; every
    collective -> 'official'; outlet/subreddit pass through unchanged."""
    if kind == "official":
        return "official" if editorial else "account"
    if kind == "collective":
        return "official"
    if kind in ("outlet", "subreddit"):
        return kind
    raise ValueError(f"unknown corpus.entities.kind: {kind!r}")


def _map_entity_row(row: Mapping[str, Any]) -> EntityProfileModel:
    """Pure row -> EntityProfileModel mapping (no DB access), so the
    vocabulary/name-trap decisions are unit-testable without a database.

    The source_citation column feeds two different model fields depending
    on kind: `lean_source` (the lean's citation) for outlet/subreddit rows,
    `bio_source` (the party's citation) for official/collective rows --
    never both, and never `entities.lean` (the flattened join-axis enum,
    which this model does not expose)."""
    ui_kind = _entity_ui_kind(row["kind"], row["editorial"])
    lean_bearing = row["kind"] in _LEAN_BEARING_KINDS
    party_bearing = row["kind"] in _PARTY_BEARING_KINDS

    term_start: Optional[str] = None
    if row.get("term_start") is not None:
        raw_term_start = row["term_start"]
        term_start = (
            raw_term_start.isoformat()
            if isinstance(raw_term_start, date)
            else str(raw_term_start)
        )

    return EntityProfileModel(
        kind=ui_kind,
        key=row["entity_key"],
        display_name=row["display_name"],
        blurb=row.get("blurb") or "",
        lean=row["lean_source"] if lean_bearing else None,
        lean_source=row["source_citation"] if lean_bearing else None,
        owner=row.get("owner"),
        founded=row.get("founded"),
        circulation_note=row.get("circulation_note"),
        office=row.get("role_title"),
        party=row["lean_source"] if party_bearing else None,
        term_start=term_start,
        bio_source=row["source_citation"] if party_bearing else None,
        subscriber_count_proxy=row.get("subscriber_count_proxy"),
        account_type=row.get("account_type"),
        entity_id=row.get("entity_id"),
    )


# --------------------------------------------------------------------------- #
#  Batched fetch                                                              #
# --------------------------------------------------------------------------- #

_ENTITY_PROFILE_ROWS_SQL = """
    SELECT entity_id, entity_key, kind, editorial, display_name, blurb,
           role_title, term_start, owner, source_citation, lean_source,
           founded, circulation_note, subscriber_count_proxy, account_type
    FROM corpus.entities
    WHERE entity_id = ANY(%(entity_ids)s)
"""


def fetch_entity_profiles(conn, entity_ids: Iterable[int]) -> Dict[int, EntityProfileModel]:
    """One batched SELECT over corpus.entities for every id in
    ``entity_ids``, mapped through `_map_entity_row`. Missing ids are
    simply absent from the returned dict -- callers decide whether that's
    an error or a catch-all fallback."""
    ids = list(entity_ids)
    if not ids:
        return {}
    rows = conn.execute(_ENTITY_PROFILE_ROWS_SQL, {"entity_ids": ids}).fetchall()
    return {row["entity_id"]: _map_entity_row(row) for row in rows}


# --------------------------------------------------------------------------- #
#  Non-registry profiles: sampled authors and catch-all sentinels           #
# --------------------------------------------------------------------------- #

def sampled_account_profile(
    handle: str,
    display_name: Optional[str] = None,
    description: Optional[str] = None,
) -> EntityProfileModel:
    """Profile for an unmatched-but-active X author promoted out of the
    "Other X users" catch-all. Built entirely from the author's own public
    X profile -- we know nothing editorial about them, so there is no lean,
    no party; the blurb says exactly where the card came from. Mirrors
    the pre-Postgres `entity_registry.sampled_account_profile()`."""
    clean_bio = " ".join((description or "").split())
    if len(clean_bio) > _SAMPLED_BIO_MAX_CHARS:
        clean_bio = clean_bio[: _SAMPLED_BIO_MAX_CHARS - 1].rstrip() + "…"
    blurb = (
        (clean_bio + " — " if clean_bio else "")
        + "one of the most active X accounts in our political sample. Not "
        "in our tracked registries; bio is their own."
    )
    return EntityProfileModel(
        kind="account",
        key=handle,
        display_name=display_name or f"@{handle}",
        blurb=blurb,
        account_type="sampled",
    )


def catch_all_profile(key: str) -> EntityProfileModel:
    """Sentinel profile for one of the three catch-all buckets ('other-
    outlets', 'other-x-users', 'other-subreddits'). Display names/blurbs
    are copied verbatim from the pre-Postgres
    entity_registry.py/aggregators/sentiment/entities.py."""
    display_name, blurb = _CATCH_ALL_PROFILES[key]
    return EntityProfileModel(kind="catch_all", key=key, display_name=display_name, blurb=blurb)
