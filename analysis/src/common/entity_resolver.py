"""
DB-backed entity resolver (Postgres redesign Phase 6) replacing the old
YAML entity_registry for FK resolution against `corpus.entities`. Loads the
active registry once per construction into an in-memory map; `resolve()` is
then a pure dict lookup with no further DB access.
"""

from __future__ import annotations

from typing import Any, Dict, Optional

from analysis.src.common import db
from analysis.src.common.canonicalize import canonicalize_entity_name

# Generational suffixes ignored when deriving a last-name alias
# ("Robert F. Kennedy Jr." -> "kennedy"). Ported from
# reporting/entity_registry.py::TargetResolver (old-stack/YAML-tied, retires
# in Phase 9 -- copied here rather than imported).
_NAME_SUFFIXES = frozenset({"jr", "jr.", "sr", "sr.", "ii", "iii", "iv"})


class EntityResolver:
    """Resolves free-text entity names (as emitted by an LLM, e.g. the text
    engine's favorability `entity` field) to `corpus.entities.entity_id`.

    Constructed once per pipeline run against a connection (or nothing, in
    which case it checks one out of the shared pool via `common.db`) --
    587 rows today, trivially cacheable in memory. `resolve()` performs no
    I/O; it is a pure lookup against the map built at construction.
    """

    def __init__(self, conn: Optional[Any] = None) -> None:
        self._index: Dict[str, int] = {}
        if conn is not None:
            self._load(conn)
        else:
            with db.connection() as owned_conn:
                self._load(owned_conn)

    def _load(self, conn: Any) -> None:
        entity_rows = conn.execute(
            "SELECT entity_id, entity_key, display_name, active FROM corpus.entities"
        ).fetchall()

        active_ids: set = set()
        last_name_owners: Dict[str, set] = {}
        for row in entity_rows:
            if not row["active"]:
                continue
            active_ids.add(row["entity_id"])
            self._index_entity(row["entity_id"], row["entity_key"], row["display_name"], last_name_owners)

        # A derived last-name-only alias is only safe when exactly one active
        # entity owns that last name (mirrors TargetResolver's ambiguity guard).
        for last_name, owners in last_name_owners.items():
            if len(owners) == 1:
                self._index.setdefault(last_name, next(iter(owners)))

        alias_rows = conn.execute(
            "SELECT entity_id, alias FROM corpus.entity_aliases"
        ).fetchall()
        for row in alias_rows:
            if row["entity_id"] not in active_ids:
                continue
            key = canonicalize_entity_name(row["alias"])
            if key:
                self._index.setdefault(key, row["entity_id"])

    def _index_entity(
        self, entity_id: int, entity_key: str, display_name: str, last_name_owners: Dict[str, set],
    ) -> None:
        for raw_name in (entity_key, display_name):
            key = canonicalize_entity_name(raw_name)
            if key:
                self._index.setdefault(key, entity_id)

        canonical_display = canonicalize_entity_name(display_name)
        if not canonical_display:
            return
        tokens = [t for t in canonical_display.split() if t not in _NAME_SUFFIXES]
        if len(tokens) >= 2:
            self._index.setdefault(f"{tokens[0]} {tokens[-1]}", entity_id)
            last_name_owners.setdefault(tokens[-1], set()).add(entity_id)

    def resolve(self, raw_name: Optional[str]) -> Optional[int]:
        """Resolve a free-text entity name to its entity_id, or None when
        unresolved. Unresolved names are never stored as a
        favorability_stances row (entity_id is NOT NULL there) -- callers
        count and log the drop."""
        key = canonicalize_entity_name(raw_name)
        if key is None:
            return None
        return self._index.get(key)
