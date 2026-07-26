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
MIGRATIONS_DIR = REPO_ROOT / "data" / "pg-migrations"
MIGRATION_SEED = MIGRATIONS_DIR / "0002_entity_registry_seed.sql"

_ALL_SCHEMAS = "raw, corpus, analysis, serving, ops, archive"


def reset_schema(dsn: str, *, seed: bool = False) -> None:
    """Drop and recreate every schema from the greenfield migration(s).
    Idempotent, so any gated module can share a running container with any
    other module in the same `unittest discover` process -- each applies
    fresh rather than assuming it's the first to run.

    Closes the shared `ConnectionPool` FIRST (see module docstring) before
    dropping, then applies every file in data/pg-migrations in numeric
    order, so a gated module's baseline always tracks the latest migration.
    `seed=True` includes `0002_entity_registry_seed.sql`, needed only by
    modules matching against the real curated registry (account_tier,
    narratives, targets).
    """
    dbmod.close_pool()
    with psycopg.connect(dsn, autocommit=True) as conn:
        conn.execute(f"DROP SCHEMA IF EXISTS {_ALL_SCHEMAS} CASCADE")
        for path in sorted(MIGRATIONS_DIR.glob("[0-9]*.sql")):
            if path == MIGRATION_SEED and not seed:
                continue
            conn.execute(path.read_text())


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
