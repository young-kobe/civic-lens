"""
Tests for analysis/src/api/models/common.py -- the Phase 9 CamelModel base,
LeanLabel's three-epistemic-kinds invariant, and SampleDocModel. Pure
pydantic validation, no DB.
"""

from __future__ import annotations

import unittest

from pydantic import ValidationError

from analysis.src.api.models.common import LeanLabel, RangeMeta, SampleDocModel


class RangeMetaTests(unittest.TestCase):
    """RangeMeta is the honesty block on every aggregate response: the two
    admission bases and the contributing model_ids are mandatory, so a
    long-range aggregate can never present itself as one homogeneous
    series."""

    def test_requires_counts_and_model_ids(self):
        with self.assertRaises(ValidationError):
            RangeMeta(window="30d")

    def test_all_time_range_with_metadata(self):
        meta = RangeMeta(
            window="all",
            sampled_doc_count=7000,
            official_record_doc_count=350,
            model_ids=["gemini-2.5-flash", "gemini-3.5-flash"],
        )
        self.assertIsNone(meta.start)
        self.assertIsNone(meta.end)
        self.assertEqual(len(meta.model_ids), 2)

    def test_json_uses_camel_case_aliases(self):
        meta = RangeMeta(
            window="7d", sampled_doc_count=1, official_record_doc_count=0,
            model_ids=["gemini-2.5-flash"],
        )
        dumped = meta.model_dump(by_alias=True)
        self.assertIn("sampledDocCount", dumped)
        self.assertIn("officialRecordDocCount", dumped)
        self.assertIn("modelIds", dumped)


class LeanLabelTests(unittest.TestCase):
    def test_fact_without_evidence_is_valid(self):
        label = LeanLabel(kind="fact", value="republican")
        self.assertIsNone(label.lean_share)
        self.assertIsNone(label.confidence)
        self.assertIsNone(label.sample_count)

    def test_curated_without_evidence_is_valid(self):
        label = LeanLabel(kind="curated", value="center-left")
        self.assertEqual(label.kind, "curated")

    def test_derived_with_full_evidence_is_valid(self):
        label = LeanLabel(
            kind="derived", value="democrat",
            lean_share=0.62, confidence=0.8, sample_count=12,
        )
        self.assertEqual(label.sample_count, 12)

    def test_derived_without_evidence_is_rejected(self):
        # The presentation invariant this model exists to enforce: a
        # derived lean must never render without the counts backing it.
        with self.assertRaises(ValidationError):
            LeanLabel(kind="derived", value="democrat")

    def test_derived_with_partial_evidence_is_rejected(self):
        with self.assertRaises(ValidationError):
            LeanLabel(kind="derived", value="democrat", lean_share=0.5, confidence=0.5)

    def test_fact_with_evidence_is_rejected(self):
        # A fact/curated lean carrying derived-only fields would blur the
        # exact distinction this model is meant to keep visible.
        with self.assertRaises(ValidationError):
            LeanLabel(kind="fact", value="republican", lean_share=0.5, confidence=0.5, sample_count=5)

    def test_curated_with_evidence_is_rejected(self):
        with self.assertRaises(ValidationError):
            LeanLabel(kind="curated", value="center-left", sample_count=5)

    def test_json_uses_camel_case_aliases(self):
        label = LeanLabel(
            kind="derived", value="democrat",
            lean_share=0.62, confidence=0.8, sample_count=12,
        )
        dumped = label.model_dump(by_alias=True)
        self.assertIn("leanShare", dumped)
        self.assertIn("sampleCount", dumped)
        self.assertNotIn("lean_share", dumped)


class SampleDocModelTests(unittest.TestCase):
    def test_valid_sample_round_trips(self):
        sample = SampleDocModel(
            doc_id=1, source_url="https://example.com/a", confidence=0.9,
            admission_class="sampled",
        )
        dumped = sample.model_dump(by_alias=True)
        self.assertEqual(dumped["sourceUrl"], "https://example.com/a")
        self.assertEqual(dumped["admissionClass"], "sampled")

    def test_blank_source_url_is_rejected(self):
        with self.assertRaises(ValidationError):
            SampleDocModel(doc_id=1, source_url="", confidence=0.9, admission_class="sampled")

    def test_missing_confidence_is_rejected(self):
        with self.assertRaises(ValidationError):
            SampleDocModel(doc_id=1, source_url="https://example.com/a", admission_class="sampled")

    def test_invalid_admission_class_is_rejected(self):
        with self.assertRaises(ValidationError):
            SampleDocModel(
                doc_id=1, source_url="https://example.com/a", confidence=0.9,
                admission_class="bogus",
            )


if __name__ == "__main__":
    unittest.main()
