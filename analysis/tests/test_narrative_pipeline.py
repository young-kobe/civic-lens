"""
Tests for the propagation layer: citation extraction + narrative clustering.

Neither path requires LLM access — citations are deterministic, and
narrative clustering runs over already-extracted claims read out of
ai_outputs rows seeded directly in the fixture.
"""

import json
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

from analysis.src.engine.citation_extractor import CitationExtractor, canonicalize_url
from analysis.src.engine.claim_extractor import _validate_claim
from analysis.src.engine.narrative_clusterer import (
    NarrativeClusterer, cosine, jaccard, tokenize_claim,
)

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


class TestCanonicalizeURL(unittest.TestCase):
    def test_lowercase_host_and_scheme(self):
        self.assertEqual(canonicalize_url("HTTPS://Example.COM/Path"), "https://example.com/Path")

    def test_strip_www_and_trailing_slash(self):
        self.assertEqual(canonicalize_url("http://www.example.com/path/"), "http://example.com/path")

    def test_strip_fragment(self):
        self.assertEqual(
            canonicalize_url("https://example.com/path#section"),
            "https://example.com/path",
        )

    def test_strip_trailing_punctuation(self):
        self.assertEqual(canonicalize_url("https://example.com/path,"), "https://example.com/path")


class TestJaccard(unittest.TestCase):
    def test_identical(self):
        self.assertEqual(jaccard({"a", "b"}, {"a", "b"}), 1.0)

    def test_disjoint(self):
        self.assertEqual(jaccard({"a"}, {"b"}), 0.0)

    def test_partial(self):
        self.assertAlmostEqual(jaccard({"a", "b", "c"}, {"b", "c", "d"}), 0.5)

    def test_stopwords_filtered(self):
        a = tokenize_claim("Trump won the election")
        b = tokenize_claim("Trump is the winner of the election")
        # Without stopword filter, similarity would be diluted by "the", "is", "of".
        self.assertGreaterEqual(jaccard(a, b), 0.4)


class TestClaimValidator(unittest.TestCase):
    def setUp(self):
        self.text = "Reports indicate the senator voted against the measure on Tuesday afternoon."

    def test_valid_claim_passes(self):
        raw = {
            "claim": "The senator voted against the measure",
            "confidence": 0.8,
            "evidence_span": "senator voted against the measure",
        }
        claim = _validate_claim(raw, self.text)
        self.assertIsNotNone(claim)
        self.assertEqual(claim.claim, "The senator voted against the measure")

    def test_missing_evidence_rejected(self):
        raw = {"claim": "Senator voted against the measure", "confidence": 0.8, "evidence_span": ""}
        self.assertIsNone(_validate_claim(raw, self.text))

    def test_fabricated_evidence_rejected(self):
        raw = {
            "claim": "Senator voted against the measure",
            "confidence": 0.9,
            "evidence_span": "totally made up phrase nowhere in source",
        }
        self.assertIsNone(_validate_claim(raw, self.text))

    def test_claim_too_short_rejected(self):
        raw = {"claim": "Bad", "confidence": 0.8, "evidence_span": "senator voted against"}
        self.assertIsNone(_validate_claim(raw, self.text))

    def test_claim_too_long_rejected(self):
        raw = {
            "claim": " ".join(["word"] * 30),
            "confidence": 0.8,
            "evidence_span": "senator voted against",
        }
        self.assertIsNone(_validate_claim(raw, self.text))


