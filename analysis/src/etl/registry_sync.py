"""
analysis/src/etl/registry_sync.py -- syncs `data/*.yaml` (the three
editorial registries plus the curated political-accounts file) into
`corpus.entities` / `corpus.entity_aliases` / `corpus.author_profiles`.

Editorial entities are upserted `editorial=true` and never DELETEd (an
entity missing from the current pass is flipped `active=false` instead, so
historical FKs keep resolving). Every curated political account is also
promoted to its own `entities` row (`editorial=false`) unless an editorial
entity already covers it. `author_profiles` links each curated account to
its author once that author exists in `corpus.authors`, skipping (not
guessing) until then.

Political lean is flattened via `analysis/src/common/registry.py`'s
constants and is curated data, never derived from or fed into an LLM.
Idempotency ties into `IS DISTINCT FROM` upserts: an unchanged re-sync
touches zero rows. Derivation and design rationale:
docs/audit-trail/analysis/2026-07-22-pg-lean-unification-registry-sync.md
"""

from __future__ import annotations

import logging
import os
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

from psycopg import Connection

from analysis.src.common import registry as reg
from analysis.src.common.db import connection

logger = logging.getLogger(__name__)

_DEFAULT_DATA_DIR = Path(__file__).resolve().parents[3] / "data"
_OUTLETS_FILENAME = "news_outlets.yaml"
_OFFICIALS_FILENAME = "verified_officials.yaml"
_SUBREDDITS_FILENAME = "major_subreddits.yaml"
_POLITICAL_ACCOUNTS_FILENAME = "known_political_x_accounts.yaml"

ELECTED_OFFICIAL_TIER = "elected_official"
CURATED_LIST_METHOD = "curated_list"

# Content columns compared with IS DISTINCT FROM to decide whether an
# entities/author_profiles row actually changed. `synced_at`/`classified_at`
# are deliberately excluded from this list (they always go in SET, so a real
# change still bumps them) but their presence must never by itself trigger
# an update.
_ENTITY_CONTENT_COLUMNS = (
    "kind", "display_name", "blurb", "role_title", "term_start", "owner",
    "source_citation", "lean", "lean_source", "active", "editorial",
)
_PROFILE_CONTENT_COLUMNS = ("tier", "method", "entity_id", "notes")


def _data_dir() -> Path:
    """Registry YAML directory. `CIVIC_ENTITY_REGISTRY_DIR` overrides it for tests."""
    return Path(os.environ.get("CIVIC_ENTITY_REGISTRY_DIR") or _DEFAULT_DATA_DIR)


def sync_registry(data_dir: Optional[Path] = None) -> Dict[str, int]:
    """Sync all YAML registries into Postgres in one connection. Idempotent:
    see module docstring. Returns per-kind counts for observability."""
    root = data_dir or _data_dir()
    now = datetime.now(timezone.utc)
    with connection() as conn:
        outlet_keys = _sync_outlets(conn, reg.load_outlets(root / _OUTLETS_FILENAME), now)
        official_keys = _sync_officials(conn, reg.load_officials(root / _OFFICIALS_FILENAME), now)
        subreddit_keys = _sync_subreddits(conn, reg.load_subreddits(root / _SUBREDDITS_FILENAME), now)
        # Built before any promoted entity exists, so this is exactly "which
        # handles already have an editorial entity" for the collision check below.
        editorial_lookup = _load_official_entity_lookup(conn, editorial_only=True)
        accounts = reg.load_political_accounts(root / _POLITICAL_ACCOUNTS_FILENAME)
        promoted_keys, promotion_deduped = _sync_promoted_officials(conn, accounts, editorial_lookup, now)
        deactivated = (
            _deactivate_missing(conn, "outlet", outlet_keys, now)
            + _deactivate_missing(conn, "official", official_keys | promoted_keys, now)
            + _deactivate_missing(conn, "subreddit", subreddit_keys, now)
        )
        profiles_synced, profiles_skipped = _sync_author_profiles(conn, accounts, now)
    counts = {
        "outlets": len(outlet_keys),
        "officials": len(official_keys),
        "subreddits": len(subreddit_keys),
        "officials_promoted": len(promoted_keys),
        "officials_promotion_editorial_dedup": promotion_deduped,
        "deactivated": deactivated,
        "author_profiles_synced": profiles_synced,
        "author_profiles_skipped": profiles_skipped,
    }
    logger.info("registry_sync complete: %s", counts)
    return counts


