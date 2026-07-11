"""
Received-tone / expressed-tone aggregation tests.

The business rule under test: an official's headline number must be able to
separate how people talk ABOUT them (received) from how they themselves talk
(expressed). Concretely:

* target_sentiment rows fan out to (doc, target) pairs and land as
  ``received`` on the target official's EntitySentimentItem — including
  officials who never posted in the window (a card is created for them).
* Small-n suppression: any received net (overall or per-topic cell) computed
  from fewer than MIN_TARGET_SAMPLE_N pairs reports net=None + lowSample=True
  with an honest volume, never a +/-100.0 headline off one post.
* Alignment control: posts by a registry official about same-party vs
  cross-party tracked targets accumulate per-speaker cells + global
  baselines; self-references are excluded.
* Bot-flagged docs and per-target confidences below the aggregation floor
  never reach public numbers.
* Party collectives roll up under targetTone.collectives, not as cards.
* Account-level bot exclusion: pairs authored by an account whose
  author_bot_scores rollup is high are withheld and counted in metadata —
  an account farm must not be able to move an official's received tone.
* Engagement weighting re-emphasizes within an already-sufficient sample
  (reach proxy) but never rescues a suppressed one.
* bySpeakerTier / byNarrative attribute WHO talks about an official and
  WHICH recurring claims drive it, with the same per-cell suppression.
"""

import json
import math
import sqlite3
import sys
import time
import unittest
import uuid
from pathlib import Path

_repo_root = Path(__file__).resolve().parents[2]
if str(_repo_root) not in sys.path:
    sys.path.insert(0, str(_repo_root))

from analysis.src.etl.loader import ContentLoader
from analysis.src.reporting.aggregators import SentimentAggregator
from analysis.src.reporting.aggregators.sentiment import MIN_TARGET_SAMPLE_N
from analysis.src.reporting.entity_registry import (
    GOP_COLLECTIVE,
    TargetResolver,
    get_registry,
)


def _target_payload(*targets: dict) -> str:
    return json.dumps({"targets": list(targets), "reasoning": "test reasoning"})


def _t(target: str, stance: str, topic: str = "Immigration", confidence: float = 0.9) -> dict:
    return {
        "target": target, "topic": topic, "stance": stance,
        "confidence": confidence,
        "evidence_spans": ["a verbatim four word span"],
    }


# Shared minimal schema for target-tone fixtures (both test classes below).
_SCHEMA_SQL = """
            CREATE TABLE docs (
                doc_id INTEGER PRIMARY KEY,
                source_type TEXT NOT NULL,
                ident TEXT UNIQUE NOT NULL,
                domain_or_subreddit TEXT,
                published_at INTEGER,
                title TEXT,
                text TEXT,
                raw_hash TEXT NOT NULL,
                metadata_json TEXT DEFAULT '{}'
            );
            CREATE TABLE ai_outputs (
                output_id INTEGER PRIMARY KEY,
                doc_id INTEGER,
                task_type TEXT,
                output_json TEXT,
                confidence REAL,
                created_at INTEGER,
                label TEXT
            );
            CREATE VIEW ai_outputs_latest AS
            SELECT a.* FROM ai_outputs a
            WHERE a.output_id = (
                SELECT MAX(a2.output_id) FROM ai_outputs a2
                WHERE a2.doc_id = a.doc_id AND a2.task_type = a.task_type
            );

            CREATE TABLE target_mentions (
                mention_id INTEGER PRIMARY KEY AUTOINCREMENT,
                output_id INTEGER NOT NULL,
                doc_id INTEGER NOT NULL,
                raw_target TEXT NOT NULL,
                entity_key TEXT,
                entity_kind TEXT,
                entity_party TEXT,
                stance TEXT NOT NULL,
                topic TEXT,
                confidence REAL NOT NULL,
                evidence_json TEXT,
                created_at INTEGER NOT NULL
            );

            CREATE TABLE x_posts_raw (
                tweet_id TEXT PRIMARY KEY,
                author_id TEXT,
                is_official_tier INTEGER NOT NULL DEFAULT 0,
                retweet_count INTEGER DEFAULT 0,
                reply_count INTEGER DEFAULT 0,
                like_count INTEGER DEFAULT 0,
                quote_count INTEGER DEFAULT 0
            );
            CREATE TABLE x_users_raw (
                user_id TEXT PRIMARY KEY,
                username TEXT,
                name TEXT,
                profile_image_url TEXT,
                verified_type TEXT,
                followers_count INTEGER DEFAULT 0,
                created_at INTEGER,
                description TEXT
            );
            CREATE TABLE reddit_posts_raw (
                fullname TEXT PRIMARY KEY,
                score INTEGER,
                num_comments INTEGER
            );
            CREATE TABLE account_profiles (
                profile_id INTEGER PRIMARY KEY AUTOINCREMENT,
                platform TEXT NOT NULL,
                author_id TEXT NOT NULL,
                tier TEXT NOT NULL,
                full_name TEXT,
                party TEXT,
                office_title TEXT,
                account_type TEXT,
                UNIQUE(platform, author_id)
            );
            CREATE TABLE author_bot_scores (
                platform TEXT NOT NULL,
                author_id TEXT NOT NULL,
                score REAL NOT NULL,
                PRIMARY KEY (platform, author_id)
            );
            CREATE TABLE narratives (
                narrative_id INTEGER PRIMARY KEY,
                name TEXT
            );
            CREATE TABLE narrative_docs (
                narrative_id INTEGER,
                doc_id INTEGER
            );
"""


