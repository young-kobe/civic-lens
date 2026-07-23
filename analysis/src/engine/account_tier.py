"""
analysis/src/engine/account_tier.py — deterministic curated account-tier
classifier (Postgres redesign Phase 6), closing the corpus.author_profiles
seeding gap left when registry_sync retired (see
docs/audit-trail/analysis/2026-07-22-db-native-entity-curation.md).
Mirrors the retired YAML loader's own matching logic (handle vs entity_key/
entity_aliases) exactly -- deterministic, no LLM, no analysis.runs row:
author_profiles is a corpus table, not an analysis result.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, List, Optional

from analysis.src.common import db
from analysis.src.common.logger import get_logger
from analysis.src.engine.constants import AFFILIATED_TIER, CURATED_LIST_METHOD, ELECTED_OFFICIAL_TIER

logger = get_logger(__name__)

# ELECTED_OFFICIAL_TIER/AFFILIATED_TIER/CURATED_LIST_METHOD consolidated
# into engine/constants.py (Postgres redesign Phase 6, constants-
# consolidation pass, 2026-07-23) -- imported above.
#
# corpus.entities.elected (added 2026-07-23) is the curated truth this
# module derives tier from: TRUE -> 'elected_official', FALSE ->
# 'affiliated' (e.g. cabinet secretaries, agency heads, party chairs --
# appointed/institutional accounts promoted from
# known_political_x_accounts.yaml or verified_officials.yaml alongside the
# genuinely elected ones -- see 0002_entity_registry_seed.sql's
# classification block). NULL shouldn't occur for kind='official' after
# seeding, but is handled as the cautious 'affiliated' default with a
# warning log rather than silently guessing elected status.

# active is filtered in Python, not SQL (mirrors common/entity_resolver.py's
# convention), so the exclusion rule is pure-testable without a database.
_SELECT_OFFICIAL_ENTITIES_SQL = """
    SELECT entity_id, entity_key, active, elected
    FROM corpus.entities
    WHERE kind = 'official'::corpus.entity_kind
"""

_SELECT_OFFICIAL_ALIASES_SQL = """
    SELECT ea.entity_id, ea.alias
    FROM corpus.entity_aliases ea
    JOIN corpus.entities e ON e.entity_id = ea.entity_id
    WHERE e.kind = 'official'::corpus.entity_kind
"""

# LEFT JOIN, not delete/reinsert: only authors with no author_profiles row
# at all are candidates, so a hand-edited existing row is never re-selected.
# platform='x' because entity_key/entity_aliases are X handles -- reddit
# authors (currently a documented no-op in etl/authors.py) have nothing
# handle-shaped to match against here.
_SELECT_UNCLASSIFIED_X_AUTHORS_SQL = """
    SELECT a.author_id, a.handle
    FROM corpus.authors a
    LEFT JOIN corpus.author_profiles p ON p.author_id = a.author_id
    WHERE p.author_id IS NULL
      AND a.platform = 'x'::corpus.platform
      AND a.handle IS NOT NULL
"""

_INSERT_AUTHOR_PROFILE_SQL = """
    INSERT INTO corpus.author_profiles (author_id, tier, method, entity_id, classified_at)
    VALUES (%(author_id)s, %(tier)s::corpus.author_tier,
            %(method)s::corpus.classification_method, %(entity_id)s, now())