def _sync_outlets(conn: Connection, records: List[reg.OutletRecord], synced_at: datetime) -> set:
    """Upsert every outlet entity + its aliases. Returns the set of
    entity_keys synced (used to compute the deactivation set)."""
    keys = set()
    for rec in records:
        lean = reg.flatten_lean_scale(rec.raw_lean, context=f"outlet {rec.entity_key}")
        entity_id = _upsert_entity(
            conn, entity_key=rec.entity_key, kind="outlet",
            display_name=rec.display_name, blurb=rec.blurb,
            role_title=None, term_start=None, owner=rec.owner,
            source_citation=rec.source_citation, lean=lean,
            lean_source=rec.raw_lean, editorial=True, synced_at=synced_at,
        )
        _sync_aliases(conn, entity_id, rec.aliases)
        keys.add(rec.entity_key)
    return keys


def _sync_officials(conn: Connection, records: List[reg.OfficialRecord], synced_at: datetime) -> set:
    """Upsert every official entity + its aliases. Party -> lean mapping is
    lossless for the enum's purposes (the raw code survives in lean_source).
    Returns the set of entity_keys synced."""
    keys = set()
    for rec in records:
        lean = reg.flatten_party_code(rec.raw_party, context=f"official {rec.entity_key}")
        entity_id = _upsert_entity(
            conn, entity_key=rec.entity_key, kind="official",
            display_name=rec.display_name, blurb=rec.blurb,
            role_title=rec.role_title, term_start=rec.term_start, owner=None,
            source_citation=rec.source_citation, lean=lean,
            lean_source=rec.raw_party, editorial=True, synced_at=synced_at,
        )
        _sync_aliases(conn, entity_id, rec.aliases)
        keys.add(rec.entity_key)
    return keys


def _sync_subreddits(conn: Connection, records: List[reg.SubredditRecord], synced_at: datetime) -> set:
    """Upsert every subreddit entity (no aliases in this YAML). Returns the
    set of entity_keys synced."""
    keys = set()
    for rec in records:
        lean = reg.flatten_lean_scale(rec.raw_lean, context=f"subreddit {rec.entity_key}")
        _upsert_entity(
            conn, entity_key=rec.entity_key, kind="subreddit",
            display_name=rec.display_name, blurb=rec.blurb,
            role_title=None, term_start=None, owner=None,
            source_citation=rec.source_citation, lean=lean,
            lean_source=rec.raw_lean, editorial=True, synced_at=synced_at,
        )
        keys.add(rec.entity_key)
    return keys


def _upsert_entity(
    conn: Connection, *, entity_key: str, kind: str, display_name: str, blurb: str,
    role_title: Optional[str], term_start: Optional[str], owner: Optional[str],
    source_citation: Optional[str], lean: str, lean_source: Optional[str],
    editorial: bool, synced_at: datetime,
) -> int:
    """Upsert one `corpus.entities` row keyed by `entity_key`. No-op when
    every content column already matches. `editorial` flips in place if a
    later upsert (e.g. a promoted official later added to
    `verified_officials.yaml`) disagrees with the stored value."""
    where_clause = " OR ".join(
        f"corpus.entities.{c} IS DISTINCT FROM EXCLUDED.{c}" for c in _ENTITY_CONTENT_COLUMNS
    )
    params = {
        "entity_key": entity_key, "kind": kind, "display_name": display_name,
        "blurb": blurb, "role_title": role_title, "term_start": term_start,
        "owner": owner, "source_citation": source_citation, "lean": lean,
        "lean_source": lean_source, "editorial": editorial, "synced_at": synced_at,
    }
    row = conn.execute(
        f"""
        INSERT INTO corpus.entities
            (entity_key, kind, display_name, blurb, role_title, term_start,
             owner, source_citation, lean, lean_source, active, editorial, synced_at)
        VALUES (%(entity_key)s, %(kind)s::corpus.entity_kind, %(display_name)s,
                %(blurb)s, %(role_title)s, %(term_start)s::date, %(owner)s,
                %(source_citation)s, %(lean)s::corpus.political_lean,
                %(lean_source)s, true, %(editorial)s, %(synced_at)s)
        ON CONFLICT (entity_key) DO UPDATE SET
            kind = EXCLUDED.kind, display_name = EXCLUDED.display_name,
            blurb = EXCLUDED.blurb, role_title = EXCLUDED.role_title,
            term_start = EXCLUDED.term_start, owner = EXCLUDED.owner,
            source_citation = EXCLUDED.source_citation, lean = EXCLUDED.lean,
            lean_source = EXCLUDED.lean_source, active = EXCLUDED.active,
            editorial = EXCLUDED.editorial, synced_at = EXCLUDED.synced_at
        WHERE {where_clause}
        RETURNING entity_id
        """,
        params,
    ).fetchone()
    if row is not None:
        return row["entity_id"]
    existing = conn.execute(
        "SELECT entity_id FROM corpus.entities WHERE entity_key = %(entity_key)s",
        {"entity_key": entity_key},
    ).fetchone()
    return existing["entity_id"]


