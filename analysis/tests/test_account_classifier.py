"""
Tests for the account tier classifier (walkthrough 036).

Covers:
  - Curated-YAML loader seeds account_profiles correctly and is idempotent.
  - Curated rerun with a changed tier overwrites the earlier row.
  - LLM classifier path via a mock client — affirmative tier is persisted,
    and a second run does not overwrite it.
  - NarrativeAggregator attaches first_seen_tier for x_post narratives via
    the account_profiles join, and defaults to general_public when the
    X author is unclassified.
"""

import json
import os
import sqlite3
import sys
import tempfile
import textwrap
import time
import unittest
from typing import Optional

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine.account_classifier import (
    AccountClassifier, ClassificationResult, _parse_curated_yaml,
)
from analysis.src.reporting.aggregators.narrative import NarrativeAggregator

import yaml as yaml_lib

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


def _seed_x_user(conn, user_id: str, username: str, followers: int = 0, verified: int = 0):
    conn.execute(
        """
        INSERT INTO x_users_raw (user_id, username, name, followers_count, verified,
                                  fetched_at, raw_hash)
        VALUES (?, ?, ?, ?, ?, ?, ?)
        """,
        (user_id, username, username, followers, verified, int(time.time()), "rh-" + user_id),
    )


def _seed_x_post(conn, tweet_id: str, author_id: str, text: str, created_at: int):
    conn.execute(
        """
        INSERT INTO x_posts_raw (tweet_id, author_id, created_at, fetched_at, text,
                                  raw_hash, extraction_version)
        VALUES (?, ?, ?, ?, ?, ?, ?)
        """,
        (tweet_id, author_id, created_at, created_at, text, "rh-" + tweet_id, "go-v1.0"),
    )


def _seed_doc_x(conn, doc_id: int, tweet_id: str, source_type: str = "x_post"):
    conn.execute(
        """
        INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit,
                           published_at, text, raw_hash)
        VALUES (?, ?, ?, ?, ?, ?, ?)
        """,
        (doc_id, source_type, tweet_id, "x.com", int(time.time()), "body", "rh-doc-" + str(doc_id)),
    )


def _seed_narrative_with_doc(conn, narrative_id: int, doc_id: int, name: str):
    now = int(time.time())
    conn.execute(
        """
        INSERT INTO narratives (narrative_id, name, description, first_seen_at,
                                 first_seen_doc_id, created_at, updated_at)
        VALUES (?, ?, ?, ?, ?, ?, ?)
        """,
        (narrative_id, name, name, now, doc_id, now, now),
    )
    conn.execute(
        """
        INSERT INTO narrative_docs (narrative_id, doc_id, discovered_at, confidence)
        VALUES (?, ?, ?, ?)
        """,
        (narrative_id, doc_id, now, 0.9),
    )


class _FakeLLMClient:
    """Minimal LLM client double for AccountClassifier tests."""

    is_available = True

    def __init__(self, responses: dict):
        # responses keyed by handle → dict of tier/confidence/reasoning
        self._responses = responses
        self.calls = []

    def complete(self, system_prompt, user_prompt, response_schema=None):
        # Pull handle out of the user_prompt (ACCOUNT_CLASSIFIER_USER_PROMPT_TEMPLATE
        # inserts "Handle: @<handle>") to pick a canned response.
        handle = ""
        for line in user_prompt.splitlines():
            if line.startswith("Handle: @"):
                handle = line.split("@", 1)[1].strip()
                break
        self.calls.append(handle)
        return self._responses.get(handle, {
            "tier": "general_public",
            "confidence": 0.5,
            "reasoning": "default fake",
        })