class TestCitationExtractor(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.db_path = tmp.name
        tmp.close()
        _apply_migrations(self.db_path)

        # Seed three docs: a news article, an X post referencing another X post,
        # and an X post whose text cites the news URL.
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        conn.executemany(
            """
            INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit, published_at,
                              title, text, raw_hash, metadata_json)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                (1, "news", "https://news.example.com/story", "news.example.com", now,
                 "Story", "Article body discussing the election.", "h1", "{}"),
                (2, "x_post", "tweet_100", "x.com", now,
                 None, "Discussing the story. https://news.example.com/story", "h2", "{}"),
                (3, "x_post", "tweet_200", "x.com", now,
                 None, "Reply thread", "h3", "{}"),
                (4, "x_post", "tweet_300", "x.com", now,
                 None, "Another tweet citing https://www.news.example.com/story/", "h4", "{}"),
            ],
        )
        conn.executemany(
            """
            INSERT INTO x_posts_raw (tweet_id, author_id, created_at, fetched_at, text,
                                     referenced_tweet_id, referenced_tweet_type, raw_hash, extraction_version)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                ("tweet_100", "u1", now, now, "t", None, None, "h2", "v1"),
                # tweet_200 is a reply to tweet_100
                ("tweet_200", "u2", now, now, "t", "tweet_100", "replied_to", "h3", "v1"),
                ("tweet_300", "u3", now, now, "t", None, None, "h4", "v1"),
            ],
        )
        conn.commit()
        conn.close()

    def tearDown(self):
        for suffix in ("", "-wal", "-shm"):
            try:
                os.remove(self.db_path + suffix)
            except (FileNotFoundError, PermissionError):
                pass

    def _docs_for_extraction(self):
        conn = sqlite3.connect(self.db_path)
        rows = conn.execute(
            "SELECT doc_id, text, source_type, ident FROM docs ORDER BY doc_id"
        ).fetchall()
        conn.close()
        return [
            {"doc_id": r[0], "text": r[1], "source_type": r[2], "ident": r[3], "metadata": {}}
            for r in rows
        ]

    def test_extract_writes_reply_and_url_citations(self):
        extractor = CitationExtractor(self.db_path)
        processed, edges = extractor.extract_batch(self._docs_for_extraction())
        self.assertEqual(processed, 4)
        # Expected edges:
        #   tweet_100 text → news URL canon → target_doc_id=1, link_type='url_citation'
        #   tweet_200 → tweet_100 reply → target_doc_id=2, link_type='reply'
        #   tweet_300 text → news URL canon (with www) → target_doc_id=1
        # Total: 3
        self.assertEqual(edges, 3)

        conn = sqlite3.connect(self.db_path)
        rows = conn.execute(
            "SELECT source_doc_id, target_doc_id, target_url, link_type FROM narrative_citations ORDER BY citation_id"
        ).fetchall()
        conn.close()
        self.assertEqual(len(rows), 3)
        kinds = [r[3] for r in rows]
        self.assertIn("url_citation", kinds)
        self.assertIn("reply", kinds)

    def test_rerun_is_idempotent_via_marker(self):
        # The job_runner path uses get_unprocessed_docs, which filters out docs
        # already marked in ai_outputs. Simulate that marker after a first run.
        extractor = CitationExtractor(self.db_path)
        extractor.extract_batch(self._docs_for_extraction())
        conn = sqlite3.connect(self.db_path)
        markers = conn.execute(
            "SELECT COUNT(*) FROM ai_outputs WHERE task_type='citations'"
        ).fetchone()[0]
        conn.close()
        self.assertEqual(markers, 4)


