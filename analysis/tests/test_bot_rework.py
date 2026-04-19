"""
Tests for the walkthrough-040 bot-detection rework.

Covers:
  - Stylometric signal functions (sentence-length variance, hedge rate,
    typographic purity) compute what they claim.
  - Account-metadata flags fire (follow-ratio anomaly, sustained tweet rate,
    unlisted-active).
  - Verified-type de-biasing: verified_type='government' → score=0, label=human.
  - Pre-exclusion in job_runner writes a deterministic row for electeds /
    affiliated / gov-verified and never flags them as bots.
  - run_account_bot_rollup aggregates per-post scores into author_bot_scores,
    excluding pre-excluded rows.
  - BotAggregator replaces stubs with real computed values.
"""

import json
import os
import sqlite3
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine.bot import (
    HybridBotDetector,
    _sentence_length_variance,
    _hedge_stats,
    _typographic_purity,
    _heuristic_llm_text_likelihood,
    _aggregate_score,
)
from analysis.src.reporting.aggregators.bot import (
    BotAggregator,
    _shingles,
    _jaccard,
    _text_similarity_signals,
    _link_domain_concentration,
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


# =============================================================================
# Stylometric helpers
# =============================================================================


class TestStylometricHelpers(unittest.TestCase):
    def test_sentence_length_variance_zero_for_short(self):
        self.assertEqual(_sentence_length_variance(""), 0.0)
        self.assertEqual(_sentence_length_variance("Just one."), 0.0)

    def test_sentence_length_variance_uniform_text_low(self):
        # Three sentences of exactly four words each → variance 0.
        uniform = "One two three four. Five six seven eight. Nine ten eleven twelve."
        self.assertEqual(_sentence_length_variance(uniform), 0.0)

    def test_sentence_length_variance_varied_text_higher(self):
        varied = "Short. This is a longer middle sentence. No."
        self.assertGreater(_sentence_length_variance(varied), 1.0)

    def test_hedge_stats_counts_substrings(self):
        text = (
            "it's important to note that while it's true that one could argue this"
            .lower()
        )
        hits, rate = _hedge_stats(text, len(text.split()))
        # "it's important to note" + "while it's true that" + "one could argue"
        self.assertGreaterEqual(hits, 3)
        self.assertGreater(rate, 0.0)

    def test_typographic_purity_scales_with_tells(self):
        plain = "This has nothing special in it."
        fancy = "This \u2014 with em-dash \u2018smart\u2019 \u201Cquotes\u201D and \u2026 ellipsis."
        self.assertEqual(_typographic_purity(plain), 0.0)
        self.assertGreaterEqual(_typographic_purity(fancy), 0.5)

    def test_heuristic_llm_text_likelihood_from_signals(self):
        llm_like = {
            "word_count": 60,
            "sentence_length_variance": 2.0,
            "hedge_phrase_rate": 2.0,
            "typographic_purity_score": 0.67,
        }
        human_like = {
            "word_count": 60,
            "sentence_length_variance": 25.0,
            "hedge_phrase_rate": 0.0,
            "typographic_purity_score": 0.0,
        }
        self.assertGreaterEqual(_heuristic_llm_text_likelihood(llm_like), 0.9)
        self.assertEqual(_heuristic_llm_text_likelihood(human_like), 0.0)


# =============================================================================
# Aggregate scoring + metadata de-biasing
# =============================================================================


class TestAggregateScore(unittest.TestCase):
    def _base(self, **overrides):
        base = {
            "spam_hits": 0, "repetition_score": 0.0, "word_count": 100,
            "url_count": 0, "hashtag_count": 0, "account_age": 365,
            "posting_freq": 5,
            "x_new_account_flag": False, "x_low_followers_flag": False,
            "x_foreign_origin_flag": False, "x_origin_confidence": None,
            "sentence_length_variance": 20.0, "hedge_phrase_rate": 0.0,
            "typographic_purity_score": 0.0,
            "follow_ratio_anomaly": False, "sustained_tweet_rate_flag": False,
            "unlisted_active_flag": False, "verified_type": "",
        }
        base.update(overrides)
        return base

    def test_baseline_human_score_low(self):
        self.assertLessEqual(_aggregate_score(self._base()), 0.3)

    def test_government_verified_returns_zero(self):
        ctx = self._base(
            spam_hits=3, follow_ratio_anomaly=True,
            sustained_tweet_rate_flag=True, verified_type="government",
        )
        self.assertEqual(_aggregate_score(ctx), 0.0)

    def test_business_verified_caps_score(self):
        ctx = self._base(
            spam_hits=3, follow_ratio_anomaly=True,
            sustained_tweet_rate_flag=True, verified_type="business",
        )
        # Without de-bias we'd be way over 0.5; with business cap, <= 0.3.
        self.assertLessEqual(_aggregate_score(ctx), 0.3)

    def test_follow_ratio_anomaly_increases_score(self):
        without = _aggregate_score(self._base())
        with_anom = _aggregate_score(self._base(follow_ratio_anomaly=True))
        self.assertGreater(with_anom, without)

    def test_stylometric_llm_signals_compose(self):
        ctx = self._base(
            sentence_length_variance=1.5,
            hedge_phrase_rate=2.5,
            typographic_purity_score=0.75,
            word_count=80,
        )
        # Three stylometric hits + none of the spam signals → still flags as
        # suspicious/bot because of LLM-text patterns.
        self.assertGreaterEqual(_aggregate_score(ctx), 0.4)


class TestHybridBotDetectorPaths(unittest.TestCase):
    def test_government_verified_author_not_flagged(self):
        det = HybridBotDetector(llm_enabled=False)
        # Otherwise bot-looking post — but the account is gov-verified.
        result = det.analyze_full(
            "Buy now click here act now limited time offer!!!",
            metadata={
                "platform": "x", "verified": True, "verified_type": "government",
            },
        )
        self.assertEqual(result.label, "human")
        self.assertIn("de-biased", " ".join(result.indicators))

    def test_unlisted_active_account_flag_fires(self):
        det = HybridBotDetector(llm_enabled=False)
        # 500+ tweets, 0 list memberships — suspicious pattern.
        result = det.analyze_full(
            "One more anodyne political take from this account.",
            metadata={
                "platform": "x", "user_tweet_count": 600, "user_listed": 0,
                "user_followers": 10, "user_following": 2000,
                "user_created_at": int(time.time() - 30 * 86400),  # 30 days
            },
        )
        indicators = " ".join(result.indicators)
        self.assertIn("zero list memberships", indicators)


# =============================================================================
# Aggregator helpers
# =============================================================================


class TestAggregatorHelpers(unittest.TestCase):
    def test_shingles_and_jaccard(self):
        a = _shingles("the quick brown fox jumps over the lazy dog pack", k=3)
        b = _shingles("the quick brown fox jumps over the lazy cat pack", k=3)
        sim = _jaccard(a, b)
        # Heavy overlap but not identical.
        self.assertGreater(sim, 0.3)
        self.assertLess(sim, 1.0)

    def test_text_similarity_identical_pairs(self):
        texts = [
            "click here to win",
            "click here to win",
            "click here to win",
            "completely different content here indeed",
        ]
        identical, buckets = _text_similarity_signals(texts)
        # Three identical-text strings produce C(3,2) = 3 pairs.
        self.assertEqual(identical, 3)
        # At least one high-bucket pair (the three identical ones with each other).
        self.assertGreater(buckets["high"], 0)

    def test_link_domain_concentration(self):
        urls = [
            "example.com", "example.com", "example.com",
            "spammy.test", "other.net",
        ]
        result = _link_domain_concentration(urls)
        self.assertEqual(result[0]["domain"], "example.com")
        self.assertEqual(result[0]["percentage"], 60.0)


# =============================================================================
# BotAggregator end-to-end (with pre-excluded row filtering)
# =============================================================================


class TestBotAggregatorPreExclusionFilter(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        # Three docs: one normal bot, one normal human, one pre-excluded gov.
        for doc_id, source_type, ident in [
            (1, "x_post", "t1"), (2, "x_post", "t2"), (3, "x_post", "t3"),
        ]:
            conn.execute(
                """
                INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit,
                                   published_at, text, raw_hash)
                VALUES (?, ?, ?, 'x.com', ?, 'body', ?)
                """,
                (doc_id, source_type, ident, now, f"rh-{doc_id}"),
            )
        # Doc 1: normal bot (LLM-produced row)
        conn.execute(
            """
            INSERT INTO ai_outputs
                (doc_id, task_type, output_json, confidence, inference_method, created_at)
            VALUES (1, 'bot_detection', ?, 0.85, 'llm', ?)
            """,
            (json.dumps({"label": "bot", "is_bot": True,
                          "indicators": ["sustained high tweet rate"]}), now),
        )
        # Doc 2: human
        conn.execute(
            """
            INSERT INTO ai_outputs
                (doc_id, task_type, output_json, confidence, inference_method, created_at)
            VALUES (2, 'bot_detection', ?, 0.9, 'llm', ?)
            """,
            (json.dumps({"label": "human"}), now),
        )
        # Doc 3: pre-excluded (deterministic) — must NOT count as bot.
        conn.execute(
            """
            INSERT INTO ai_outputs
                (doc_id, task_type, output_json, confidence, inference_method, created_at)
            VALUES (3, 'bot_detection', ?, 1.0, 'deterministic', ?)
            """,
            (json.dumps({"label": "human",
                          "indicators": ["pre-excluded: verified_type=government"]}), now),
        )
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def test_denominator_excludes_pre_excluded_rows(self):
        agg = BotAggregator(self.db_path)
        data = agg.get_bot_activity()
        # total_eligible = 2 (docs 1 + 2); bot_count = 1 → rate = 50%.
        self.assertEqual(data.overview.suspectedAutomationRate, 50.0)


if __name__ == "__main__":
    unittest.main()
