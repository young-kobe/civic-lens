"""
Shared Postgres test fixture (Postgres redesign, gated-suite fragility fix
2026-07-23). Every gated (CIVIC_TEST_DATABASE_URL) integration TestCase in
this directory re-implemented the same schema-reset / pool-lifecycle dance
with copy-pasted SQL and Python; consolidated here now that it has
three-plus consumers.

Root-cause fix for the "the full gated suite fails when run as one
`unittest discover` process, but each module passes alone" fragility:
every module's `setUpClass` dropped and recreated the schema via a raw
connection WITHOUT first closing the shared `common/db.py` `ConnectionPool`
singleton -- only the per-test `setUp`/`tearDown` did that. A pool left
open (or still finishing an async `open(wait=False)` reconnect -- see
`common/db.py::get_pool`) by whichever class ran immediately before holds
connections whose session state (prepared plans, catalog caches) is keyed
to the OLD schema's relation OIDs; `DROP SCHEMA ... CASCADE` changes every
one of those OIDs. Running the drop while that pool could still be handing
out or reconnecting a connection races the next class's fresh pool against
stale catalog state -- intermittent by nature (it depends on exactly when
the previous class's tests finished relative to this drop), which is why
each module passed reliably in isolation but the full suite did not.
`reset_schema()` below closes the pool FIRST, every time, closing that gap.
"""

from __future__ import annotations

import os
from pathlib import Path
from typing import Optional

import psycopg

from analysis.src.common import db as dbmod

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
MIGRATION_0001 = REPO_ROOT / "data" / "pg-migrations" / "0001_north_star.sql"
MIGRATION_0002 = REPO_ROOT / "data" / "pg-migrations" / "0002_entity_registry_seed.sql"
MIGRATION_0003 = REPO_ROOT / "data" / "pg-migrations" / "0003_admission_class.sql"
MIGRATION_0004 = REPO_ROOT / "data" / "pg-migrations" / "0004_drop_serving.sql"

_ALL_SCHEMAS = "raw, corpus, analysis, serving, ops, archive"


def reset_schema(dsn: str, *, seed: bool = False) -> None:
    """Drop and recreate every schema from the greenfield migration(s).
    Idempotent, so any gated module can share a running container with any
    other module in the same `unittest discover` process -- each applies
    fresh rather than assuming it's the first to run.

    Closes the shared `ConnectionPool` FIRST (see module docstring) before
    dropping. `seed=True` also applies `0002_entity_registry_seed.sql` --
    needed by modules matching against the real curated registry
    (account_tier, narratives, targets). `0003_admission_class.sql` (the
    corpus.documents.admission_class column) and `0004_drop_serving.sql`
    (drops the never-written `serving` schema -- Phase 9 went
    strictly-live, see docs/audit-trail/analysis/2026-07-24-phase9-prewave.md)
    are always applied, in numeric order after the optional 0002 seed --
    every gated module's baseline schema should track the latest
    migration, not just 0001.
    """
    dbmod.close_pool()
    with psycopg.connect(dsn, autocommit=True) as conn:
        conn.execute(f"DROP SCHEMA IF EXISTS {_ALL_SCHEMAS} CASCADE")
        conn.execute(MIGRATION_0001.read_text())
        if seed:
            conn.execute(MIGRATION_0002.read_text())
        conn.execute(MIGRATION_0003.read_text())
        conn.execute(MIGRATION_0004.read_text())


def begin_test(dsn: str) -> Optional[str]:
    """Per-test `setUp()`: close the pool, point `CIVIC_DATABASE_URL` at
    `dsn`. Returns the previous value of `CIVIC_DATABASE_URL` -- pass it to
    `end_test()` to restore."""
    dbmod.close_pool()
    prev_url = os.environ.get("CIVIC_DATABASE_URL")
    os.environ["CIVIC_DATABASE_URL"] = dsn
    return prev_url


def end_test(prev_url: Optional[str]) -> None:
    """Per-test `tearDown()`: close the pool, restore `CIVIC_DATABASE_URL`
    to whatever `begin_test()` captured."""
    dbmod.close_pool()
    if prev_url is None:
        os.environ.pop("CIVIC_DATABASE_URL", None)
    else:
        os.environ["CIVIC_DATABASE_URL"] = prev_url
