"""
Citation-edge semantics tests.

The business rule under test: the flat inbound-citation count must be
decomposable into meanings a reader can act on — HOW posts cite into a
narrative (quote vs reply vs bare link), what the narrative points at
OUTSIDE our sample (external URLs we never ingested), and which OTHER
narratives its documents exchange citations with. All of it stays scoped
to edges between sampled docs — the numbers must never grow into an
origin/propagation claim (walkthrough 035).
"""

import sqlite3
import sys
import unittest
import uuid
from pathlib import Path

_repo_root = Path(__file__).resolve().parents[2]
if str(_repo_root) not in sys.path:
    sys.path.insert(0, str(_repo_root))

from analysis.src.reporting.aggregators.narrative import NarrativeAggregator


class CitationDetailTests(unittest.TestCase):
    def setUp(self):
        self.db_path = f"test_citation_detail_{uuid.uuid4()}.db"
        conn = sqlite3.connect(self.db_path)
        cur = conn.cursor()
        cur.executescript("""
            CREATE TABLE narratives (
                narrative_id INTEGER PRIMARY KEY,
                name TEXT
            );
            CREATE TABLE narrative_docs (
                narrative_id INTEGER,
                doc_id INTEGER
            );
            CREATE TABLE narrative_citations (
                citation_id INTEGER PRIMARY KEY,
                source_doc_id INTEGER,
                target_doc_id INTEGER,
                target_url TEXT,
                link_type TEXT,
                discovered_at INTEGER
            );
        """)
        cur.executemany(
            "INSERT INTO narratives (narrative_id, name) VALUES (?,?)",
            [(1, "Claim A"), (2, "Claim B"), (3, "Claim C")],
        )
        cur.executemany(
            "INSERT INTO narrative_docs (narrative_id, doc_id) VALUES (?,?)",
            [(1, 1), (1, 2), (2, 3), (3, 4)],
        )
        ts = 1_735_000_000
        cur.executemany(
            "INSERT INTO narrative_citations "
            "(source_doc_id, target_doc_id, target_url, link_type, discovered_at) "
            "VALUES (?,?,?,?,?)",
            [
                # Inbound to narrative 1: a quote from n2's doc, a reply from n3's.
                (3, 1, None, "quote", ts),
                (4, 2, None, "reply", ts),
                # Narrative 1 cites into narrative 2.
                (1, 3, None, "url_citation", ts),
                # Narrative 1 links out to a URL we never ingested.
                (2, None, "https://example.invalid/story", "url_citation", ts),
                # An old edge, for the cutoff test.
                (3, 2, None, "quote", ts - 10_000),
            ],
        )
        conn.commit()
        conn.close()
        self.agg = NarrativeAggregator(self.db_path)

    def tearDown(self):
        Path(self.db_path).unlink(missing_ok=True)

    def _detail(self, narrative_id, cutoff=None):
        conn = sqlite3.connect(self.db_path)
        try:
            return self.agg._citation_detail(conn.cursor(), narrative_id, cutoff)
        finally:
            conn.close()

    def test_inbound_split_by_link_type(self):
        by_link_type, _, _ = self._detail(1)
        self.assertEqual(by_link_type, {"quote": 2, "reply": 1})

    def test_external_citations_counted_separately(self):
        """Un-ingested target_url edges must not inflate inbound counts —
        they are what the narrative points AT outside the sample."""
        by_link_type, external, _ = self._detail(1)
        self.assertEqual(external, 1)
        self.assertEqual(sum(by_link_type.values()), 3)

    def test_cross_narrative_edges_both_directions(self):
        _, _, cross = self._detail(1)
        edges = {(e["direction"], e["narrative_id"]): e["edge_count"] for e in cross}
        self.assertEqual(edges[("cites", 2)], 1)
        self.assertEqual(edges[("cited_by", 2)], 2)   # quote + old quote
        self.assertEqual(edges[("cited_by", 3)], 1)
        names = {e["narrative_id"]: e["name"] for e in cross}
        self.assertEqual(names[2], "Claim B")

    def test_cutoff_filters_old_edges(self):
        by_link_type, _, cross = self._detail(1, cutoff=1_735_000_000 - 100)
        self.assertEqual(by_link_type, {"quote": 1, "reply": 1})
        edges = {(e["direction"], e["narrative_id"]): e["edge_count"] for e in cross}
        self.assertEqual(edges[("cited_by", 2)], 1)


if __name__ == "__main__":
    unittest.main()
