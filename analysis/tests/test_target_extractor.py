"""
Target-sentiment extractor + resolver tests.

Covers the two halves of the received-tone pipeline's input:

1. TargetSentimentExtractor — LLM-response validation. The invariant under
   test is B2 traceability: a stance the model can't anchor to verbatim text
   must not enter public aggregates at full confidence, and a transport
   failure must be distinguishable from "no targets" so docs re-queue.
2. TargetResolver — deterministic resolution of raw target names against the
   editorial officials registry + party collectives. Resolution lives in
   code, not the prompt, so registry edits retroactively resolve stored rows.

Also pins the topic enum in llm/schemas.py to the aggregator's
TOPIC_KEYWORDS taxonomy — if they drift, per-topic received cells would
fragment into buckets the UI's topic tabs can't display.
"""

import sys
import unittest
from pathlib import Path

_repo_root = Path(__file__).resolve().parents[2]
if str(_repo_root) not in sys.path:
    sys.path.insert(0, str(_repo_root))

from analysis.src.engine.target_extractor import (
    TargetSentimentExtractor,
    UNVERIFIED_EVIDENCE_CONFIDENCE_CAP,
    build_tracked_targets_block,
)
from analysis.src.llm.schemas import TARGET_TOPIC_ENUM
from analysis.src.reporting.aggregators.constants import TOPIC_KEYWORDS
from analysis.src.reporting.entity_registry import (
    DEM_COLLECTIVE,
    GOP_COLLECTIVE,
    TargetResolver,
    get_registry,
    normalize_target_name,
)


class FakeLLMClient:
    """Minimal BaseLLMClient stand-in returning a canned response."""

    def __init__(self, response=None, raise_error=False):
        self.response = response or {"targets": []}
        self.raise_error = raise_error
        self.is_available = True
        self.calls = []

    def complete(self, system_prompt, user_prompt, response_schema=None):
        self.calls.append(user_prompt)
        if self.raise_error:
            raise RuntimeError("transport failure")
        return self.response


SOURCE_TEXT = (
    "Republicans ended the ACA tax credits and millions will pay more. "
    "Chuck Schumer promised to fight the repeal in the Senate."
)


class TestTopicEnumSync(unittest.TestCase):
    def test_topic_enum_matches_aggregator_taxonomy(self):
        """The LLM topic enum and the aggregator's TOPIC_KEYWORDS must stay
        in sync (plus 'Other') or per-topic received-tone cells fragment
        into topics the UI tabs can't render."""
        self.assertEqual(
            set(TARGET_TOPIC_ENUM),
            set(TOPIC_KEYWORDS.keys()) | {"Other"},
        )


