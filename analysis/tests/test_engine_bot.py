"""
Tests for analysis/src/engine/bot_detection.py -- Postgres redesign Phase 6,
Wave 3.

Two tiers, matching the repo's established convention for Postgres-redesign
test modules (test_engine_text.py, test_engine_targets.py):

  1. Pure-core tests (no DB) -- analyze() against a fake LLMClient transport
     and hand-built BotDocInput fixtures. Always run.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with data/pg-migrations/0001_north_star.sql applied. Skipped
     (never failed) when the env var is absent.

No heuristic-only fallback exists in this module: an unavailable backend or
an LLM call that fails after retries raises out of analyze(), matching
text.py's contract (see AnalyzeLlmFailureTests / the failed-run integration
test below). The deterministic signal battery is exercised directly
(SignalBatteryTests, against the module's private _compute_signals/
_aggregate_score) since it no longer classifies on its own -- it only feeds
a successful hybrid run's LLM prompt and typed stylometric columns.
"""

from __future__ import annotations

import os
import sys
import unittest
from datetime import datetime, timedelta, timezone

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine import bot_detection as bot
from analysis.src.llm.base import SchemaValidationError
from analysis.src.llm.client import LLMClient
from analysis.tests import pg_fixture


# =============================================================================
# Fakes shared by Tier 1 tests below (same shape as test_engine_text.py's).
# =============================================================================

class FakeTransport:
    """Stands in for a backend's complete_once()/is_available/
    get_token_usage() surface. Each call pops the next scripted outcome --
    an exception to raise, or a dict to return."""

    def __init__(self, outcomes, available=True):
        self.is_available = available
        self._outcomes = list(outcomes)
        self.calls = 0

    def complete_once(self, system_prompt, user_prompt, response_schema=None, temperature=None):
        self.calls += 1
        outcome = self._outcomes.pop(0)
        if isinstance(outcome, Exception):
            raise outcome
        return outcome

    def get_token_usage(self) -> int:
        return 0


def _valid_llm_response(**overrides) -> dict:
    base = {
        "is_bot": False,
        "label": "human",
        "confidence": 0.8,
        "llm_text_likelihood": 0.2,
        "indicators": ["casual phrasing, no automation tells"],
        "reasoning": "Reads as a normal human post.",
    }
    base.update(overrides)
    return base


_CASUAL_TEXT = "lol trump just did it again, cant believe this tbh"

_UNIFORM_LLM_STYLE_TEXT = (
    "It's important to note that this policy has many complex implications—"
    "on the other hand, various factors must be considered when evaluating "
    "this outcome. In conclusion, this remains a “nuanced issue” for voters… "
    "Studies have shown that public opinion is deeply divided on this matter."
)


def _doc(
    doc_id=1,
    source_type="x_post",
    text=_CASUAL_TEXT,
    followers_count=None,
    following_count=None,
    verified_type=None,
    account_created_at=None,
    place_country_code=None,
) -> "bot.BotDocInput":
    return bot.BotDocInput(
        doc_id=doc_id, source_type=source_type, text=text,
        followers_count=followers_count, following_count=following_count,
        verified_type=verified_type, account_created_at=account_created_at,
        place_country_code=place_country_code,
    )


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class AnalyzeEmptyTextTests(unittest.TestCase):
    """The 'unknown' label decision: DDL's analysis.bot_label enum DOES
    include 'unknown' (0001_north_star.sql), used for exactly this case --
    a doc with no text at all, where no signal battery can run."""

    def test_empty_text_yields_unknown_label_without_llm_call(self):
        backend = FakeTransport([_valid_llm_response()])
        client = LLMClient(backend)

        result = bot.analyze(_doc(text=""), client)

        self.assertEqual(backend.calls, 0)
        self.assertEqual(result.label, "unknown")
        self.assertEqual(result.confidence, 0.0)
        self.assertEqual(result.inference_method, "deterministic")
        self.assertIsNone(result.llm_text_likelihood)
        self.assertIsNone(result.raw_response)