class TestCuratedLoader(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        # Seed one x_users_raw row so the handle → author_id lookup resolves.
        conn = sqlite3.connect(self.db_path)
        _seed_x_user(conn, user_id="u_potus", username="POTUS")
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def _write_yaml(self, body: str) -> str:
        fd, path = tempfile.mkstemp(suffix=".yaml")
        with os.fdopen(fd, "w") as f:
            f.write(textwrap.dedent(body))
        self.addCleanup(lambda: os.unlink(path))
        return path

    def test_curated_loader_writes_rows_with_correct_tier(self):
        yaml_path = self._write_yaml("""
        elected_official:
          - handle: POTUS
            notes: "President"
        affiliated:
          - handle: GOP
            notes: "RNC"
        """)
        clf = AccountClassifier(self.db_path, llm_enabled=False)
        counts = clf.load_curated(yaml_path)
        self.assertEqual(counts["elected_official"], 1)
        self.assertEqual(counts["affiliated"], 1)

        conn = sqlite3.connect(self.db_path)
        rows = conn.execute(
            "SELECT author_id, tier, classification_method, notes "
            "FROM account_profiles ORDER BY author_id"
        ).fetchall()
        conn.close()
        # POTUS resolves to its seeded user_id (u_potus). GOP is unseen, so
        # falls back to the lowercased handle.
        tiers = {r[0]: r[1] for r in rows}
        self.assertEqual(tiers["u_potus"], "elected_official")
        self.assertEqual(tiers["gop"], "affiliated")
        methods = {r[0]: r[2] for r in rows}
        self.assertEqual(methods["u_potus"], "curated_list")
        self.assertEqual(methods["gop"], "curated_list")

    def test_curated_loader_is_idempotent(self):
        yaml_path = self._write_yaml("""
        elected_official:
          - handle: POTUS
        """)
        clf = AccountClassifier(self.db_path, llm_enabled=False)
        clf.load_curated(yaml_path)
        clf.load_curated(yaml_path)
        conn = sqlite3.connect(self.db_path)
        (count,) = conn.execute("SELECT COUNT(*) FROM account_profiles").fetchone()
        conn.close()
        self.assertEqual(count, 1)

    def test_curated_rerun_overwrites_tier_change(self):
        yaml1 = self._write_yaml("""
        elected_official:
          - handle: POTUS
        """)
        yaml2 = self._write_yaml("""
        affiliated:
          - handle: POTUS
            notes: "reclassified"
        """)
        clf = AccountClassifier(self.db_path, llm_enabled=False)
        clf.load_curated(yaml1)
        clf.load_curated(yaml2)
        conn = sqlite3.connect(self.db_path)
        row = conn.execute(
            "SELECT tier, notes FROM account_profiles WHERE author_id = 'u_potus'"
        ).fetchone()
        conn.close()
        self.assertEqual(row[0], "affiliated")
        self.assertEqual(row[1], "reclassified")


class TestLLMClassifier(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        _seed_x_user(conn, user_id="u1", username="journo1", followers=50000)
        _seed_x_user(conn, user_id="u2", username="randomuser", followers=50)
        # Meet the volume threshold (>=3 posts in window) for both.
        for i in range(3):
            _seed_x_post(conn, tweet_id=f"t_u1_{i}", author_id="u1",
                         text="politics post", created_at=now - i * 3600)
            _seed_x_post(conn, tweet_id=f"t_u2_{i}", author_id="u2",
                         text="some random take", created_at=now - i * 3600)
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def test_llm_classifier_persists_affirmative_tier(self):
        fake = _FakeLLMClient({
            "journo1": {"tier": "affiliated", "confidence": 0.8,
                         "reasoning": "bio says journalist"},
            "randomuser": {"tier": "general_public", "confidence": 0.6,
                           "reasoning": "no known role"},
        })
        clf = AccountClassifier(self.db_path, llm_enabled=False)
        # Force the fake client into place without taking the real init path.
        clf._llm_client = fake
        clf.llm_enabled = True
        written = clf.classify_with_llm(limit=10)
        self.assertEqual(written, 2)

        conn = sqlite3.connect(self.db_path)
        rows = {
            row[0]: (row[1], row[2])
            for row in conn.execute(
                "SELECT author_id, tier, classification_method FROM account_profiles"
            ).fetchall()
        }
        conn.close()
        self.assertEqual(rows["u1"], ("affiliated", "llm"))
        self.assertEqual(rows["u2"], ("general_public", "llm"))

    def test_llm_classifier_skips_already_classified(self):
        fake = _FakeLLMClient({
            "journo1": {"tier": "affiliated", "confidence": 0.8, "reasoning": "x"},
            "randomuser": {"tier": "general_public", "confidence": 0.6, "reasoning": "y"},
        })
        clf = AccountClassifier(self.db_path, llm_enabled=False)
        clf._llm_client = fake
        clf.llm_enabled = True
        clf.classify_with_llm(limit=10)
        initial_calls = len(fake.calls)
        # Second pass should find no unclassified authors.
        fake.calls.clear()
        clf.classify_with_llm(limit=10)
        self.assertEqual(len(fake.calls), 0)
        self.assertEqual(initial_calls, 2)


class TestParseCuratedYAMLRichFormat(unittest.TestCase):
    """The production YAML uses the rich nested shape from walkthrough 036's
    follow-up (accounts.executive_branch + accounts.congress.{house,senate}).
    Verify the parser flattens both shapes into CuratedEntry rows with the
    right faction metadata populated.
    """

    def test_executive_branch_multiple_handles_per_person(self):
        payload = yaml_lib.safe_load("""
        accounts:
          executive_branch:
            - name: Donald J. Trump
              office: President of the United States
              branch: Executive
              accounts:
                - platform: x
                  handle: '@POTUS'
                  account_type: official
                - platform: x
                  handle: '@realDonaldTrump'
                  account_type: personal/political
                - platform: x
                  handle: '@WhiteHouse'
                  account_type: institutional
        """)
        entries = _parse_curated_yaml(payload)
        self.assertEqual(len(entries), 3)
        by_handle = {e.handle: e for e in entries}
        self.assertIn("POTUS", by_handle)
        self.assertIn("realDonaldTrump", by_handle)
        self.assertIn("WhiteHouse", by_handle)
        for e in entries:
            self.assertEqual(e.tier, "elected_official")
            self.assertEqual(e.full_name, "Donald J. Trump")
            self.assertEqual(e.branch, "executive")
            self.assertEqual(e.office_title, "President of the United States")
        self.assertEqual(by_handle["POTUS"].account_type, "official")
        self.assertEqual(by_handle["realDonaldTrump"].account_type, "personal/political")
        self.assertEqual(by_handle["WhiteHouse"].account_type, "institutional")

    def test_congress_house_and_senate(self):
        payload = yaml_lib.safe_load("""
        accounts:
          congress:
            house:
              - name: Alma Adams
                handle: '@RepAdams'
                chamber: House
                state_or_district: NC12
                party: D
            senate:
              - name: Todd Young
                handle: '@SenToddYoung'
                chamber: Senate
                state_or_district: IN
                party: R
        """)
        entries = _parse_curated_yaml(payload)
        self.assertEqual(len(entries), 2)
        by_handle = {e.handle: e for e in entries}

        adams = by_handle["RepAdams"]
        self.assertEqual(adams.tier, "elected_official")
        self.assertEqual(adams.full_name, "Alma Adams")
        self.assertEqual(adams.party, "D")
        self.assertEqual(adams.branch, "legislative")
        self.assertEqual(adams.chamber, "house")
        self.assertEqual(adams.state_or_district, "NC12")
        self.assertEqual(adams.office_title, "Representative")

        young = by_handle["SenToddYoung"]
        self.assertEqual(young.party, "R")
        self.assertEqual(young.chamber, "senate")
        self.assertEqual(young.office_title, "Senator")
        self.assertEqual(young.state_or_district, "IN")

    def test_legacy_flat_format_still_parses(self):
        payload = yaml_lib.safe_load("""
        elected_official:
          - handle: POTUS
            notes: "President"
        affiliated:
          - handle: GOP
            notes: "RNC"
        """)
        entries = _parse_curated_yaml(payload)
        self.assertEqual(len(entries), 2)
        by_handle = {e.handle: e for e in entries}
        self.assertEqual(by_handle["POTUS"].tier, "elected_official")
        self.assertEqual(by_handle["GOP"].tier, "affiliated")

    def test_mixed_rich_and_legacy_in_same_file(self):
        payload = yaml_lib.safe_load("""
        accounts:
          congress:
            senate:
              - name: Todd Young
                handle: '@SenToddYoung'
                chamber: Senate
                state_or_district: IN
                party: R
        affiliated:
          - handle: GOP
            notes: "Republican National Committee"
        """)
        entries = _parse_curated_yaml(payload)
        self.assertEqual(len(entries), 2)
        tiers = {e.handle: e.tier for e in entries}
        self.assertEqual(tiers["SenToddYoung"], "elected_official")
        self.assertEqual(tiers["GOP"], "affiliated")


class TestNarrativeAggregatorTierJoin(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        # Two X users: one elected, one unclassified.
        _seed_x_user(conn, user_id="u_elect", username="POTUS")
        _seed_x_user(conn, user_id="u_rand", username="someuser")
        _seed_x_post(conn, tweet_id="t1", author_id="u_elect", text="claim A", created_at=now)
        _seed_x_post(conn, tweet_id="t2", author_id="u_rand", text="claim B", created_at=now)
        _seed_doc_x(conn, doc_id=1, tweet_id="t1")
        _seed_doc_x(conn, doc_id=2, tweet_id="t2")
        _seed_narrative_with_doc(conn, narrative_id=1, doc_id=1, name="A")
        _seed_narrative_with_doc(conn, narrative_id=2, doc_id=2, name="B")
        # Classify u_elect as elected_official with full faction metadata;
        # leave u_rand absent.
        conn.execute(
            """
            INSERT INTO account_profiles
                (platform, author_id, display_name, tier, classification_method,
                 classified_at, full_name, party, branch, office_title, account_type)
            VALUES ('x', 'u_elect', 'POTUS', 'elected_official', 'curated_list', ?,
                    'Donald J. Trump', 'R', 'executive',
                    'President of the United States', 'official')
            """,
            (now,),
        )
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def test_aggregator_returns_tier_for_classified_x_author(self):
        agg = NarrativeAggregator(self.db_path)
        results = agg.get_top_narratives(time_window="all", limit=10)
        by_name = {r.name: r for r in results}
        self.assertEqual(by_name["A"].first_seen_tier, "elected_official")
        # Unclassified X author defaults to general_public (per aggregator rule).
        self.assertEqual(by_name["B"].first_seen_tier, "general_public")

    def test_aggregator_returns_author_faction_payload(self):
        agg = NarrativeAggregator(self.db_path)
        results = agg.get_top_narratives(time_window="all", limit=10)
        by_name = {r.name: r for r in results}
        # Classified X author carries faction metadata through to the payload.
        author = by_name["A"].first_seen_author
        self.assertIsNotNone(author)
        self.assertEqual(author["full_name"], "Donald J. Trump")
        self.assertEqual(author["party"], "R")
        self.assertEqual(author["branch"], "executive")
        self.assertEqual(author["office_title"], "President of the United States")
        self.assertEqual(author["account_type"], "official")
        self.assertEqual(author["handle"], "POTUS")
        # Unclassified X author still gets the sub-object (handle known from
        # x_users_raw) but faction fields are NULL.
        author_b = by_name["B"].first_seen_author
        self.assertIsNotNone(author_b)
        self.assertEqual(author_b["handle"], "someuser")
        self.assertIsNone(author_b["party"])
        self.assertIsNone(author_b["office_title"])

    def test_aggregator_tier_is_null_for_news(self):
        # Drop-in news doc + narrative to confirm tier stays None.
        conn = sqlite3.connect(self.db_path)
        now = int(time.time())
        conn.execute(
            """
            INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit,
                               published_at, text, raw_hash)
            VALUES (3, 'news', 'https://x.test/1', 'x.test', ?, 'body', 'rh-doc-3')
            """,
            (now,),
        )
        conn.execute(
            """
            INSERT INTO narratives (narrative_id, name, description, first_seen_at,
                                     first_seen_doc_id, created_at, updated_at)
            VALUES (3, 'C', 'C', ?, 3, ?, ?)
            """,
            (now, now, now),
        )
        conn.execute(
            """
            INSERT INTO narrative_docs (narrative_id, doc_id, discovered_at, confidence)
            VALUES (3, 3, ?, 0.9)
            """,
            (now,),
        )
        conn.commit()
        conn.close()

        agg = NarrativeAggregator(self.db_path)
        results = agg.get_top_narratives(time_window="all", limit=10)
        by_name = {r.name: r for r in results}
        self.assertIsNone(by_name["C"].first_seen_tier)
        # News first-seen docs carry no author-faction payload.
        self.assertIsNone(by_name["C"].first_seen_author)


# --------------------------------------------------------------------------- #
#  Phase 3b entity routing — walkthrough 058.                                 #
#  Verifies NarrativeAggregator attaches the registry-matched entity profile
#  to each narrative + flags cross-tier narratives when supporting docs span
#  multiple tier groups.
# --------------------------------------------------------------------------- #

class TestNarrativeEntityRouting(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        self.now = int(time.time())
        self.conn = sqlite3.connect(self.db_path)
        # @POTUS is in verified_officials.yaml — a registry-matched official.
        _seed_x_user(self.conn, user_id="u_potus", username="POTUS")
        # Unmatched author for the general-public tier.
        _seed_x_user(self.conn, user_id="u_rand", username="random_account")
        self.conn.commit()

    def tearDown(self):
        try:
            self.conn.close()
        except Exception:
            pass
        os.unlink(self.db_path)

    def _insert_news_doc(self, doc_id: int, domain: str, ident: Optional[str] = None) -> None:
        self.conn.execute(
            "INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit, "
            "published_at, text, raw_hash) VALUES (?, 'news', ?, ?, ?, 'body', ?)",
            (doc_id, ident or f"https://{domain}/a{doc_id}", domain, self.now, f"rh-{doc_id}"),
        )

    def _insert_reddit_doc(self, doc_id: int, subreddit: str) -> None:
        self.conn.execute(
            "INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit, "
            "published_at, text, raw_hash) VALUES (?, 'reddit_post', ?, ?, ?, 'body', ?)",
            (doc_id, f"t3_rp{doc_id}", subreddit, self.now, f"rh-{doc_id}"),
        )

    def _insert_narrative(self, narrative_id: int, name: str, first_seen_doc_id: int) -> None:
        self.conn.execute(
            "INSERT INTO narratives (narrative_id, name, description, first_seen_at, "
            "first_seen_doc_id, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?)",
            (narrative_id, name, name, self.now, first_seen_doc_id, self.now, self.now),
        )

    def _link_doc(self, narrative_id: int, doc_id: int) -> None:
        self.conn.execute(
            "INSERT INTO narrative_docs (narrative_id, doc_id, discovered_at, confidence) "
            "VALUES (?, ?, ?, 0.9)",
            (narrative_id, doc_id, self.now),
        )

    def test_first_seen_entity_profile_for_news(self):
        """News narrative first-seen at www.nytimes.com → NYT profile."""
        self._insert_news_doc(10, "www.nytimes.com")
        self._insert_narrative(10, "N-news", first_seen_doc_id=10)
        self._link_doc(10, 10)
        self.conn.commit()

        [narrative] = NarrativeAggregator(self.db_path).get_top_narratives(
            time_window="all", limit=10,
        )
        self.assertEqual(narrative.first_seen_tier_group, "news")
        self.assertIsNotNone(narrative.first_seen_entity_profile)
        self.assertEqual(narrative.first_seen_entity_profile["displayName"], "The New York Times")
        self.assertFalse(narrative.cross_tier)

    def test_first_seen_entity_profile_for_verified_official(self):
        """X post by @POTUS → officials tier + official profile from registry."""
        _seed_x_post(self.conn, tweet_id="t_potus", author_id="u_potus", text="claim", created_at=self.now)
        _seed_doc_x(self.conn, doc_id=20, tweet_id="t_potus")
        self._insert_narrative(20, "N-official", first_seen_doc_id=20)
        self._link_doc(20, 20)
        self.conn.commit()

        [narrative] = NarrativeAggregator(self.db_path).get_top_narratives(
            time_window="all", limit=10,
        )
        self.assertEqual(narrative.first_seen_tier_group, "officials")
        self.assertEqual(narrative.first_seen_entity_profile["kind"], "official")

    def test_first_seen_entity_profile_for_unmatched_x_author(self):
        """X post by unverified author → public tier + catch-all profile."""
        _seed_x_post(self.conn, tweet_id="t_rand", author_id="u_rand", text="claim", created_at=self.now)
        _seed_doc_x(self.conn, doc_id=30, tweet_id="t_rand")
        self._insert_narrative(30, "N-public-x", first_seen_doc_id=30)
        self._link_doc(30, 30)
        self.conn.commit()

        [narrative] = NarrativeAggregator(self.db_path).get_top_narratives(
            time_window="all", limit=10,
        )
        self.assertEqual(narrative.first_seen_tier_group, "public")
        self.assertEqual(narrative.first_seen_entity_profile["kind"], "catch_all")

    def test_cross_tier_flag_when_narrative_spans_tiers(self):
        """Supporting docs across news + X(official) + reddit → cross_tier True."""
        self._insert_news_doc(100, "www.nytimes.com")
        _seed_x_post(self.conn, tweet_id="t_cross", author_id="u_potus", text="claim", created_at=self.now)
        _seed_doc_x(self.conn, doc_id=101, tweet_id="t_cross")
        self._insert_reddit_doc(102, "politics")
        self._insert_narrative(40, "N-cross", first_seen_doc_id=100)
        self._link_doc(40, 100)
        self._link_doc(40, 101)
        self._link_doc(40, 102)
        self.conn.commit()

        [narrative] = NarrativeAggregator(self.db_path).get_top_narratives(
            time_window="all", limit=10,
        )
        self.assertTrue(narrative.cross_tier)

    def test_cross_tier_false_for_single_tier_narrative(self):
        """All supporting docs from one tier → cross_tier False."""
        self._insert_news_doc(200, "www.nytimes.com")
        self._insert_news_doc(201, "www.bbc.com")
        self._insert_narrative(50, "N-single", first_seen_doc_id=200)
        self._link_doc(50, 200)
        self._link_doc(50, 201)
        self.conn.commit()

        [narrative] = NarrativeAggregator(self.db_path).get_top_narratives(
            time_window="all", limit=10,
        )
        self.assertFalse(narrative.cross_tier)


if __name__ == "__main__":
    unittest.main()
