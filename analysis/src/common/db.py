"""
Postgres connection module (Phase 1 of the Postgres redesign, walkthrough
plan `has-our-aggregate-method-async-frog`).

This is NEW plumbing that runs alongside the existing SQLite stack; nothing
in the pipeline imports it yet except its own tests. It provides exactly one
thing per the plan's code design principles: a single lazily-initialized
psycopg3 `ConnectionPool` singleton, `dict_row`-shaped, that every future
Postgres call site checks a connection out of via `connection()`.

All SQL written against this pool must be schema-qualified (e.g.
`SELECT * FROM corpus.documents`) — this module deliberately does not set
`search_path`, so an unqualified table name is a bug, not a convenience.
"""

import threading
from contextlib import contextmanager
from typing import Iterator, Optional

from psycopg import Connection
from psycopg.rows import dict_row
from psycopg_pool import ConnectionPool

from analysis.src.common.settings import get_settings

_pool: Optional[ConnectionPool] = None
_pool_lock = threading.Lock()


def get_pool() -> ConnectionPool:
    """
    Return the process-wide Postgres connection pool, creating it on first
    call. Sized min=1 / max=`CIVIC_PG_POOL_MAX` (default 5, matching the
    small box's `max_connections=30` server-side per the redesign plan).

    Fails loud: raises RuntimeError immediately if `CIVIC_DATABASE_URL` is
    unset rather than falling back to any default DSN. The pool is opened
    non-blocking (`wait=False`) — connectivity problems surface on first
    use, not here, so a transiently-down database doesn't block process
    startup.
    """
    global _pool
    with _pool_lock:
        if _pool is None:
            settings = get_settings()
            if not settings.database_url:
                raise RuntimeError(
                    "CIVIC_DATABASE_URL is not set. Postgres connections require "
                    "an explicit connection string (e.g. "
                    "postgresql://user:pass@host:5432/dbname) — refusing to "
                    "guess a default."
                )
            pool = ConnectionPool(
                conninfo=settings.database_url,
                min_size=1,
                max_size=settings.pg_pool_max,
                kwargs={"row_factory": dict_row},
                open=False,
            )
            pool.open(wait=False)
            _pool = pool
        return _pool


def close_pool() -> None:
    """Close and discard the pool singleton. For tests and shutdown paths —
    a subsequent get_pool() call creates a fresh pool from current settings."""
    global _pool
    with _pool_lock:
        if _pool is not None:
            _pool.close()
            _pool = None


@contextmanager
def connection() -> Iterator[Connection]:
    """
    Check out a pooled Postgres connection as a context manager.

    Delegates to the pool's own `connection()` context manager, which
    returns the connection on exit (committing) or rolls back and returns
    it on an unhandled exception. Rows come back as dicts (`dict_row`).
    """
    pool = get_pool()
    with pool.connection() as conn:
        yield conn
