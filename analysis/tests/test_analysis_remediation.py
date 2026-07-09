"""
Regression tests for the analysis-layer remediation (audit A-1..A-13).

Each test encodes WHY the fix matters, not just the mechanics:
- A-1: out-of-range LLM confidences must never reach ai_outputs verbatim.
- A-3: a transient LLM failure must NOT be persisted as a real "nothing
  found" verdict — the doc must re-queue.
- A-13: the prompt-audit task_type column must not flip-flop per doc.
- A-2/A-6/A-5/A-4: headline numbers must match their documented contract.
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

from analysis.src.engine.analyzer import Analyzer
from analysis.src.engine.bot import HybridBotDetector
from analysis.src.engine.claim_extractor import ClaimExtractor
from analysis.src.engine.propaganda_detector import PropagandaDetector
from analysis.src.llm.base import normalize_confidence


# --------------------------------------------------------------------------- #
#  Fake LLM clients                                                           #
# --------------------------------------------------------------------------- #

class _FakeLLM:
    is_available = True

    def __init__(self, response):
        self._response = response

    def complete(self, system_prompt, user_prompt, response_schema=None, temperature=None):
        return self._response


class _RaisingLLM:
    is_available = True

    def complete(self, *a, **k):
        raise RuntimeError("simulated Ollama outage")


class _UnavailableLLM:
    is_available = False

    def complete(self, *a, **k):  # pragma: no cover - never called
        raise AssertionError("should not be called when unavailable")


# --------------------------------------------------------------------------- #
#  A-1 — confidence clamping                                                  #
# --------------------------------------------------------------------------- #

class TestNormalizeConfidence(unittest.TestCase):
    def test_percentage_scale_is_divided(self):
        """85 is a 0-100 percentage the model leaked despite the 0-1 rule."""
        self.assertAlmostEqual(normalize_confidence(85), 0.85)

    def test_slight_overshoot_clamps_not_divides(self):
        """1.5 is an overshoot of the 0-1 scale, not a 1.5% confidence."""
        self.assertEqual(normalize_confidence(1.5), 1.0)

    def test_in_range_untouched(self):
        self.assertAlmostEqual(normalize_confidence(0.42), 0.42)

    def test_non_numeric_falls_back(self):
        self.assertEqual(normalize_confidence("nonsense"), 0.5)


class TestAnalyzerConfidenceClamp(unittest.TestCase):
    def _analyzer(self, response):
        a = Analyzer(llm_enabled=False)
        a._llm_client = _FakeLLM(response)
        a.llm_enabled = True
        return a

    def test_sentiment_percentage_confidence_clamped(self):
        a = self._analyzer({
            "sentiment_label": "POSITIVE",
            "sentiment_confidence": 85,
            "sentiment_evidence_spans": [],
            "entity_stances": [],
            "overall_gop_stance": "neutral",
            "overall_favorability_confidence": 85,
        })
        sent, fav = a.analyze_full("The Senate passed the border bill on Tuesday.")
        self.assertAlmostEqual(sent.confidence, 0.85)
        self.assertAlmostEqual(fav.overall_confidence, 0.85)

    def test_sentiment_overshoot_confidence_clamped(self):
        a = self._analyzer({
            "sentiment_label": "NEGATIVE",
            "sentiment_confidence": 1.5,
            "sentiment_evidence_spans": [],
            "entity_stances": [],
            "overall_gop_stance": "neutral",
            "overall_favorability_confidence": 1.5,
        })
        sent, fav = a.analyze_full("The Senate rejected the bill on Tuesday.")
        self.assertEqual(sent.confidence, 1.0)
        self.assertEqual(fav.overall_confidence, 1.0)


class TestBotConfidenceClamp(unittest.TestCase):
    def test_bot_percentage_confidence_clamped(self):
        d = HybridBotDetector(llm_enabled=False)
        d._llm_client = _FakeLLM({
            "is_bot": True, "label": "bot", "confidence": 85,
            "llm_text_likelihood": 90, "indicators": [], "reasoning": "x",
        })
        d.llm_enabled = True
        result = d.analyze_full("some social post text")
        self.assertAlmostEqual(result.confidence, 0.85)
        self.assertAlmostEqual(result.llm_text_likelihood, 0.90)


# --------------------------------------------------------------------------- #
#  A-3 — transport failure must not persist as a clean verdict               #
# --------------------------------------------------------------------------- #

class TestClaimExtractionFailureSentinel(unittest.TestCase):
    def test_transport_failure_sets_failed_flag(self):
        ex = ClaimExtractor(llm_client=_RaisingLLM())
        result = ex.extract("The Senate passed the bill on Tuesday.")
        self.assertTrue(result.extraction_failed)
        self.assertEqual(result.claims, [])

    def test_unavailable_client_sets_failed_flag(self):
        ex = ClaimExtractor(llm_client=_UnavailableLLM())
        result = ex.extract("The Senate passed the bill on Tuesday.")
        self.assertTrue(result.extraction_failed)

    def test_empty_text_is_not_a_failure(self):
        ex = ClaimExtractor(llm_client=_RaisingLLM())
        result = ex.extract("")
        self.assertFalse(result.extraction_failed)


class TestPropagandaFailureSentinel(unittest.TestCase):
    def _detector(self, client):
        d = PropagandaDetector(llm_enabled=False)
        d._llm_client = client
        d.llm_enabled = True
        return d

    def test_transport_failure_sets_failed_flag(self):
        # Needs loaded language so the pre-filter doesn't short-circuit.
        d = self._detector(_RaisingLLM())
        result = d.detect("The radical extremists want to destroy everything we value.")
        self.assertTrue(result.detection_failed)

    def test_bare_result_has_no_inference_method(self):
        from analysis.src.engine.models import PropagandaResult
        self.assertIsNone(PropagandaResult().inference_method)


class TestJobRunnerSkipsFailedDocs(unittest.TestCase):
    """The doc must remain unprocessed (no ai_outputs row) so it re-queues."""

    def _apply_migrations(self, db_path):
        migrations_dir = os.path.join(project_root, "data", "migrations")
        conn = sqlite3.connect(db_path)
        for m in sorted(os.listdir(migrations_dir)):
            if m.endswith(".sql"):
                with open(os.path.join(migrations_dir, m)) as f:
                    conn.executescript(f.read())
        conn.commit()
        conn.close()

    def test_failed_claim_extraction_leaves_doc_unprocessed(self):
        tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        tmp.close()
        try:
            self._apply_migrations(tmp.name)
            conn = sqlite3.connect(tmp.name)
            conn.execute(
                "INSERT INTO docs (doc_id, source_type, ident, published_at, text, raw_hash) "
                "VALUES (1, 'x_post', 'tw1', ?, 'The Senate passed the bill Tuesday.', 'h1')",
                (int(time.time()),),
            )
            conn.commit()
            conn.close()

            from analysis.src.etl.loader import ContentLoader
            loader = ContentLoader(tmp.name)

            # Simulate the job_runner skip contract directly: a failed result
            # is never saved, so the doc stays in get_unprocessed_docs.
            ex = ClaimExtractor(llm_client=_RaisingLLM())
            result = ex.extract("The Senate passed the bill Tuesday.")
            if not result.extraction_failed:
                loader.save_ai_output(1, "claims", result.to_dict(), 0.0)

            unprocessed = loader.get_unprocessed_docs("claims")
            self.assertEqual([d["doc_id"] for d in unprocessed], [1])
        finally:
            for suffix in ("", "-wal", "-shm"):
                try:
                    os.remove(tmp.name + suffix)
                except OSError:
                    pass


# --------------------------------------------------------------------------- #
#  A-13 — prompt_versions.task_type must not flip-flop                        #
# --------------------------------------------------------------------------- #

class TestPromptVersionTaskTypeStable(unittest.TestCase):
    def _apply_migrations(self, db_path):
        migrations_dir = os.path.join(project_root, "data", "migrations")
        conn = sqlite3.connect(db_path)
        for m in sorted(os.listdir(migrations_dir)):
            if m.endswith(".sql"):
                with open(os.path.join(migrations_dir, m)) as f:
                    conn.executescript(f.read())
        conn.commit()
        conn.close()

    def test_shared_prompt_version_keeps_first_task_type(self):
        tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        tmp.close()
        try:
            self._apply_migrations(tmp.name)
            conn = sqlite3.connect(tmp.name)
            conn.execute(
                "INSERT INTO docs (doc_id, source_type, ident, text, raw_hash) "
                "VALUES (1, 'x_post', 'tw1', 'text', 'h1')"
            )
            conn.commit()
            conn.close()

            from analysis.src.etl.loader import ContentLoader
            loader = ContentLoader(tmp.name)
            # Sentiment then favorability share one prompt_version.
            loader.save_ai_output(1, "sentiment", {}, 0.5, prompt_version="ta-v1",
                                  system_prompt="SYS")
            loader.save_ai_output(1, "favorability", {}, 0.5, prompt_version="ta-v1",
                                  system_prompt="SYS")

            conn = sqlite3.connect(tmp.name)
            row = conn.execute(
                "SELECT task_type FROM prompt_versions WHERE prompt_version = 'ta-v1'"
            ).fetchone()
            conn.close()
            # Whatever the first writer recorded, it must not have flipped to
            # 'favorability' on the second write.
            self.assertEqual(row[0], "sentiment")
        finally:
            for suffix in ("", "-wal", "-shm"):
                try:
                    os.remove(tmp.name + suffix)
                except OSError:
                    pass


# --------------------------------------------------------------------------- #
#  Shared DB fixture helpers                                                  #
# --------------------------------------------------------------------------- #

def _apply_migrations(db_path):
    migrations_dir = os.path.join(project_root, "data", "migrations")
    conn = sqlite3.connect(db_path)
    for m in sorted(os.listdir(migrations_dir)):
        if m.endswith(".sql"):
            with open(os.path.join(migrations_dir, m)) as f:
                conn.executescript(f.read())
    conn.commit()
    conn.close()


def _seed_doc(conn, doc_id, source_type, ident, published_at, domain=None, text="body"):
    conn.execute(
        "INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit, "
        "published_at, text, raw_hash) VALUES (?, ?, ?, ?, ?, ?, ?)",
        (doc_id, source_type, ident, domain or source_type, published_at, text, f"rh-{doc_id}"),
    )


def _seed_output(conn, doc_id, task, payload, confidence, inference_method="llm", created_at=None):
    conn.execute(
        "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, "
        "inference_method, created_at) VALUES (?, ?, ?, ?, ?, ?)",
        (doc_id, task, json.dumps(payload), confidence, inference_method,
         created_at or int(time.time())),
    )


class _DbTest(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)

    def tearDown(self):
        for suffix in ("", "-wal", "-shm"):
            try:
                os.remove(self.db_path + suffix)
            except OSError:
                pass


# --------------------------------------------------------------------------- #
#  A-2 — deterministic-clean rows belong in the propaganda denominator        #
# --------------------------------------------------------------------------- #

class TestPropagandaDenominator(_DbTest):
    def test_deterministic_clean_docs_count_in_denominator(self):
        from analysis.src.reporting.aggregators.propaganda import PropagandaAggregator
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        # 1 flagged, 3 llm-clean, 6 deterministic pre-filter-clean = 10 total.
        _seed_doc(conn, 1, "news", "https://flag", now, domain="a.com")
        _seed_output(conn, 1, "propaganda", {
            "techniques": [{"technique": "loaded_language", "confidence": 0.8,
                            "evidence_span": "radical extremists want to destroy"}],
            "overall_propaganda_score": 0.7}, 0.7)
        for i in range(2, 5):  # 3 llm-clean
            _seed_doc(conn, i, "news", f"https://c{i}", now, domain="c.com")
            _seed_output(conn, i, "propaganda",
                         {"techniques": [], "overall_propaganda_score": 0.1}, 0.1)
        for i in range(5, 11):  # 6 deterministic pre-filter-clean
            _seed_doc(conn, i, "news", f"https://d{i}", now, domain="d.com")
            _seed_output(conn, i, "propaganda",
                         {"techniques": [], "overall_propaganda_score": 0.0},
                         0.0, inference_method="deterministic")
        conn.commit()
        conn.close()

        result = PropagandaAggregator(self.db_path).get_propaganda_overview("all")
        self.assertEqual(result.total_eligible_docs, 10)
        self.assertEqual(result.flagged_docs, 1)
        self.assertAlmostEqual(result.propaganda_rate_pct, 10.0, places=1)


# --------------------------------------------------------------------------- #
#  A-6 — narrative net-sentiment averages over all supporting docs            #
# --------------------------------------------------------------------------- #

class TestNarrativeNetSentiment(_DbTest):
    def test_neutral_docs_count_in_denominator(self):
        from analysis.src.reporting.aggregators.narrative import NarrativeAggregator
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        conn.execute(
            "INSERT INTO narratives (narrative_id, name, description, first_seen_at, "
            "first_seen_doc_id, created_at, updated_at) VALUES (1, 'N', 'N', ?, 1, ?, ?)",
            (now, now, now),
        )
        # 49 NEUTRAL + 1 POSITIVE, all above the 0.5 confidence floor.
        for i in range(1, 51):
            _seed_doc(conn, i, "news", f"https://n{i}", now, domain="n.com")
            label = "POSITIVE" if i == 50 else "NEUTRAL"
            _seed_output(conn, i, "sentiment", {"label": label, "confidence": 0.9}, 0.9)
            conn.execute(
                "INSERT INTO narrative_docs (narrative_id, doc_id, discovered_at, confidence) "
                "VALUES (1, ?, ?, 0.9)", (i, now),
            )
        conn.commit()
        conn.close()

        n = NarrativeAggregator(self.db_path).get_top_narratives("all", limit=5)[0]
        # (+0.9) / 50 * 100 = +1.8, NOT +90 (averaging over polarized docs only).
        self.assertAlmostEqual(n.net_sentiment, 1.8, places=1)


# --------------------------------------------------------------------------- #
#  A-4 / A-5 — movers favorability + bot/confidence filters                   #
# --------------------------------------------------------------------------- #

class TestMoversFavorabilityAndFilters(_DbTest):
    def _seed_windows(self, conn, now):
        cur = now - 1 * 86400          # inside current 7d window
        prev = now - 10 * 86400        # inside previous window [now-14d, now-7d]
        doc_id = 1
        for base_ts, stance in ((cur, "favorable"), (prev, "unfavorable")):
            for _ in range(10):
                _seed_doc(conn, doc_id, "x_post", f"tw{doc_id}", base_ts, domain="x.com")
                _seed_output(conn, doc_id, "favorability",
                             {"overall_gop_stance": stance}, 0.9)
                doc_id += 1
        return doc_id

    def test_favorability_mover_is_populated(self):
        from analysis.src.reporting.aggregators.movers import MoversAggregator
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        self._seed_windows(conn, now)
        conn.commit()
        conn.close()

        result = MoversAggregator(self.db_path).get_movers("7d")
        # A-4: reading overall_gop_stance means the mover is no longer null.
        self.assertIsNotNone(result.favorability_mover)
        self.assertEqual(result.favorability_mover.current_volume, 10)
        # current all favorable (+100), prev all unfavorable (-100) → +200 delta.
        self.assertAlmostEqual(result.favorability_mover.delta_pts, 200.0, places=1)

    def test_bot_flagged_favorability_row_excluded(self):
        from analysis.src.reporting.aggregators.movers import MoversAggregator
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        next_id = self._seed_windows(conn, now)
        # Add one bot-flagged favorable doc to the current window. A-5: it must
        # not inflate the mover's current volume beyond the clean 10.
        cur = now - 1 * 86400
        _seed_doc(conn, next_id, "x_post", f"tw{next_id}", cur, domain="x.com")
        _seed_output(conn, next_id, "favorability", {"overall_gop_stance": "favorable"}, 0.9)
        _seed_output(conn, next_id, "bot_detection",
                     {"label": "bot", "is_bot": True}, 0.9)
        conn.commit()
        conn.close()

        result = MoversAggregator(self.db_path).get_movers("7d")
        self.assertEqual(result.favorability_mover.current_volume, 10)


# --------------------------------------------------------------------------- #
#  A-7 — X classification samples carry a permalink                           #
# --------------------------------------------------------------------------- #

class TestSentimentSampleXPermalink(unittest.TestCase):
    def test_x_post_sample_has_permalink(self):
        from analysis.src.reporting.aggregators.sentiment import _build_sample_dict
        sample = _build_sample_dict(
            doc_id=7, label="POSITIVE", confidence=0.9,
            data={"reasoning": "r", "evidence_spans": []},
            title="t", source_type="x_post", published_at=int(time.time()),
            domain_or_subreddit="x.com", ident="1780000000000000001",
            text="body", x_handle="SpeakerJohnson",
        )
        self.assertEqual(
            sample["url"], "https://x.com/SpeakerJohnson/status/1780000000000000001"
        )


# --------------------------------------------------------------------------- #
#  A-8 / A-9 — ETL stamps published_at and etl_version                        #
# --------------------------------------------------------------------------- #

class TestEtlStamping(_DbTest):
    def test_null_published_at_stamped_and_etl_version_recorded(self):
        from analysis.src.etl.loader import ContentLoader, ETL_VERSION
        conn = sqlite3.connect(self.db_path)
        # A reddit post with NULL created_utc (no publish date) but political.
        conn.execute(
            "INSERT INTO reddit_posts_raw (fullname, subreddit, created_utc, title, "
            "body, raw_hash, extraction_version) VALUES ('t3_x', 'politics', NULL, "
            "'Senate election bill', 'congress vote body', 'rh', 'v1')"
        )
        conn.commit()
        conn.close()

        ContentLoader(self.db_path).load_new_raw_content()

        conn = sqlite3.connect(self.db_path)
        row = conn.execute(
            "SELECT published_at, etl_version FROM docs WHERE ident = 't3_x'"
        ).fetchone()
        conn.close()
        self.assertIsNotNone(row)
        published_at, etl_version = row
        # A-8: NULL date is stamped (non-null, recent) so the doc lands in windows.
        self.assertIsNotNone(published_at)
        self.assertGreater(published_at, int(time.time()) - 120)
        # A-9: etl_version stamped.
        self.assertEqual(etl_version, ETL_VERSION)


if __name__ == "__main__":
    unittest.main()