class TestNarrativeClusterer(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.db_path = tmp.name
        tmp.close()
        _apply_migrations(self.db_path)

        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        # Four docs, each with a single seeded claim via ai_outputs rows.
        # Claims 1 and 2 should cluster together (Jaccard ≥ 0.3 after stopword strip).
        # Claim 3 is unrelated. Claim 4 is related to 1/2.
        conn.executemany(
            """
            INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit, published_at,
                              text, raw_hash, metadata_json)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                (1, "news", "a", "a.com", now, "t", "h1", "{}"),
                (2, "news", "b", "b.com", now, "t", "h2", "{}"),
                (3, "news", "c", "c.com", now, "t", "h3", "{}"),
                (4, "x_post", "d", "x.com", now, "t", "h4", "{}"),
            ],
        )
        # Claims 1, 2, 4 are near-duplicates that share ≥3 content tokens with
        # each other — Jaccard-over-tokens should cluster them into one narrative.
        # Claim 3 is unrelated. Semantic synonymy (won vs victory) is NOT handled
        # by this MVP clusterer; tests exercise lexical near-duplication only.
        claim_rows = [
            (1, "claims", json.dumps({"claims": [
                {"claim": "Trump won Pennsylvania by narrow margin", "confidence": 0.9,
                 "evidence_span": "trump won pennsylvania"},
            ]}), 0.9, now),
            (2, "claims", json.dumps({"claims": [
                {"claim": "Trump won Pennsylvania election decisively", "confidence": 0.85,
                 "evidence_span": "trump won pennsylvania"},
            ]}), 0.85, now),
            (3, "claims", json.dumps({"claims": [
                {"claim": "Senate passed the infrastructure bill yesterday", "confidence": 0.8,
                 "evidence_span": "senate passed bill"},
            ]}), 0.8, now),
            (4, "claims", json.dumps({"claims": [
                {"claim": "Trump won Pennsylvania election results confirmed", "confidence": 0.88,
                 "evidence_span": "trump pennsylvania"},
            ]}), 0.88, now),
        ]
        conn.executemany(
            """
            INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, created_at)
            VALUES (?, ?, ?, ?, ?)
            """,
            claim_rows,
        )
        conn.commit()
        conn.close()

    def tearDown(self):
        for suffix in ("", "-wal", "-shm"):
            try:
                os.remove(self.db_path + suffix)
            except (FileNotFoundError, PermissionError):
                pass

    def test_clusters_similar_claims_into_one_narrative(self):
        clusterer = NarrativeClusterer(self.db_path)
        summary = clusterer.run()

        # Three Pennsylvania claims (1, 2, 4) plus one Senate claim (3) =
        # two narratives total. 4 assignments (one per doc).
        self.assertEqual(summary["claims_considered"], 4)
        self.assertEqual(summary["narratives_created"], 2)
        self.assertEqual(summary["assignments"], 4)

        conn = sqlite3.connect(self.db_path)
        rows = conn.execute(
            "SELECT narrative_id, COUNT(*) FROM narrative_docs GROUP BY narrative_id"
        ).fetchall()
        conn.close()
        counts = sorted(c for _, c in rows)
        self.assertEqual(counts, [1, 3])

    def test_rerun_does_not_duplicate_assignments(self):
        clusterer = NarrativeClusterer(self.db_path)
        clusterer.run()
        second = clusterer.run()
        # All docs are already assigned; second run should find nothing pending.
        self.assertEqual(second["claims_considered"], 0)
        self.assertEqual(second["assignments"], 0)


class _FakeEmbeddingClient:
    """Tiny embedding client that maps fixed phrases to fixed vectors.

    Used to test embedding mode without spinning up Ollama. Returns None
    for unknown text so the fallback path is also exercised.
    """

    def __init__(self, vectors):
        self._vectors = vectors

    def embed(self, text, model=None):
        return self._vectors.get(text)


class TestNarrativeClustererEmbeddingMode(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.db_path = tmp.name
        tmp.close()
        _apply_migrations(self.db_path)

        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        conn.executemany(
            """
            INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit,
                              published_at, text, raw_hash, metadata_json)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                (1, "news", "a", "a.com", now, "t", "h1", "{}"),
                (2, "news", "b", "b.com", now, "t", "h2", "{}"),
                (3, "news", "c", "c.com", now, "t", "h3", "{}"),
            ],
        )
        # Synonymous claims that Jaccard would split (low token overlap) but
        # an embedding model would correctly cluster.
        conn.executemany(
            """
            INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, created_at)
            VALUES (?, ?, ?, ?, ?)
            """,
            [
                (1, "claims", json.dumps({"claims": [
                    {"claim": "Trump won Pennsylvania",
                     "confidence": 0.9, "evidence_span": "trump"}
                ]}), 0.9, now),
                (2, "claims", json.dumps({"claims": [
                    {"claim": "Trump victory in Keystone State",
                     "confidence": 0.85, "evidence_span": "trump"}
                ]}), 0.85, now),
                (3, "claims", json.dumps({"claims": [
                    {"claim": "Senate passed infrastructure bill",
                     "confidence": 0.8, "evidence_span": "senate"}
                ]}), 0.8, now),
            ],
        )
        conn.commit()
        conn.close()

    def tearDown(self):
        for suffix in ("", "-wal", "-shm"):
            try:
                os.remove(self.db_path + suffix)
            except (FileNotFoundError, PermissionError):
                pass

    def test_embedding_mode_clusters_synonyms(self):
        # Embeddings: the two PA claims point in nearly the same direction;
        # the Senate claim is orthogonal.
        vectors = {
            "Trump won Pennsylvania": [1.0, 0.0, 0.05],
            "Trump victory in Keystone State": [0.95, 0.0, 0.10],
            "Senate passed infrastructure bill": [0.0, 1.0, 0.0],
        }
        client = _FakeEmbeddingClient(vectors)
        clusterer = NarrativeClusterer(
            self.db_path,
            mode="embedding",
            embedding_client=client,
            embedding_threshold=0.65,
        )
        summary = clusterer.run()

        # Expected: PA claims merge → 1 narrative, Senate stands alone → 1 more.
        self.assertEqual(summary["mode"], "embedding")
        self.assertEqual(summary["narratives_created"], 2)

        conn = sqlite3.connect(self.db_path)
        # Anchor embeddings should have been persisted on creation.
        rows = conn.execute(
            "SELECT name, anchor_embedding_json FROM narratives ORDER BY narrative_id"
        ).fetchall()
        conn.close()
        self.assertEqual(len(rows), 2)
        for _, embed_json in rows:
            self.assertIsNotNone(embed_json)
            self.assertIsInstance(json.loads(embed_json), list)

    def test_embedding_mode_falls_back_when_client_returns_none(self):
        # Client returns None for everything → behaves like jaccard mode.
        client = _FakeEmbeddingClient({})
        clusterer = NarrativeClusterer(
            self.db_path, mode="embedding", embedding_client=client,
        )
        summary = clusterer.run()
        # Without working embeddings, the synonymous claims won't cluster
        # (Jaccard token overlap is too low between "Pennsylvania" and
        # "Keystone State"), so we end up with 3 narratives.
        self.assertEqual(summary["narratives_created"], 3)


class TestCosine(unittest.TestCase):
    def test_orthogonal(self):
        self.assertEqual(cosine([1, 0], [0, 1]), 0.0)

    def test_identical(self):
        self.assertAlmostEqual(cosine([1, 2, 3], [1, 2, 3]), 1.0)

    def test_empty_returns_zero(self):
        self.assertEqual(cosine([], [1, 2]), 0.0)

    def test_mismatched_length(self):
        self.assertEqual(cosine([1, 2], [1, 2, 3]), 0.0)


if __name__ == "__main__":
    unittest.main()