class SignalBatteryTests(unittest.TestCase):
    """Deterministic text + account signal battery, tested directly against
    _compute_signals. These never classify: an LLM failure raises rather than
    falling back to them, and since 2026-07-25 they no longer feed a score
    either (_aggregate_score is gone). They are measurements that reach the
    prompt and the typed stylometric columns, nothing more."""

    def test_casual_human_text_has_no_template_signal(self):
        signals = bot._compute_signals(_doc(text=_CASUAL_TEXT))
        self.assertEqual(signals["hedge_phrase_rate"], 0)

    def test_llm_style_uniform_text_trips_stylometric_signals(self):
        signals = bot._compute_signals(_doc(text=_UNIFORM_LLM_STYLE_TEXT))
        self.assertGreater(signals["hedge_phrase_rate"], 0)

    def test_new_low_follower_x_account_trips_both_flags(self):
        recent = datetime.now(timezone.utc) - timedelta(days=5)
        signals = bot._compute_signals(_doc(
            source_type="x_post", followers_count=3, following_count=2000,
            account_created_at=recent,
        ))
        self.assertTrue(signals["x_new_account_flag"])
        self.assertTrue(signals["x_low_followers_flag"])

    def test_follow_ratio_anomaly_is_flagged_for_x_only(self):
        signals = bot._compute_signals(
            _doc(source_type="x_post", followers_count=5, following_count=5000)
        )
        self.assertTrue(signals["follow_ratio_anomaly"])

    def test_reddit_doc_never_trips_x_specific_flags(self):
        """Reddit has no author snapshot today (etl/authors.py::
        sync_reddit_authors is a no-op) -- even if a caller somehow supplied
        follower-shaped fields on a reddit_post doc, the x_post gate must
        keep them inert."""
        signals = bot._compute_signals(_doc(
            source_type="reddit_post", followers_count=1, following_count=9999,
            account_created_at=datetime.now(timezone.utc) - timedelta(days=1),
        ))
        self.assertFalse(signals["x_new_account_flag"])
        self.assertFalse(signals["x_low_followers_flag"])
        self.assertFalse(signals["follow_ratio_anomaly"])

    def test_verified_type_is_measured_but_no_longer_gates_anything(self):
        """The de-bias gate that hard-zeroed government-verified accounts and
        capped business accounts at 0.3 went with _aggregate_score. It was
        itself a hand-tuned rule overriding the model. verified_type is still
        captured so it reaches the prompt and raw_response, but nothing in
        the engine now uses it to override a judgment."""
        signals = bot._compute_signals(_doc(
            source_type="x_post", text=_UNIFORM_LLM_STYLE_TEXT,
            verified_type="government", followers_count=2, following_count=9000,
        ))
        self.assertEqual(signals["verified_type"], "government")
        self.assertNotIn("aggregated_score", signals)


class HybridAnalyzeTests(unittest.TestCase):
    """A successful LLM call is always 'hybrid': the deterministic battery
    (computed first, feeding the prompt) lands in the typed stylometric
    columns, and the LLM's own judgment sets label/confidence."""

    def test_llm_available_and_successful_is_hybrid(self):
        client = LLMClient(FakeTransport([_valid_llm_response(label="bot", confidence=0.9)]))
        result = bot.analyze(_doc(), client)

        self.assertEqual(result.inference_method, "hybrid")
        self.assertEqual(result.label, "bot")
        self.assertEqual(result.confidence, 0.9)
        self.assertIsNotNone(result.raw_response)
        self.assertIn("llm", result.raw_response)
        self.assertIn("deterministic_signals", result.raw_response)

    def test_typed_stylometrics_are_populated_from_the_signal_battery(self):
        client = LLMClient(FakeTransport([_valid_llm_response()]))
        result = bot.analyze(_doc(text=_UNIFORM_LLM_STYLE_TEXT), client)

        expected = bot._compute_signals(_doc(text=_UNIFORM_LLM_STYLE_TEXT))
        self.assertEqual(result.burstiness, expected["sentence_length_variance"])
        self.assertEqual(result.type_token_ratio, expected["unique_ratio"])
        self.assertEqual(result.template_score, expected["hedge_phrase_rate"])

    def test_schema_invalid_response_retries_then_succeeds_as_hybrid(self):
        backend = FakeTransport([SchemaValidationError("bad enum"), _valid_llm_response()])
        client = LLMClient(backend, max_retries=3)

        import unittest.mock as mock
        with mock.patch("analysis.src.llm.client.time.sleep"):
            result = bot.analyze(_doc(), client)

        self.assertEqual(backend.calls, 2)
        self.assertEqual(result.inference_method, "hybrid")


class AnalyzeLlmFailureTests(unittest.TestCase):
    """The deliberate behavior change: no heuristic fallback -- a failed or
    unavailable LLM call must raise out of analyze(), identical to
    text.py's contract, not silently degrade to a lower-quality
    deterministic guess. The deterministic battery still runs before the
    LLM call, but never classifies on its own outside the empty-text
    'unknown' case."""

    def test_unavailable_backend_raises(self):
        client = LLMClient(FakeTransport([_valid_llm_response()], available=False))

        with self.assertRaises(RuntimeError):
            bot.analyze(_doc(text=_CASUAL_TEXT), client)

    def test_exhausted_retries_raises(self):
        backend = FakeTransport([RuntimeError("e1"), RuntimeError("e2")])
        client = LLMClient(backend, max_retries=2)

        import unittest.mock as mock
        with mock.patch("analysis.src.llm.client.time.sleep"):
            with self.assertRaises(RuntimeError):
                bot.analyze(_doc(text=_CASUAL_TEXT), client)


