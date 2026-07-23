"""
Unit tests for the unified evidence-span validator
(analysis.src.engine.validation), consolidated from analyzer.py,
claim_extractor.py, propaganda_detector.py, and target_extractor.py's
per-engine validators (Postgres redesign Phase 5).

Includes a compatibility-matrix test asserting the unified functions agree
with the majority (3-of-4) behavior on a shared fixture set, with expected
outcomes encoded explicitly rather than by importing the old validators —
those validators still live in the old-stack engines and are out of scope
for this module.
"""

import sys
import unittest
from pathlib import Path

_repo_root = Path(__file__).resolve().parents[2]
if str(_repo_root) not in sys.path:
    sys.path.insert(0, str(_repo_root))

from analysis.src.engine.constants import (
    MAX_CLAIM_WORDS,
    MIN_CLAIM_WORDS,
    MIN_EVIDENCE_WORDS,
    UNVERIFIED_EVIDENCE_CONFIDENCE_CAP,
)
from analysis.src.engine.validation import (
    cap_confidence_if_unverified,
    validate_claim_length,
    validate_evidence_span,
    validate_spans,
)


class ValidateEvidenceSpanTests(unittest.TestCase):
    def setUp(self):
        self.source = "The senator praised the new bipartisan infrastructure bill today."

    def test_verbatim_match(self):
        self.assertTrue(validate_evidence_span("praised the new bipartisan", self.source))

    def test_no_match(self):
        self.assertFalse(validate_evidence_span("opposed the terrible bill today", self.source))

    def test_case_insensitive_match(self):
        self.assertTrue(validate_evidence_span("PRAISED THE NEW BIPARTISAN", self.source))

    def test_word_floor_below_minimum_rejected(self):
        # 3 words, verbatim substring, but under MIN_EVIDENCE_WORDS = 4.
        self.assertEqual(MIN_EVIDENCE_WORDS, 4)
        self.assertFalse(validate_evidence_span("praised the new", self.source))

    def test_word_floor_at_minimum_accepted(self):
        # Exactly 4 words, verbatim substring.
        self.assertTrue(validate_evidence_span("praised the new bipartisan", self.source))

    def test_empty_span_invalid(self):
        self.assertFalse(validate_evidence_span("", self.source))

    def test_whitespace_only_span_invalid(self):
        self.assertFalse(validate_evidence_span("   ", self.source))

    def test_non_string_span_invalid(self):
        self.assertFalse(validate_evidence_span(None, self.source))
        self.assertFalse(validate_evidence_span(42, self.source))

    def test_span_is_stripped_before_word_count(self):
        self.assertTrue(validate_evidence_span("  praised the new bipartisan  ", self.source))

    def test_paraphrase_not_verbatim_rejected(self):
        self.assertFalse(
            validate_evidence_span("commended the fresh bipartisan measure", self.source)
        )


class ValidateSpansTests(unittest.TestCase):
    def setUp(self):
        self.source = "The senator praised the new bipartisan infrastructure bill today."

    def test_all_valid(self):
        spans = ["praised the new bipartisan", "bipartisan infrastructure bill today"]
        valid, had_invalid = validate_spans(spans, self.source)
        self.assertEqual(valid, spans)
        self.assertFalse(had_invalid)

    def test_all_invalid_flags_had_invalid(self):
        spans = ["completely fabricated evidence here", "another made up quote entirely"]
        valid, had_invalid = validate_spans(spans, self.source)
        self.assertEqual(valid, [])
        self.assertTrue(had_invalid)

    def test_mixed_valid_and_invalid_does_not_flag(self):
        # One real span survives, so had_invalid is False even though one
        # input entry was fabricated — matches the combined condition both
        # original call sites actually used (cap only when nothing survives).
        spans = ["praised the new bipartisan", "fabricated nonsense phrase here"]
        valid, had_invalid = validate_spans(spans, self.source)
        self.assertEqual(valid, ["praised the new bipartisan"])
        self.assertFalse(had_invalid)

    def test_empty_input_list_not_flagged_invalid(self):
        valid, had_invalid = validate_spans([], self.source)
        self.assertEqual(valid, [])
        self.assertFalse(had_invalid)

    def test_non_string_entries_skipped_and_can_still_flag_invalid(self):
        valid, had_invalid = validate_spans([None, 42, "   "], self.source)
        self.assertEqual(valid, [])
        self.assertTrue(had_invalid)

    def test_no_deduplication(self):
        # Deliberate choice: validate_spans does not dedupe (analyzer.py's
        # original behavior); dedup is left to callers.
        spans = ["praised the new bipartisan", "praised the new bipartisan"]
        valid, had_invalid = validate_spans(spans, self.source)
        self.assertEqual(valid, spans)
        self.assertFalse(had_invalid)