class TestTargetValidation(unittest.TestCase):
    def _extract(self, targets, text=SOURCE_TEXT):
        client = FakeLLMClient({"targets": targets, "reasoning": "test"})
        extractor = TargetSentimentExtractor(llm_client=client)
        return extractor.extract(text)

    def test_valid_target_kept_with_verbatim_evidence(self):
        result = self._extract([{
            "target": "Republican Party", "topic": "Healthcare",
            "stance": "negative", "confidence": 0.9,
            "evidence_spans": ["Republicans ended the ACA tax credits"],
        }])
        self.assertFalse(result.extraction_failed)
        self.assertEqual(len(result.targets), 1)
        t = result.targets[0]
        self.assertEqual(t.stance, "negative")
        self.assertEqual(t.confidence, 0.9)
        self.assertEqual(t.evidence_spans, ["Republicans ended the ACA tax credits"])

    def test_fabricated_evidence_caps_confidence(self):
        """A stance whose evidence isn't verbatim from the source must fall
        below the aggregation confidence floor — it stays auditable but
        can't move a public number."""
        result = self._extract([{
            "target": "Chuck Schumer", "topic": "Healthcare",
            "stance": "positive", "confidence": 0.95,
            "evidence_spans": ["Schumer heroically saved the entire healthcare system"],
        }])
        self.assertEqual(len(result.targets), 1)
        t = result.targets[0]
        self.assertEqual(t.evidence_spans, [])
        self.assertLessEqual(t.confidence, UNVERIFIED_EVIDENCE_CONFIDENCE_CAP)

    def test_invalid_stance_dropped(self):
        result = self._extract([{
            "target": "Chuck Schumer", "topic": "Healthcare",
            "stance": "hostile", "confidence": 0.9,
        }])
        self.assertEqual(result.targets, [])

    def test_unknown_topic_falls_back_to_other(self):
        result = self._extract([{
            "target": "Chuck Schumer", "topic": "Sportsball",
            "stance": "positive", "confidence": 0.8,
            "evidence_spans": ["promised to fight the repeal"],
        }])
        self.assertEqual(result.targets[0].topic, "Other")

    def test_confidence_clamped_to_unit_interval(self):
        result = self._extract([{
            "target": "Chuck Schumer", "topic": "Healthcare",
            "stance": "positive", "confidence": 85,
            "evidence_spans": ["promised to fight the repeal"],
        }])
        self.assertEqual(result.targets[0].confidence, 1.0)

    def test_duplicate_targets_deduped(self):
        result = self._extract([
            {"target": "Chuck Schumer", "topic": "Healthcare",
             "stance": "positive", "confidence": 0.9,
             "evidence_spans": ["promised to fight the repeal"]},
            {"target": "chuck schumer", "topic": "Healthcare",
             "stance": "negative", "confidence": 0.6,
             "evidence_spans": ["promised to fight the repeal"]},
        ])
        self.assertEqual(len(result.targets), 1)
        self.assertEqual(result.targets[0].stance, "positive")

    def test_trivial_content_skips_llm_call(self):
        client = FakeLLMClient()
        extractor = TargetSentimentExtractor(llm_client=client)
        result = extractor.extract("@someone https://t.co/abc #maga")
        self.assertFalse(result.extraction_failed)
        self.assertEqual(result.targets, [])
        self.assertEqual(client.calls, [])

    def test_transport_failure_flags_extraction_failed(self):
        """A failed LLM call is NOT 'no targets' — job_runner must see the
        flag and skip persisting so the doc re-queues (audit A-3)."""
        client = FakeLLMClient(raise_error=True)
        extractor = TargetSentimentExtractor(llm_client=client)
        result = extractor.extract(SOURCE_TEXT)
        self.assertTrue(result.extraction_failed)

    def test_disabled_extractor_flags_extraction_failed(self):
        extractor = TargetSentimentExtractor(llm_enabled=False)
        result = extractor.extract(SOURCE_TEXT)
        self.assertTrue(result.extraction_failed)

    def test_tracked_targets_block_prepended_to_prompt(self):
        client = FakeLLMClient({"targets": []})
        extractor = TargetSentimentExtractor(
            llm_client=client, tracked_targets=["Donald J. Trump", "Republican Party"],
        )
        extractor.extract(SOURCE_TEXT)
        self.assertIn("TRACKED TARGETS", client.calls[0])
        self.assertIn("- Donald J. Trump", client.calls[0])

    def test_empty_tracked_targets_produces_no_block(self):
        self.assertEqual(build_tracked_targets_block([]), "")


class TestTargetResolver(unittest.TestCase):
    """Resolution against the real editorial registry (data/*.yaml)."""

    @classmethod
    def setUpClass(cls):
        cls.resolver = TargetResolver(get_registry())

    def test_display_name_resolves_to_official(self):
        key, kind, party = self.resolver.resolve("Donald J. Trump")
        self.assertEqual((key, kind, party), ("potus", "official", "R"))

    def test_handle_and_alias_handle_resolve(self):
        self.assertEqual(self.resolver.resolve("@POTUS")[0], "potus")
        self.assertEqual(self.resolver.resolve("realDonaldTrump")[0], "potus")

    def test_bare_last_name_resolves_when_unambiguous(self):
        key, kind, party = self.resolver.resolve("Schumer")
        self.assertEqual(kind, "official")
        self.assertEqual(party, "D")

    def test_title_prefix_stripped(self):
        self.assertEqual(self.resolver.resolve("President Trump")[0], "potus")

    def test_name_suffix_ignored_for_last_name(self):
        key, kind, _ = self.resolver.resolve("Kennedy")
        self.assertEqual(kind, "official")

    def test_collectives_resolve_with_party(self):
        self.assertEqual(
            self.resolver.resolve("the GOP"), (GOP_COLLECTIVE, "collective", "R"),
        )
        self.assertEqual(
            self.resolver.resolve("Republicans"), (GOP_COLLECTIVE, "collective", "R"),
        )
        self.assertEqual(
            self.resolver.resolve("Democratic Party"), (DEM_COLLECTIVE, "collective", "D"),
        )

    def test_unknown_target_unresolved(self):
        self.assertEqual(self.resolver.resolve("Elon Musk"), (None, None, None))

    def test_normalize_strips_possessive_and_article(self):
        self.assertEqual(normalize_target_name("The GOP's"), "gop")


if __name__ == "__main__":
    unittest.main()
