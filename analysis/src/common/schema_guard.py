"""
Fail-fast schema guard.

The Go ingestor owns applying migrations (`civic-ingest migrate`); the
Python pipeline only CONSUMES the schema. When an analysis image newer
than the database runs (manual `docker compose run analyze`, an image pull
outside deploy.sh), stages die mid-pipeline on missing tables/views with
unhelpful errors — 2026-07-10 prod: `no such table: ai_outputs_latest`
after migration 022 shipped in code but was never applied.

This module compares the database's `schema_version` against the highest
migration number shipped WITH this build (`data/migrations/NNN_*.sql`,
copied into the analysis image), so there is no version constant to keep
bumped. A DB *newer* than the code is tolerated — migrations are additive
and forward-compatible reads are the deploy-ordering norm.
"""

import os
import re
import sqlite3
from typing import Optional

_MIGRATION_RE = re.compile(r"^(\d+)_.*\.sql$")


def expected_schema_version(migrations_dir: str) -> Optional[int]:
    """Highest migration number shipped with this build, or None when the
    migrations directory is unreadable (unusual dev layouts — don't block)."""
    try:
        names = os.listdir(migrations_dir)
    except OSError:
        return None
    versions = [
        int(m.group(1))
        for n in names
        if (m := _MIGRATION_RE.match(n)) is not None
    ]
    return max(versions, default=None)


def check_schema_current(db_path: str, migrations_dir: str) -> Optional[str]:
    """Return an actionable error message when the DB is behind this
    build's migrations; None when current (or when we cannot tell)."""
    expected = expected_schema_version(migrations_dir)
    if expected is None:
        return None
    conn = sqlite3.connect(db_path)
    try:
        try:
            row = conn.execute(
                "SELECT COALESCE(MAX(version), 0) FROM schema_version"
            ).fetchone()
            actual = int(row[0] or 0)
        except sqlite3.OperationalError:
            # No schema_version table at all — a never-migrated database.
            actual = 0
    finally:
        conn.close()

    if actual < expected:
        return (
            f"database schema is at version {actual} but this build expects "
            f"{expected}. Apply migrations first — dev: `.\\run.ps1 migrate`; "
            "prod: `docker compose run --rm ingest migrate --db "
            "/var/lib/civic-lens/data/civic_lens.db`."
        )
    return None