class CapConfidenceIfUnverifiedTests(unittest.TestCase):
    def test_verified_high_confidence_uncapped(self):
        self.assertEqual(cap_confidence_if_unverified(0.9, verified=True), 0.9)

    def test_unverified_high_confidence_capped(self):
        self.assertEqual(
            cap_confidence_if_unverified(0.9, verified=False),
            UNVERIFIED_EVIDENCE_CONFIDENCE_CAP,
        )

    def test_unverified_low_confidence_not_raised(self):
        # min(confidence, cap) must not raise a confidence already below
        # the cap.
        self.assertEqual(cap_confidence_if_unverified(0.1, verified=False), 0.1)

    def test_clamp_above_one(self):
        self.assertEqual(cap_confidence_if_unverified(1.5, verified=True), 1.0)

    def test_clamp_below_zero(self):
        self.assertEqual(cap_confidence_if_unverified(-0.5, verified=True), 0.0)

    def test_clamp_applied_before_cap(self):
        # Out-of-range input, unverified: clamp to 1.0 first, then cap.
        self.assertEqual(
            cap_confidence_if_unverified(2.0, verified=False),
            UNVERIFIED_EVIDENCE_CONFIDENCE_CAP,
        )

    def test_cap_constant_value(self):
        self.assertEqual(UNVERIFIED_EVIDENCE_CONFIDENCE_CAP, 0.3)


class ValidateClaimLengthTests(unittest.TestCase):
    def test_below_minimum_rejected(self):
        self.assertEqual(MIN_CLAIM_WORDS, 4)
        self.assertFalse(validate_claim_length("only three words"))

    def test_at_minimum_accepted(self):
        self.assertTrue(validate_claim_length("exactly four words here"))

    def test_at_maximum_accepted(self):
        self.assertEqual(MAX_CLAIM_WORDS, 20)
        claim = " ".join(["word"] * 20)
        self.assertTrue(validate_claim_length(claim))

    def test_above_maximum_rejected(self):
        claim = " ".join(["word"] * 21)
        self.assertFalse(validate_claim_length(claim))

    def test_empty_claim_rejected(self):
        self.assertFalse(validate_claim_length(""))


class CompatibilityMatrixTests(unittest.TestCase):
    """Cross-checks the unified functions against explicit, hand-encoded
    expected outcomes for the majority (3-of-4) behavior each constant and
    rule represents. Fixtures are self-contained; the old validators are
    never imported."""

    def setUp(self):
        self.source = (
            "The governor announced a historic bipartisan deal on infrastructure "
            "funding after months of negotiation with both parties."
        )

    def test_evidence_word_floor_matches_majority_four(self):
        # analyzer/propaganda/target all required >= 4 words; claims'
        # original 3-word floor is the outlier now unified to 4.
        three_word_span = "historic bipartisan deal"  # verbatim, 3 words
        four_word_span = "historic bipartisan deal on"  # verbatim, 4 words
        self.assertFalse(validate_evidence_span(three_word_span, self.source))
        self.assertTrue(validate_evidence_span(four_word_span, self.source))

    def test_confidence_cap_matches_majority_point_three(self):
        # analyzer/target used 0.3; propaganda's 0.2 is the outlier now
        # unified to 0.3 (looser).
        capped = cap_confidence_if_unverified(0.95, verified=False)
        self.assertEqual(capped, 0.3)
        self.assertNotEqual(capped, 0.2)

    def test_fabricated_single_span_capped_like_analyzer_and_target(self):
        # Mirrors analyzer._validate_evidence_spans / target_extractor.
        # _validate_target: one fabricated span, none verified -> capped.
        spans = ["completely invented quote not in source"]
        valid, had_invalid = validate_spans(spans, self.source)
        conf = cap_confidence_if_unverified(0.8, verified=not had_invalid)
        self.assertEqual(valid, [])
        self.assertEqual(conf, 0.3)

    def test_verified_span_not_capped(self):
        spans = ["historic bipartisan deal on infrastructure"]
        valid, had_invalid = validate_spans(spans, self.source)
        conf = cap_confidence_if_unverified(0.8, verified=not had_invalid)
        self.assertEqual(valid, spans)
        self.assertEqual(conf, 0.8)

    def test_claim_bound_matrix(self):
        cases = {
            "two words": False,
            "three word claim": False,
            "four word claim text": True,
            " ".join(["word"] * 20): True,
            " ".join(["word"] * 21): False,
        }
        for claim_text, expected in cases.items():
            with self.subTest(claim_text=claim_text[:30]):
                self.assertEqual(validate_claim_length(claim_text), expected)


if __name__ == "__main__":
    unittest.main()