class TargetToneAggregationTests(unittest.TestCase):
    def setUp(self):
        self.db_path = f"test_target_tone_{uuid.uuid4()}.db"
        self.conn = sqlite3.connect(self.db_path)
        cur = self.conn.cursor()
        cur.executescript(_SCHEMA_SQL)

        ts = int(time.time()) - 3600  # inside every window

        docs = [
            # 6 news docs about Trump: 4 negative + 2 positive → net -33.3.
            # Topics: 5 Immigration (n>=5, shown), 1 Other (n=1, suppressed).
            (1, "news", "https://a.example/1", "a.example", ts, "t1", "body", "h1"),
            (2, "news", "https://a.example/2", "a.example", ts, "t2", "body", "h2"),
            (3, "news", "https://a.example/3", "a.example", ts, "t3", "body", "h3"),
            (4, "news", "https://a.example/4", "a.example", ts, "t4", "body", "h4"),
            (5, "news", "https://a.example/5", "a.example", ts, "t5", "body", "h5"),
            (6, "news", "https://a.example/6", "a.example", ts, "t6", "body", "h6"),
            # 2 docs about Thune (who never posts) → card created, suppressed.
            (7, "news", "https://a.example/7", "a.example", ts, "t7", "body", "h7"),
            (8, "news", "https://a.example/8", "a.example", ts, "t8", "body", "h8"),
            # X post authored by @SenSchumer → alignment cells.
            (9, "x_post", "tweet_schumer_1", "x.com", ts, None, "body", "h9"),
            # Unresolved + below-confidence-floor targets.
            (10, "news", "https://a.example/10", "a.example", ts, "t10", "body", "h10"),
            # X post by an account whose account-level bot rollup is high —
            # its targets must be withheld and counted in metadata.
            (11, "x_post", "tweet_farm_1", "x.com", ts, None, "body", "h11"),
            # X post by a curated affiliated (non-registry) account — counts
            # toward Trump's received tone under the 'affiliated' speaker
            # tier, with heavy engagement for the weighted-net test.
            (12, "x_post", "tweet_pundit_1", "x.com", ts, None, "body", "h12"),
            # Bot-flagged doc — its targets must not count.
            (99, "news", "https://a.example/99", "a.example", ts, "t99", "body", "h99"),
        ]
        cur.executemany(
            "INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit, "
            "published_at, title, text, raw_hash) VALUES (?,?,?,?,?,?,?,?)",
            docs,
        )
        cur.executemany(
            "INSERT INTO x_posts_raw (tweet_id, author_id, is_official_tier, like_count) "
            "VALUES (?,?,?,?)",
            [
                ("tweet_schumer_1", "1001", 1, 0),
                ("tweet_farm_1", "2002", 0, 0),
                ("tweet_pundit_1", "3003", 0, 99),
            ],
        )
        cur.executemany(
            "INSERT INTO x_users_raw (user_id, username) VALUES (?,?)",
            [("1001", "SenSchumer"), ("2002", "FarmAcct"), ("3003", "PunditJane")],
        )
        cur.execute(
            "INSERT INTO account_profiles (platform, author_id, tier, full_name, party) "
            "VALUES ('x', '3003', 'affiliated', 'Jane Pundit', 'D')"
        )
        cur.execute(
            "INSERT INTO author_bot_scores (platform, author_id, score) "
            "VALUES ('x', '2002', 0.9)"
        )
        cur.execute("INSERT INTO narratives (narrative_id, name) VALUES (1, 'Border claim')")
        cur.executemany(
            "INSERT INTO narrative_docs (narrative_id, doc_id) VALUES (1, ?)",
            [(1,), (2,), (3,)],
        )

        rows = [
            (1, _target_payload(_t("Donald J. Trump", "negative"))),
            (2, _target_payload(_t("Donald J. Trump", "negative"))),
            (3, _target_payload(_t("Donald J. Trump", "negative"))),
            (4, _target_payload(_t("Donald J. Trump", "negative"))),
            (5, _target_payload(_t("Donald J. Trump", "positive"))),
            (6, _target_payload(_t("Donald J. Trump", "positive", topic="Other"))),
            (7, _target_payload(_t("John Thune", "positive"))),
            (8, _target_payload(_t("John Thune", "positive"))),
            # Schumer's own post: cross-party x2, same-party x1, self x1
            # (self counts toward his received tone but not alignment).
            (9, _target_payload(
                _t("Donald J. Trump", "negative"),
                _t("Republican Party", "negative", topic="Healthcare"),
                _t("Hakeem Jeffries", "positive"),
                _t("Chuck Schumer", "positive"),
            )),
            (10, _target_payload(
                _t("Elon Musk", "negative"),                       # unresolved
                _t("Donald J. Trump", "negative", confidence=0.4),  # below floor
            )),
            # Account-level bot exclusion: three stances, none may count.
            (11, _target_payload(
                _t("Donald J. Trump", "positive"),
                _t("Donald J. Trump", "positive"),
                _t("John Thune", "positive"),
            )),
            (12, _target_payload(_t("Donald J. Trump", "negative"))),
            (99, _target_payload(_t("Donald J. Trump", "positive"))),
        ]
        cur.executemany(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, created_at) "
            f"VALUES (?, 'target_sentiment', ?, 0.9, {ts})",
            rows,
        )
        self.conn.commit()

        # Materialize target_mentions through the real write path (migration
        # 025): identity is resolved ONCE here, exactly as the pipeline does
        # at extraction time, and the aggregator below reads only the frozen
        # rows — never the raw JSON.
        ContentLoader(self.db_path).backfill_target_mentions(
            TargetResolver(get_registry()).resolve
        )

        self.result = SentimentAggregator(self.db_path).get_public_sentiment(
            time_window="7d", bot_docs={99},
        )
        self.by_official = {item.key: item for item in self.result.byOfficial}

    def tearDown(self):
        self.conn.close()
        Path(self.db_path).unlink(missing_ok=True)
        for suffix in ("-wal", "-shm"):
            Path(self.db_path + suffix).unlink(missing_ok=True)

    def test_received_tone_separates_about_from_by(self):
        """Trump's received tone reflects the non-bot, above-floor posts
        ABOUT him, not anyone's own posts."""
        potus = self.by_official["potus"]
        self.assertIsNotNone(potus.received)
        # docs 1-6 + Schumer + pundit = 8; doc 99 (bot-flagged), doc 10
        # (below floor), and doc 11 (bot-scored account) excluded.
        self.assertEqual(potus.received["volume"], 8)
        # 2 positive, 6 negative → (2-6)/8*100 = -50.0
        self.assertAlmostEqual(potus.received["net"], -50.0, places=1)
        self.assertFalse(potus.received["lowSample"])

    def test_topic_cells_suppressed_below_threshold(self):
        potus = self.by_official["potus"]
        cells = {c["topic"]: c for c in potus.received["byTopic"]}
        # Immigration: docs 1-5 + Schumer = 6 pairs → shown.
        self.assertGreaterEqual(cells["Immigration"]["volume"], MIN_TARGET_SAMPLE_N)
        self.assertIsNotNone(cells["Immigration"]["net"])
        self.assertFalse(cells["Immigration"]["lowSample"])
        # Other: 1 pair → volume shown, net suppressed.
        self.assertEqual(cells["Other"]["volume"], 1)
        self.assertIsNone(cells["Other"]["net"])
        self.assertTrue(cells["Other"]["lowSample"])

    def test_small_n_received_never_shows_a_headline_number(self):
        """Thune: 2 positive mentions must NOT render +100.0 — the exact
        failure this feature exists to prevent."""
        thune = self.by_official.get("leaderjohnthune")
        self.assertIsNotNone(thune, "target-only official should get a card")
        self.assertEqual(thune.volume, 0)  # never posted (expressed side empty)
        self.assertEqual(thune.received["volume"], 2)
        self.assertIsNone(thune.received["net"])
        self.assertTrue(thune.received["lowSample"])

    def test_alignment_cells_exclude_self_and_split_by_party(self):
        schumer = self.by_official["senschumer"]
        align = schumer.expressed_alignment
        self.assertIsNotNone(align)
        # Trump + GOP = cross-party; Jeffries = same-party; self excluded.
        self.assertEqual(align["crossPartyVolume"], 2)
        self.assertEqual(align["samePartyVolume"], 1)
        # Both cells below MIN_TARGET_SAMPLE_N → suppressed.
        self.assertIsNone(align["crossPartyNet"])
        self.assertIsNone(align["samePartyNet"])

    def test_self_reference_counts_toward_received(self):
        schumer = self.by_official["senschumer"]
        self.assertEqual(schumer.received["volume"], 1)

    def test_collectives_roll_up_in_target_tone_not_as_cards(self):
        tone = self.result.targetTone
        self.assertIsNotNone(tone)
        self.assertIn(GOP_COLLECTIVE, tone["collectives"])
        self.assertEqual(tone["collectives"][GOP_COLLECTIVE]["volume"], 1)
        self.assertNotIn(GOP_COLLECTIVE, self.by_official)

    def test_target_tone_metadata(self):
        tone = self.result.targetTone
        self.assertEqual(tone["minSampleN"], MIN_TARGET_SAMPLE_N)
        self.assertEqual(tone["unresolvedMentions"], 1)   # Elon Musk
        # 8 Trump + 2 Thune + GOP + Jeffries + Schumer-self = 13
        self.assertEqual(tone["resolvedMentions"], 13)
        self.assertEqual(tone["baselines"]["crossPartyVolume"], 2)
        self.assertEqual(tone["baselines"]["samePartyVolume"], 1)

    def test_bot_scored_account_mentions_withheld_and_counted(self):
        """An account farm (high author_bot_scores rollup) must not move
        received tone — its 3 stances are withheld, visibly, in metadata."""
        self.assertEqual(self.result.targetTone["botExcludedMentions"], 3)
        # Thune still shows only the 2 legitimate mentions.
        thune = self.by_official["leaderjohnthune"]
        self.assertEqual(thune.received["volume"], 2)

    def test_engagement_weighting_reemphasizes_but_never_rescues(self):
        """The pundit's viral (99-like) attack pulls the weighted net below
        the unweighted one; a suppressed sample stays suppressed."""
        potus = self.by_official["potus"]
        w_viral = 1 + math.log1p(99)
        w_total = 7 + w_viral
        expected = round((2 - (5 + w_viral)) / w_total * 100, 1)
        self.assertAlmostEqual(potus.received["engagementWeightedNet"], expected, places=1)
        self.assertLess(potus.received["engagementWeightedNet"], potus.received["net"])
        # Thune is below the sample floor: no weighted headline either.
        thune = self.by_official["leaderjohnthune"]
        self.assertIsNone(thune.received["engagementWeightedNet"])

    def test_speaker_tier_split(self):
        """WHO talks about Trump: 6 news pairs (shown), 1 official + 1
        affiliated (each suppressed but honestly counted)."""
        potus = self.by_official["potus"]
        tiers = {c["tier"]: c for c in potus.received["bySpeakerTier"]}
        self.assertEqual(tiers["news"]["volume"], 6)
        self.assertIsNotNone(tiers["news"]["net"])
        self.assertEqual(tiers["officials"]["volume"], 1)
        self.assertTrue(tiers["officials"]["lowSample"])
        self.assertEqual(tiers["affiliated"]["volume"], 1)
        self.assertTrue(tiers["affiliated"]["lowSample"])

    def test_narrative_attribution(self):
        """Mentions clustered into a narrative surface WHICH claim drives
        the tone; per-narrative cells share the suppression floor."""
        potus = self.by_official["potus"]
        narratives = {c["narrativeId"]: c for c in potus.received["byNarrative"]}
        self.assertIn(1, narratives)
        self.assertEqual(narratives[1]["name"], "Border claim")
        self.assertEqual(narratives[1]["volume"], 3)   # docs 1-3
        self.assertIsNone(narratives[1]["net"])        # 3 < floor
        self.assertTrue(narratives[1]["lowSample"])

    def test_serialization_carries_received_and_target_tone(self):
        data = self.result.to_dict()
        self.assertIn("targetTone", data)
        potus_dict = next(e for e in data["byOfficial"] if e["key"] == "potus")
        self.assertIn("received", potus_dict)
        self.assertIn("byTopic", potus_dict["received"])
        # Samples must be JSON-serializable plain dicts with evidence.
        self.assertTrue(json.dumps(data))
        self.assertTrue(potus_dict["received"]["samples"])
        self.assertIn("evidence_spans", potus_dict["received"]["samples"][0])