class IndicatorSanitizationTests(unittest.TestCase):
    def test_noise_indicators_from_llm_are_dropped(self):
        client = LLMClient(FakeTransport([_valid_llm_response(indicators=[
            "account_age=None days", "followers=0", "hedge_phrase_rate=1.8", "",
        ])]))
        result = bot.analyze(_doc(), client)

        self.assertEqual(result.indicators, ["hedge_phrase_rate=1.8"])


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class ProcessIntegrationTests(unittest.TestCase):
    """Live-run against a real Postgres with 0001 applied."""

    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate_all()

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate_all(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.bot_signals, analysis.runs, analysis.prompt_versions, "
                "analysis.author_bot_scores, corpus.documents, corpus.authors CASCADE"
            )

    def _seed_author(self, platform_author_id: str, **cols) -> int:
        from analysis.src.results import store
        fields = {"followers_count": None, "following_count": None, "verified_type": None,
                  "account_created_at": None}
        fields.update(cols)
        with store.db.connection() as conn:
            row = conn.execute(
                """
                INSERT INTO corpus.authors
                    (platform, platform_author_id, followers_count, following_count,
                     verified_type, account_created_at)
                VALUES ('x', %s, %s, %s, %s, %s)
                RETURNING author_id
                """,
                (platform_author_id, fields["followers_count"], fields["following_count"],
                 fields["verified_type"], fields["account_created_at"]),
            ).fetchone()
            return row["author_id"]

    def _seed_doc(self, natural_key: str, author_id=None, source_type="x_post") -> int:
        from analysis.src.results import store
        with store.db.connection() as conn:
            row = conn.execute(
                """
                INSERT INTO corpus.documents
                    (source_type, natural_key, author_id, published_at, body,
                     source_url, raw_hash, etl_version)
                VALUES (%s, %s, %s, now(), %s, 'http://example.com/' || %s,
                        'deadbeef', 'test')
                RETURNING doc_id
                """,
                (source_type, natural_key, author_id, _CASUAL_TEXT, natural_key),
            ).fetchone()
            return row["doc_id"]

    def _run_row(self, run_id):
        from analysis.src.results import store
        with store.db.connection() as conn:
            return conn.execute(
                "SELECT * FROM analysis.runs WHERE run_id = %s", (run_id,)
            ).fetchone()

    def _bot_signals_row(self, run_id):
        from analysis.src.results import store
        with store.db.connection() as conn:
            return conn.execute(
                "SELECT * FROM analysis.bot_signals WHERE run_id = %s", (run_id,)
            ).fetchone()

    def test_hybrid_process_persists_run_with_prompt_version(self):
        author_id = self._seed_author("u2")
        doc_id = self._seed_doc("doc-2", author_id=author_id)
        client = LLMClient(FakeTransport([_valid_llm_response(label="bot", confidence=0.9)]))

        run_id = bot.process(bot.BotDocInput(doc_id=doc_id, source_type="x_post", text=_CASUAL_TEXT), client).run_id

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "done")
        self.assertEqual(run["inference_method"], "hybrid")
        self.assertIsNotNone(run["prompt_version_id"])
        signals_row = self._bot_signals_row(run_id)
        self.assertEqual(signals_row["label"], "bot")

    def test_empty_text_process_persists_unknown_label(self):
        author_id = self._seed_author("u3")
        doc_id = self._seed_doc("doc-3", author_id=author_id)
        client = LLMClient(FakeTransport([], available=False))

        run_id = bot.process(bot.BotDocInput(doc_id=doc_id, source_type="x_post", text=""), client).run_id

        run = self._run_row(run_id)
        self.assertEqual(run["inference_method"], "deterministic")
        signals_row = self._bot_signals_row(run_id)
        self.assertEqual(signals_row["label"], "unknown")
        self.assertIsNone(signals_row["llm_text_likelihood"])

    def test_llm_failure_process_persists_failed_run_with_no_signals_row(self):
        """The spec's new failure contract: client unavailable (or retries
        exhausted) -> analyze() raises -> process() records a failed run
        with the error and writes no bot_signals row at all -- the queue's
        retry/attempts mechanism owns recovery, not a heuristic degrade."""
        author_id = self._seed_author("u4")
        doc_id = self._seed_doc("doc-4", author_id=author_id)
        client = LLMClient(FakeTransport([_valid_llm_response()], available=False))

        run_id = bot.process(bot.BotDocInput(doc_id=doc_id, source_type="x_post", text=_CASUAL_TEXT), client).run_id

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "failed")
        self.assertFalse(run["is_current"])
        self.assertIsNotNone(run["error"])
        self.assertIsNone(self._bot_signals_row(run_id))


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class RefreshAuthorBotScoresIntegrationTests(unittest.TestCase):
    """refresh_author_bot_scores() aggregates per-author over current bot
    runs -- verified against a hand-computed fixture (2 authors, several
    docs each), plus reprocess supersession, the confidence floor
    (BOT_LABEL_MIN_CONFIDENCE), and the label-not-llm_text_likelihood
    exclusion contract (owner decision 2026-07-25,
    docs/audit-trail/analysis/2026-07-25-bot-exclusion-gate.md)."""

    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate_all()

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate_all(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.bot_signals, analysis.runs, analysis.prompt_versions, "
                "analysis.author_bot_scores, corpus.documents, corpus.authors CASCADE"
            )

    def _seed_author(self, platform_author_id: str) -> int:
        from analysis.src.results import store
        with store.db.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id) "
                "VALUES ('x', %s) RETURNING author_id",
                (platform_author_id,),
            ).fetchone()
            return row["author_id"]

    def _seed_doc(self, natural_key: str, author_id: int) -> int:
        from analysis.src.results import store
        with store.db.connection() as conn:
            row = conn.execute(
                """
                INSERT INTO corpus.documents
                    (source_type, natural_key, author_id, published_at, body,
                     source_url, raw_hash, etl_version)
                VALUES ('x_post', %s, %s, now(), 'test body',
                        'http://example.com/' || %s, 'deadbeef', 'test')
                RETURNING doc_id
                """,
                (natural_key, author_id, natural_key),
            ).fetchone()
            return row["doc_id"]

    def _process_with_signal(self, doc_id, label, llm_text_likelihood, *, confidence=0.9):
        """Writes one done bot run + bot_signals row with a fixed
        label/confidence, bypassing analyze() so the rollup counts -- and
        the confidence-floor gate on bot_post_count/suspicious_post_count --
        can be exercised directly against known inputs."""
        from analysis.src.results import store
        handle = store.open_run(
            bot.BOT_TASK, doc_id=doc_id, model_id="test-model", inference_method="deterministic",
        )
        handle.save_bot_signals(store.BotSignalsRow(
            label=label, llm_text_likelihood=llm_text_likelihood,
        ))
        return handle.finish("done", confidence=confidence)

    def test_aggregates_two_authors_with_hand_computed_counts(self):
        author_a = self._seed_author("author-a")
        author_b = self._seed_author("author-b")

        # Author A: one human, one suspicious, one bot -- all HIGH confidence.
        doc_a1 = self._seed_doc("a1", author_a)
        doc_a2 = self._seed_doc("a2", author_a)
        doc_a3 = self._seed_doc("a3", author_a)
        self._process_with_signal(doc_a1, "human", 0.1)
        self._process_with_signal(doc_a2, "suspicious", 0.3)
        self._process_with_signal(doc_a3, "bot", 0.8)

        # Author B: two human docs.
        doc_b1 = self._seed_doc("b1", author_b)
        doc_b2 = self._seed_doc("b2", author_b)
        self._process_with_signal(doc_b1, "human", 0.0)
        self._process_with_signal(doc_b2, "human", 0.2)

        written = bot.refresh_author_bot_scores()
        self.assertEqual(written, 2)

        from analysis.src.results import store
        with store.db.connection() as conn:
            rows = {
                r["author_id"]: r
                for r in conn.execute("SELECT * FROM analysis.author_bot_scores").fetchall()
            }

        row_a = rows[author_a]
        # 0005_drop_bot_score.sql retired the additive score/variance pair --
        # a regression that reintroduces either column would fail here.
        self.assertNotIn("score", row_a)
        self.assertNotIn("variance", row_a)
        self.assertEqual(row_a["sample_count"], 3)
        self.assertEqual(row_a["bot_post_count"], 1)
        self.assertEqual(row_a["suspicious_post_count"], 1)
        self.assertAlmostEqual(row_a["llm_text_likelihood_mean"], (0.1 + 0.3 + 0.8) / 3, places=6)

        row_b = rows[author_b]
        self.assertEqual(row_b["sample_count"], 2)
        self.assertEqual(row_b["bot_post_count"], 0)
        self.assertEqual(row_b["suspicious_post_count"], 0)

    def test_reprocess_supersession_updates_rollup_on_refresh(self):
        author_a = self._seed_author("author-a")
        doc_a1 = self._seed_doc("a1", author_a)

        self._process_with_signal(doc_a1, "bot", 0.9)
        bot.refresh_author_bot_scores()

        from analysis.src.results import store
        with store.db.connection() as conn:
            before = conn.execute(
                "SELECT * FROM analysis.author_bot_scores WHERE author_id = %s", (author_a,)
            ).fetchone()
        self.assertEqual(before["bot_post_count"], 1)

        # Reprocess the same doc: the new run supersedes the old one
        # (is_current flip), so refresh must reflect ONLY the new label.
        self._process_with_signal(doc_a1, "human", 0.05)
        bot.refresh_author_bot_scores()

        with store.db.connection() as conn:
            after = conn.execute(
                "SELECT * FROM analysis.author_bot_scores WHERE author_id = %s", (author_a,)
            ).fetchone()
        self.assertEqual(after["sample_count"], 1)
        self.assertEqual(after["bot_post_count"], 0)

    def test_author_with_no_qualifying_runs_is_removed_on_refresh(self):
        """An author previously rolled up whose only doc later reprocesses
        to 'unknown' (empty text) must be deleted, not left stale."""
        author_a = self._seed_author("author-a")
        doc_a1 = self._seed_doc("a1", author_a)
        self._process_with_signal(doc_a1, "bot", 0.7)
        bot.refresh_author_bot_scores()

        from analysis.src.results import store
        with store.db.connection() as conn:
            count_before = conn.execute(
                "SELECT count(*) AS n FROM analysis.author_bot_scores WHERE author_id = %s",
                (author_a,),
            ).fetchone()["n"]
        self.assertEqual(count_before, 1)

        # Supersede with an 'unknown' (empty-text) run -- no label to aggregate.
        handle = store.open_run(
            bot.BOT_TASK, doc_id=doc_a1, model_id="test-model", inference_method="deterministic",
        )
        handle.save_bot_signals(store.BotSignalsRow(label="unknown"))
        handle.finish("done")
        bot.refresh_author_bot_scores()

        with store.db.connection() as conn:
            count_after = conn.execute(
                "SELECT count(*) AS n FROM analysis.author_bot_scores WHERE author_id = %s",
                (author_a,),
            ).fetchone()["n"]
        self.assertEqual(count_after, 0)

    def test_low_confidence_bot_labels_do_not_count_toward_bot_post_count(self):
        """Confidence-floor gate (BOT_LABEL_MIN_CONFIDENCE): an author whose
        posts are ALL labelled 'bot' but at LOW confidence must not
        accumulate bot_post_count -- a low-confidence guess must not silence
        (exclude) an author from downstream panels. Contrast with
        test_aggregates_two_authors_with_hand_computed_counts, where the
        same label at HIGH confidence DOES count."""
        author = self._seed_author("low-conf-author")
        docs = [self._seed_doc(f"low-conf-{i}", author) for i in range(3)]
        for doc_id in docs:
            self._process_with_signal(doc_id, "bot", 0.9, confidence=0.2)

        bot.refresh_author_bot_scores()

        from analysis.src.results import store
        with store.db.connection() as conn:
            row = conn.execute(
                "SELECT * FROM analysis.author_bot_scores WHERE author_id = %s", (author,)
            ).fetchone()
        # All 3 posts still count in the denominator (label != 'unknown');
        # none count as bot_post_count (confidence below the floor).
        self.assertEqual(row["sample_count"], 3)
        self.assertEqual(row["bot_post_count"], 0)

    def test_high_llm_text_likelihood_with_human_label_does_not_count_as_bot(self):
        """The whole point of moving off llm_text_likelihood (2026-07-25):
        text that READS as machine-written but is LABELLED human must not
        count toward bot_post_count/suspicious_post_count -- only the
        model's own bot/suspicious/human verdict does."""
        author = self._seed_author("llm-style-human-author")
        doc_id = self._seed_doc("llm-style-doc", author)
        self._process_with_signal(doc_id, "human", 0.95, confidence=0.9)

        bot.refresh_author_bot_scores()

        from analysis.src.results import store
        with store.db.connection() as conn:
            row = conn.execute(
                "SELECT * FROM analysis.author_bot_scores WHERE author_id = %s", (author,)
            ).fetchone()
        self.assertEqual(row["bot_post_count"], 0)
        self.assertEqual(row["suspicious_post_count"], 0)
        self.assertAlmostEqual(row["llm_text_likelihood_mean"], 0.95, places=6)


if __name__ == "__main__":
    unittest.main()