"""


@dataclass(frozen=True)
class AccountTierResult:
    """Outcome of one classify_authors() pass. `matched` counts authors just
    given a curated_list corpus.author_profiles row (tier elected_official
    or affiliated) this pass; `unmatched` counts authors considered with no
    entity match -- they stay general_public by absence (the old-stack
    convention: only affirmative rows are ever stored, never a
    'general_public' row)."""

    matched: int
    unmatched: int


def _canonicalize_handle(raw: Optional[str]) -> Optional[str]:
    """``@POTUS`` -> ``potus``. Ported from the deleted
    common/registry.py::canonicalize_handle (retired with registry_sync.py,
    no remaining caller until this module) so matching uses the exact
    normalization 0002_entity_registry_seed.sql's entity_key/entity_aliases
    values were produced with."""
    if not raw:
        return None
    handle = raw.strip().lower()
    if handle.startswith("@"):
        handle = handle[1:]
    return handle or None


def _index_entities(entity_rows: List[dict], alias_rows: List[dict]) -> Dict[str, tuple]:
    """Pure: {canonicalized handle: (entity_id, elected)}, given
    corpus.entities rows (entity_id, entity_key, active, elected) and
    corpus.entity_aliases rows (entity_id, alias), both already restricted
    to kind='official' by the caller's SQL. `active` is checked here, not
    in SQL, so it stays testable without a database. entity_key wins over
    an alias that would map to the same key (setdefault)."""
    active_entities: Dict[int, Optional[bool]] = {}
    lookup: Dict[str, tuple] = {}
    for row in entity_rows:
        if not row["active"]:
            continue
        active_entities[row["entity_id"]] = row["elected"]
        key = _canonicalize_handle(row["entity_key"])
        if key:
            lookup[key] = (row["entity_id"], row["elected"])
    for row in alias_rows:
        if row["entity_id"] not in active_entities:
            continue
        key = _canonicalize_handle(row["alias"])
        if key:
            lookup.setdefault(key, (row["entity_id"], active_entities[row["entity_id"]]))
    return lookup


def _load_official_entity_lookup(conn: Any) -> Dict[str, tuple]:
    """One pair of queries per classify_authors() call (not per author)."""
    entity_rows = conn.execute(_SELECT_OFFICIAL_ENTITIES_SQL).fetchall()
    alias_rows = conn.execute(_SELECT_OFFICIAL_ALIASES_SQL).fetchall()
    return _index_entities(entity_rows, alias_rows)


def classify_authors(conn: Optional[Any] = None) -> AccountTierResult:
    """Give every corpus.authors row without a corpus.author_profiles row a
    curated_list profile when its handle matches an active kind='official'
    entity (by entity_key or entity_aliases) -- tier is 'elected_official'
    or 'affiliated' depending on the matched entity's curated `elected`
    column; leave it unmatched (implicit general_public) otherwise.

    Idempotent and self-healing by construction: the LEFT JOIN filter means
    an author that already has a profile row -- including one hand-edited
    directly in pgAdmin -- is never selected, so a re-run only ever
    classifies authors ETL'd since the last pass and never touches an
    existing row. Expected to run after etl/authors.py's sync in the
    pipeline order (Phase 7 orders it); author-scoped, not doc-scoped, which
    is why etl/queue.py deliberately excludes 'account_tier' from
    ops.task_queue.
    """
    if conn is not None:
        return _classify(conn)
    with db.connection() as owned_conn:
        return _classify(owned_conn)


def _tier_for(elected: Optional[bool], entity_id: int, handle: Optional[str]) -> str:
    """elected=TRUE -> elected_official, elected=FALSE -> affiliated.
    elected=NULL shouldn't occur for kind='official' post-seed (0002
    classifies every promoted/editorial official) -- treated as the
    cautious 'affiliated' default rather than over-claiming elected status,
    but logged loudly so a genuine data gap in curation is never silent."""
    if elected is None:
        logger.warning(
            f"account_tier: entity_id={entity_id} (handle={handle!r}) matched with "
            "elected=NULL; defaulting to affiliated (cautious tier) -- curate "
            "corpus.entities.elected"
        )
        return AFFILIATED_TIER
    return ELECTED_OFFICIAL_TIER if elected else AFFILIATED_TIER


def _classify(conn: Any) -> AccountTierResult:
    entity_lookup = _load_official_entity_lookup(conn)
    authors = conn.execute(_SELECT_UNCLASSIFIED_X_AUTHORS_SQL).fetchall()

    matched = 0
    unmatched = 0
    for author in authors:
        match = entity_lookup.get(_canonicalize_handle(author["handle"]))
        if match is None:
            unmatched += 1
            continue
        entity_id, elected = match
        conn.execute(
            _INSERT_AUTHOR_PROFILE_SQL,
            {
                "author_id": author["author_id"],
                "tier": _tier_for(elected, entity_id, author["handle"]),
                "method": CURATED_LIST_METHOD,
                "entity_id": entity_id,
            },
        )
        matched += 1

    logger.info(f"account_tier: matched={matched}, unmatched={unmatched}")
    return AccountTierResult(matched=matched, unmatched=unmatched)