class OutboundTargetsTests(unittest.TestCase):
    """Outbound targets: WHO a public bucket's posts talk about.

    The business rule: the Public column's cards (named sampled authors and
    the catch-alls) must be able to answer "who is this sentiment directed
    at" from the same frozen target_mentions rows that power received tone
    — attributed to the AUTHORING bucket, with the same suppression floor,
    and without ever minting an identity from a one-off free-text target.
    """

    def setUp(self):
        self.db_path = f"test_outbound_targets_{uuid.uuid4()}.db"
        self.conn = sqlite3.connect(self.db_path)
        cur = self.conn.cursor()
        cur.executescript(_SCHEMA_SQL)

        ts = int(time.time()) - 3600

        # BigVoice: 4 posts + 50k followers → clears both sampled-author
        # floors and keeps a named card. SmallFry: 1 post, 12 followers →
        # consolidated into "Other X users".
        docs = [
            (21, "x_post", "tw_big_1", "x.com", ts, None, "post body", "h21"),
            (22, "x_post", "tw_big_2", "x.com", ts, None, "post body", "h22"),
            (23, "x_post", "tw_big_3", "x.com", ts, None, "post body", "h23"),
            (24, "x_post", "tw_big_4", "x.com", ts, None, "post body", "h24"),
            (25, "x_post", "tw_small_1", "x.com", ts, None, "post body", "h25"),
        ]
        cur.executemany(
            "INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit, "
            "published_at, title, text, raw_hash) VALUES (?,?,?,?,?,?,?,?)",
            docs,
        )
        cur.executemany(
            "INSERT INTO x_posts_raw (tweet_id, author_id, is_official_tier) VALUES (?,?,0)",
            [("tw_big_1", "5001"), ("tw_big_2", "5001"), ("tw_big_3", "5001"),
             ("tw_big_4", "5001"), ("tw_small_1", "5002")],
        )
        cur.executemany(
            "INSERT INTO x_users_raw (user_id, username, name, followers_count) "
            "VALUES (?,?,?,?)",
            [("5001", "BigVoice", "Big Voice", 50000),
             ("5002", "SmallFry", "Small Fry", 12)],
        )
        # Sentiment rows create the public-tier buckets the rollup lands on.
        cur.executemany(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, created_at) "
            f"VALUES (?, 'sentiment', ?, 0.9, {ts})",
            [(d, json.dumps({"label": "NEGATIVE", "confidence": 0.9,
                             "reasoning": "test reasoning"}))
             for d in (21, 22, 23, 24, 25)],
        )
        cur.executemany(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, created_at) "
            f"VALUES (?, 'target_sentiment', ?, 0.9, {ts})",
            [
                # BigVoice → Trump: 4 negative + 1 positive = net -60.0 over
                # 5 pairs (clears the floor).
                (21, _target_payload(
                    _t("Donald J. Trump", "negative"),
                    _t("Donald J. Trump", "negative"),
                    _t("The Media", "negative"),          # unresolved, recurs
                )),
                (22, _target_payload(
                    _t("Donald J. Trump", "negative"),
                    _t("Donald J. Trump", "negative"),
                )),
                (23, _target_payload(_t("Donald J. Trump", "positive"))),
                (24, _target_payload(
                    _t("The Media", "negative"),
                    _t("Some Random Blogger", "negative"),  # one-off raw
                )),
                # SmallFry (consolidated) → attributed to Other X users.
                (25, _target_payload(_t("John Thune", "positive"))),
            ],
        )
        self.conn.commit()

        ContentLoader(self.db_path).backfill_target_mentions(
            TargetResolver(get_registry()).resolve
        )
        self.result = SentimentAggregator(self.db_path).get_public_sentiment(
            time_window="7d", bot_docs=set(),
        )
        self.by_public = {item.key: item for item in self.result.byGeneralPublic}

    def tearDown(self):
        self.conn.close()
        Path(self.db_path).unlink(missing_ok=True)
        for suffix in ("-wal", "-shm"):
            Path(self.db_path + suffix).unlink(missing_ok=True)

    def test_named_author_gets_outbound_rollup(self):
        big = self.by_public["bigvoice"]
        self.assertIsNotNone(big.outbound)
        trump = big.outbound["targets"][0]
        expected_label = get_registry().officials["potus"].profile_dict()["displayName"]
        self.assertEqual(trump["label"], expected_label)
        self.assertEqual(trump["entityKey"], "potus")
        self.assertEqual(trump["kind"], "official")
        self.assertEqual(trump["volume"], 5)
        self.assertAlmostEqual(trump["net"], -60.0, places=1)
        self.assertFalse(trump["lowSample"])

    def test_recurring_raw_target_named_but_suppressed(self):
        """'The Media' recurs (2 pairs) → named row; net stays suppressed
        below the floor — a free-text target never gets a headline number
        off thin evidence."""
        big = self.by_public["bigvoice"]
        media = next(t for t in big.outbound["targets"] if t["label"] == "The Media")
        self.assertEqual(media["kind"], "raw")
        self.assertEqual(media["volume"], 2)
        self.assertIsNone(media["net"])
        self.assertTrue(media["lowSample"])

    def test_oneoff_raw_target_folds_into_other(self):
        """A single free-text mention must not mint an identity row."""
        big = self.by_public["bigvoice"]
        labels = [t["label"] for t in big.outbound["targets"]]
        self.assertNotIn("Some Random Blogger", labels)
        other = next(t for t in big.outbound["targets"] if t["kind"] == "other")
        self.assertEqual(other["volume"], 1)

    def test_consolidated_author_attributes_to_catch_all(self):
        """SmallFry's bucket folded into Other X users, so their mention
        lands on the card the reader actually sees."""
        catch = self.by_public["other-x-users"]
        self.assertIsNotNone(catch.outbound)
        thune = catch.outbound["targets"][0]
        self.assertEqual(thune["entityKey"], "leaderjohnthune")
        self.assertEqual(thune["volume"], 1)
        self.assertTrue(thune["lowSample"])

    def test_samples_carry_target_chips(self):
        """Each sampled post says WHO it's about: resolved targets first,
        deduped by label, capped at two chips."""
        big = self.by_public["bigvoice"]
        sample = next(s for s in big.classification_samples if s.doc_id == 21)
        self.assertIsNotNone(sample.targets)
        expected_label = get_registry().officials["potus"].profile_dict()["displayName"]
        self.assertEqual(sample.targets[0]["label"], expected_label)
        self.assertEqual(sample.targets[0]["stance"], "negative")
        self.assertEqual(sample.targets[1]["label"], "The Media")
        self.assertEqual(len(sample.targets), 2)  # 2 Trump pairs dedupe to 1 chip

    def test_outbound_serializes_only_when_present(self):
        data = self.result.to_dict()
        big_dict = next(e for e in data["byGeneralPublic"] if e["key"] == "bigvoice")
        self.assertIn("outbound", big_dict)
        self.assertEqual(big_dict["outbound"]["minSampleN"], MIN_TARGET_SAMPLE_N)
        self.assertTrue(json.dumps(data))
        # Officials' cards carry received, never outbound (public-tier only).
        for e in data["byOfficial"]:
            self.assertNotIn("outbound", e)


if __name__ == "__main__":
    unittest.main()
