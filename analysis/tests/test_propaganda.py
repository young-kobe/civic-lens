"""
Tests for the propaganda-detection backend (walkthrough 042).

Covers:
  - _validate_technique accepts legitimate flags, rejects fabricated evidence,
    short spans, unknown technique names, and bad confidence values.
  - PropagandaDetector with a mocked LLM client:
      * validated techniques flow through.
      * fabricated-evidence techniques are dropped.
      * empty techniques array → overall_propaganda_score = 0.0.
      * LLM returned techniques but all invalid → score capped at 0.2.
  - to_dict / model serialization is stable.
"""

import os
import sys
import unittest
from unittest.mock import patch

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine.propaganda_detector import (
    PropagandaDetector,
    _validate_technique,
    UNVERIFIED_EVIDENCE_CAP,
)
from analysis.src.engine.models import PropagandaResult, PropagandaTechnique


SOURCE_TEXT = (
    "The radical extremists want to destroy our values. "
    "Only a corrupt career politician would side with them. "
    "Many people are saying the election was stolen."
)


class _FakeLLMClient:
    """Minimal stand-in for the LLM client used by PropagandaDetector."""

    is_available = True

    def __init__(self, response: dict):
        self._response = response
        self.calls = 0

    def complete(self, system_prompt, user_prompt, response_schema=None):
        self.calls += 1
        return self._response


class TestValidateTechnique(unittest.TestCase):
    def test_accepts_verbatim_substring_evidence(self):
        raw = {
            "technique": "loaded_language",
            "confidence": 0.8,
            "evidence_span": "radical extremists want to destroy",
        }
        result = _validate_technique(raw, SOURCE_TEXT)
        self.assertIsNotNone(result)
        self.assertEqual(result.technique, "loaded_language")
        self.assertAlmostEqual(result.confidence, 0.8)

    def test_rejects_fabricated_evidence(self):
        raw = {
            "technique": "loaded_language",
            "confidence": 0.8,
            "evidence_span": "these words do not appear in source",
        }
        self.assertIsNone(_validate_technique(raw, SOURCE_TEXT))

    def test_rejects_short_evidence(self):
        raw = {
            "technique": "ad_hominem",
            "confidence": 0.7,
            "evidence_span": "corrupt politician",  # 2 words
        }
        self.assertIsNone(_validate_technique(raw, SOURCE_TEXT))

    def test_rejects_unknown_technique(self):
        raw = {
            "technique": "vibes_based",
            "confidence": 0.7,
            "evidence_span": "radical extremists want to destroy",
        }
        self.assertIsNone(_validate_technique(raw, SOURCE_TEXT))

    def test_rejects_bad_confidence(self):
        raw = {
            "technique": "loaded_language",
            "confidence": "not a number",
            "evidence_span": "radical extremists want to destroy",
        }
        self.assertIsNone(_validate_technique(raw, SOURCE_TEXT))

    def test_clamps_confidence_into_unit_interval(self):
        raw = {
            "technique": "loaded_language",
            "confidence": 1.7,
            "evidence_span": "radical extremists want to destroy",
        }
        result = _validate_technique(raw, SOURCE_TEXT)
        self.assertIsNotNone(result)
        self.assertEqual(result.confidence, 1.0)


class TestPropagandaDetector(unittest.TestCase):
    """Uses a mocked LLM client; does not touch any real backend."""

    def _detector_with_response(self, response: dict) -> PropagandaDetector:
        detector = PropagandaDetector(llm_enabled=False)
        detector._llm_client = _FakeLLMClient(response)
        detector.llm_enabled = True
        return detector

    def test_detect_returns_empty_when_llm_disabled(self):
        d = PropagandaDetector(llm_enabled=False)
        result = d.detect(SOURCE_TEXT)
        self.assertEqual(result.techniques, [])
        self.assertEqual(result.overall_propaganda_score, 0.0)

    def test_detect_validates_and_returns_technique(self):
        response = {
            "techniques": [
                {
                    "technique": "loaded_language",
                    "confidence": 0.85,
                    "evidence_span": "radical extremists want to destroy",
                },
                {
                    "technique": "ad_hominem",
                    "confidence": 0.75,
                    "evidence_span": "corrupt career politician would side",
                },
            ],
            "overall_propaganda_score": 0.72,
            "reasoning": "loaded + ad hominem",
        }
        detector = self._detector_with_response(response)
        result = detector.detect(SOURCE_TEXT)
        self.assertEqual(len(result.techniques), 2)
        self.assertEqual(result.overall_propaganda_score, 0.72)
        self.assertEqual(
            {t.technique for t in result.techniques},
            {"loaded_language", "ad_hominem"},
        )

    def test_detect_drops_fabricated_evidence(self):
        response = {
            "techniques": [
                {
                    "technique": "loaded_language",
                    "confidence": 0.9,
                    "evidence_span": "radical extremists want to destroy",
                },
                {
                    "technique": "name_calling",
                    "confidence": 0.9,
                    "evidence_span": "phrase not in source text",
                },
            ],
            "overall_propaganda_score": 0.6,
        }
        detector = self._detector_with_response(response)
        result = detector.detect(SOURCE_TEXT)
        self.assertEqual(len(result.techniques), 1)
        self.assertEqual(result.techniques[0].technique, "loaded_language")

    def test_detect_caps_score_when_all_invalid(self):
        response = {
            "techniques": [
                {
                    "technique": "name_calling",
                    "confidence": 0.9,
                    "evidence_span": "phrase not in source text",
                },
            ],
            "overall_propaganda_score": 0.8,  # would otherwise be headline-moving
        }
        detector = self._detector_with_response(response)
        result = detector.detect(SOURCE_TEXT)
        self.assertEqual(result.techniques, [])
        self.assertLessEqual(result.overall_propaganda_score, UNVERIFIED_EVIDENCE_CAP)

    def test_detect_forces_zero_when_no_techniques(self):
        response = {
            "techniques": [],
            "overall_propaganda_score": 0.4,  # contradictory, but ignored
        }
        detector = self._detector_with_response(response)
        result = detector.detect(SOURCE_TEXT)
        self.assertEqual(result.overall_propaganda_score, 0.0)

    def test_detect_caps_at_five_techniques(self):
        response = {
            "techniques": [
                {"technique": "loaded_language", "confidence": 0.9,
                 "evidence_span": "radical extremists want to destroy"}
                for _ in range(8)
            ],
            "overall_propaganda_score": 0.9,
        }
        detector = self._detector_with_response(response)
        result = detector.detect(SOURCE_TEXT)
        self.assertLessEqual(len(result.techniques), 5)

    def test_to_dict_is_stable(self):
        t = PropagandaTechnique(
            technique="loaded_language", confidence=0.8,
            evidence_span="radical extremists want to destroy",
        )
        r = PropagandaResult(
            techniques=[t], overall_propaganda_score=0.5, reasoning="test",
        )
        d = r.to_dict()
        self.assertEqual(d["overall_propaganda_score"], 0.5)
        self.assertEqual(d["techniques"][0]["technique"], "loaded_language")
        self.assertEqual(d["inference_method"], "llm")


if __name__ == "__main__":
    unittest.main()