def _sync_aliases(conn: Connection, entity_id: int, aliases: Sequence[str]) -> None:
    """Reconcile `corpus.entity_aliases` for one entity: insert aliases new
    to this sync, delete aliases no longer present in the YAML. Nothing else
    FKs to entity_aliases, so a targeted diff (rather than delete-then-
    reinsert) is safe and avoids touching unchanged rows."""
    existing = {
        row["alias"] for row in conn.execute(
            "SELECT alias FROM corpus.entity_aliases WHERE entity_id = %(entity_id)s",
            {"entity_id": entity_id},
        ).fetchall()
    }
    wanted = set(aliases)
    for alias in wanted - existing:
        conn.execute(
            "INSERT INTO corpus.entity_aliases (entity_id, alias) "
            "VALUES (%(entity_id)s, %(alias)s) ON CONFLICT DO NOTHING",
            {"entity_id": entity_id, "alias": alias},
        )
    for alias in existing - wanted:
        conn.execute(
            "DELETE FROM corpus.entity_aliases WHERE entity_id = %(entity_id)s AND alias = %(alias)s",
            {"entity_id": entity_id, "alias": alias},
        )


def _deactivate_missing(conn: Connection, kind: str, seen_keys: set, synced_at: datetime) -> int:
    """Flip `active=false` for any entity of `kind` whose entity_key is no
    longer present in the current YAML pass -- never DELETE, so historical
    FKs keep resolving. Returns the number deactivated."""
    rows = conn.execute(
        "SELECT entity_key FROM corpus.entities WHERE kind = %(kind)s::corpus.entity_kind AND active = true",
        {"kind": kind},
    ).fetchall()
    stale = [r["entity_key"] for r in rows if r["entity_key"] not in seen_keys]
    for entity_key in stale:
        conn.execute(
            "UPDATE corpus.entities SET active = false, synced_at = %(synced_at)s "
            "WHERE entity_key = %(entity_key)s",
            {"synced_at": synced_at, "entity_key": entity_key},
        )
    if stale:
        logger.warning(
            "registry_sync: deactivated %d %s entities absent from YAML: %s",
            len(stale), kind, stale,
        )
    return len(stale)


def _sync_promoted_officials(
    conn: Connection, accounts: List[reg.PoliticalAccountRecord],
    editorial_lookup: Dict[str, int], synced_at: datetime,
) -> Tuple[set, int]:
    """Promote every curated political account to its own `corpus.entities`
    row (`kind='official'`, `editorial=false`), grouped by `display_name` so
    one person's multiple handles collapse to one entity: the group's first
    handle becomes `entity_key`, the rest become aliases. A group is skipped
    entirely if its primary handle already resolves via `editorial_lookup`
    (an editorial `verified_officials.yaml` entity always wins); a
    non-primary handle that resolves via `editorial_lookup` is dropped from
    the aliases and logged rather than aliased onto the wrong entity. See
    the audit trail (module docstring) for the full dedup rationale.

    Returns (entity_keys promoted this pass, count of person-groups skipped
    as editorial-covered)."""
    groups: Dict[str, List[reg.PoliticalAccountRecord]] = {}
    for account in accounts:
        groups.setdefault(account.display_name, []).append(account)

    promoted_keys: set = set()
    editorial_dedup = 0
    for display_name, group in groups.items():
        primary, *rest = group
        if editorial_lookup.get(primary.handle) is not None:
            editorial_dedup += 1
            continue
        lean = reg.flatten_party_code(primary.raw_party, context=f"account {primary.handle}")
        entity_id = _upsert_entity(
            conn, entity_key=primary.handle, kind="official",
            display_name=display_name, blurb="",
            role_title=primary.office_title, term_start=None, owner=None,
            source_citation=None, lean=lean, lean_source=primary.raw_party,
            editorial=False, synced_at=synced_at,
        )
        aliases = []
        for account in rest:
            if editorial_lookup.get(account.handle) is not None:
                logger.warning(
                    "registry_sync: account %s (%s) shares handle %r with an existing "
                    "editorial entity; not aliased onto the promoted entity",
                    primary.handle, display_name, account.handle,
                )
                continue
            aliases.append(account.handle)
        _sync_aliases(conn, entity_id, aliases)
        promoted_keys.add(primary.handle)
    return promoted_keys, editorial_dedup


