"""
N+1 regression guard for the narrative aggregator.

WHY this test exists: ``get_top_narratives`` used to fetch each narrative's
facts one narrative at a time — one ranking query plus ~13 per-narrative
queries (source breakdown, timeline, sentiment, mean confidence, propaganda,
bot-pushed, inbound citations, citation detail x3, cross-tier, supporting docs,
first-seen). Total SQL grew linearly with the number of narratives returned, so
a busy window quietly turned into hundreds of round-trips. The aggregator was
rewritten to batch every fetch with ``WHERE narrative_id IN (...)`` (and window
functions where a per-narrative top-N had to be preserved), making the query
count a small constant.

This test pins that invariant: it counts every ``execute`` the aggregator
fires, runs it against N narratives and then 2N narratives, and asserts the
count does NOT grow with the number of narratives. If someone reintroduces a
per-narrative query, the 2N run will balloon and this test fails.
"""

import contextlib
import os
import sqlite3
import sys
import tempfile
import time
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

import analysis.src.reporting.aggregators.narrative.aggregator as agg_mod
from analysis.src.reporting.aggregators.narrative import NarrativeAggregator

MIGRATIONS_DIR = os.path.join(project_root, "data", "migrations")


def _apply_migrations(db_path: str) -> None:
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    for name in sorted(os.listdir(MIGRATIONS_DIR)):
        if name.endswith(".sql"):
            with open(os.path.join(MIGRATIONS_DIR, name)) as f:
                cursor.executescript(f.read())
    conn.commit()
    conn.close()


def _seed_narratives(db_path: str, count: int) -> None:
    """Seed ``count`` narratives, each with two news supporting docs and a
    sentiment row, so every batched fetch path actually returns rows."""
    now = int(time.time())
    conn = sqlite3.connect(db_path)
    doc_id = 0
    for n in range(1, count + 1):
        conn.execute(
            "INSERT INTO narratives (narrative_id, name, description, "
            "first_seen_at, first_seen_doc_id, created_at, updated_at) "
            "VALUES (?, ?, ?, ?, ?, ?, ?)",
            (n, f"Claim {n}", f"Claim {n}", now, None, now, now),
        )
        first_doc = None
        for _ in range(2):
            doc_id += 1
            if first_doc is None:
                first_doc = doc_id
            conn.execute(
                "INSERT INTO docs (doc_id, source_type, ident, "
                "domain_or_subreddit, published_at, text, raw_hash) "
                "VALUES (?, 'news', ?, 'nytimes.com', ?, 'body', ?)",
                (doc_id, f"https://nytimes.com/{doc_id}", now, f"rh-{doc_id}"),
            )
            conn.execute(
                "INSERT INTO ai_outputs (doc_id, task_type, output_json, "
                "confidence, label, created_at) "
                "VALUES (?, 'sentiment', '{\"label\": \"POSITIVE\"}', 0.9, "
                "'POSITIVE', ?)",
                (doc_id, now),
            )
            conn.execute(
                "INSERT INTO narrative_docs (narrative_id, doc_id, "
                "discovered_at, confidence) VALUES (?, ?, ?, 0.9)",
                (n, doc_id, now),
            )
        conn.execute(
            "UPDATE narratives SET first_seen_doc_id = ? WHERE narrative_id = ?",
            (first_doc, n),
        )
    conn.commit()
    conn.close()


class _CountingCursor(sqlite3.Cursor):
    """Cursor that tallies every execute() onto its owning connection."""

    def execute(self, *args, **kwargs):
        self.connection.execute_count += 1
        return super().execute(*args, **kwargs)


class _CountingConnection(sqlite3.Connection):
    """Connection whose cursor() hands back a counting cursor."""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.execute_count = 0

    def cursor(self, *args, **kwargs):
        return super().cursor(_CountingCursor)


def _run_and_count(db_path: str, limit: int) -> int:
    """Drive get_top_narratives against a counting connection and return the
    number of execute() calls the aggregator issued for that one call.

    The aggregator manages its own connection via ``get_connection``; we patch
    that helper to yield a counting connection so we observe exactly the SQL
    the aggregator issues (the ranking query plus every batched fetch)."""
    holder = {}

    @contextlib.contextmanager
    def _counting_connection(path):
        conn = sqlite3.connect(path, factory=_CountingConnection)
        conn.execute("PRAGMA foreign_keys = ON")
        # The PRAGMA above runs on the connection, not a counted cursor; reset
        # so only the aggregator's own cursor executes are tallied.
        conn.execute_count = 0
        holder["conn"] = conn
        try:
            yield conn
        finally:
            conn.close()

    original = agg_mod.get_connection
    agg_mod.get_connection = _counting_connection
    try:
        results = NarrativeAggregator(db_path).get_top_narratives(
            time_window="all", limit=limit,
        )
    finally:
        agg_mod.get_connection = original

    assert len(results) == limit, f"expected {limit} narratives, got {len(results)}"
    return holder["conn"].execute_count


class TestNarrativeBatching(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        # Seed 2N narratives so both the N and 2N runs read the same DB.
        _seed_narratives(self.db_path, 6)

    def tearDown(self):
        os.unlink(self.db_path)

    def test_query_count_does_not_scale_with_narrative_count(self):
        """The batched aggregator must issue the same (constant) number of
        queries whether it returns 3 narratives or 6. The pre-batch code fired
        ~13 queries per narrative, so doubling the narratives would have added
        ~39 queries; batching removes that growth entirely."""
        count_n = _run_and_count(self.db_path, 3)
        count_2n = _run_and_count(self.db_path, 6)

        # Batching invariant: doubling the narrative count must not grow the
        # query count at all — every fetch is a single IN (...) query.
        self.assertEqual(
            count_n, count_2n,
            f"query count scaled with narrative count: {count_n} -> {count_2n}",
        )
        # And the constant must be small — nowhere near the old ~1 + 13*N
        # (3 narratives would have been ~40 queries under the old scheme).
        self.assertLess(count_2n, 20)


if __name__ == "__main__":
    unittest.main()