def _load_x_author_lookup(conn: Connection) -> Dict[str, int]:
    """Build {canonicalized handle: author_id} for every X author currently
    in `corpus.authors`. Canonicalization happens in Python (matching
    `registry.canonicalize_handle` exactly) rather than in SQL, so it is
    correct regardless of how `etl/authors.py` stored the raw handle."""
    rows = conn.execute(
        "SELECT author_id, handle FROM corpus.authors WHERE platform = 'x'::corpus.platform "
        "AND handle IS NOT NULL"
    ).fetchall()
    lookup: Dict[str, int] = {}
    for row in rows:
        handle = reg.canonicalize_handle(row["handle"])
        if handle:
            lookup[handle] = row["author_id"]
    return lookup


def _load_official_entity_lookup(conn: Connection, *, editorial_only: bool = False) -> Dict[str, int]:
    """Build {canonicalized handle-or-alias: entity_id} for every active
    `kind='official'` entity. `editorial_only=True` is used for the
    promotion collision check (must see only editorial entities, not a
    promoted person's own row from an earlier pass); the default
    (editorial + promoted) is used for account linking."""
    editorial_clause = " AND editorial = true" if editorial_only else ""
    rows = conn.execute(
        "SELECT entity_id, entity_key FROM corpus.entities "
        f"WHERE kind = 'official'::corpus.entity_kind AND active = true{editorial_clause}"
    ).fetchall()
    lookup: Dict[str, int] = {r["entity_key"]: r["entity_id"] for r in rows}
    entity_ids = [r["entity_id"] for r in rows]
    if entity_ids:
        alias_rows = conn.execute(
            "SELECT entity_id, alias FROM corpus.entity_aliases WHERE entity_id = ANY(%(ids)s)",
            {"ids": entity_ids},
        ).fetchall()
        for r in alias_rows:
            lookup[r["alias"]] = r["entity_id"]
    return lookup


def _sync_author_profiles(
    conn: Connection, accounts: List[reg.PoliticalAccountRecord], classified_at: datetime,
) -> Tuple[int, int]:
    """Upsert `corpus.author_profiles` for every curated account whose
    author already exists in `corpus.authors`. A handle with no matching
    author yet is skipped (not guessed at) and self-heals on a later sync.
    Returns (synced_count, skipped_count)."""
    author_lookup = _load_x_author_lookup(conn)
    entity_lookup = _load_official_entity_lookup(conn)
    synced = 0
    skipped = 0
    for account in accounts:
        author_id = author_lookup.get(account.handle)
        if author_id is None:
            skipped += 1
            continue
        _upsert_author_profile(
            conn, author_id=author_id, tier=ELECTED_OFFICIAL_TIER,
            method=CURATED_LIST_METHOD, entity_id=entity_lookup.get(account.handle),
            notes=None, classified_at=classified_at,
        )
        synced += 1
    return synced, skipped


def _upsert_author_profile(
    conn: Connection, *, author_id: int, tier: str, method: str,
    entity_id: Optional[int], notes: Optional[str], classified_at: datetime,
) -> None:
    """Upsert one `corpus.author_profiles` row keyed by `author_id`. Same
    no-op-when-unchanged idempotency contract as `_upsert_entity`."""
    where_clause = " OR ".join(
        f"corpus.author_profiles.{c} IS DISTINCT FROM EXCLUDED.{c}" for c in _PROFILE_CONTENT_COLUMNS
    )
    conn.execute(
        f"""
        INSERT INTO corpus.author_profiles (author_id, tier, method, entity_id, classified_at, notes)
        VALUES (%(author_id)s, %(tier)s::corpus.author_tier,
                %(method)s::corpus.classification_method, %(entity_id)s,
                %(classified_at)s, %(notes)s)
        ON CONFLICT (author_id) DO UPDATE SET
            tier = EXCLUDED.tier, method = EXCLUDED.method,
            entity_id = EXCLUDED.entity_id, notes = EXCLUDED.notes,
            classified_at = EXCLUDED.classified_at
        WHERE {where_clause}
        """,
        {
            "author_id": author_id, "tier": tier, "method": method,
            "entity_id": entity_id, "notes": notes, "classified_at": classified_at,
        },
    )


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    sync_registry()
