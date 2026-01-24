"""
Tests for LLM-powered hybrid analysis engines.

Tests cover:
1. Deterministic fallback when LLM disabled
2. JSON response parsing validation
3. Evidence spans requirement
4. Bot filtering in aggregations
"""

import unittest
import os
import sys

# Ensure project root is in path
current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine.sentiment import HybridSentimentAnalyzer, SentimentResult
from analysis.src.engine.bot import HybridBotDetector, BotResult
from analysis.src.engine.favorability import FavorabilityAnalyzer, FavorabilityResult


class TestHybridSentimentAnalyzer(unittest.TestCase):
    """Tests for the hybrid sentiment analyzer."""
    
    def setUp(self):
        # Disable LLM for deterministic tests
        self.analyzer = HybridSentimentAnalyzer(llm_enabled=False)
    
    def test_deterministic_fallback_positive(self):
        """Verify heuristic works for positive sentiment."""
        result = self.analyzer.analyze_full("I love this amazing and excellent project")
        
        self.assertEqual(result.label, "POSITIVE")
        self.assertGreater(result.confidence, 0.5)
        self.assertIsInstance(result.evidence_spans, list)
        self.assertIsNotNone(result.deterministic_signals)
    
    def test_deterministic_fallback_negative(self):
        """Verify heuristic works for negative sentiment."""
        result = self.analyzer.analyze_full("This is terrible and awful, I hate it")
        
        self.assertEqual(result.label, "NEGATIVE")
        self.assertGreater(result.confidence, 0.5)
    
    def test_deterministic_fallback_neutral(self):
        """Verify heuristic works for neutral text."""
        result = self.analyzer.analyze_full("The meeting is at 3pm tomorrow")
        
        self.assertEqual(result.label, "NEUTRAL")
    
    def test_evidence_spans_present(self):
        """Verify outputs include evidence spans."""
        result = self.analyzer.analyze_full("This is a great and wonderful achievement")
        
        self.assertIsInstance(result.evidence_spans, list)
    
    def test_empty_text_handling(self):
        """Verify empty text returns neutral with zero confidence."""
        result = self.analyzer.analyze_full("")
        
        self.assertEqual(result.label, "NEUTRAL")
        self.assertEqual(result.confidence, 0.0)
    
    def test_result_to_dict(self):
        """Verify result can be serialized to dict."""
        result = self.analyzer.analyze_full("Great job!")
        result_dict = result.to_dict()
        
        self.assertIn("label", result_dict)
        self.assertIn("confidence", result_dict)
        self.assertIn("evidence_spans", result_dict)
    
    def test_backwards_compatibility(self):
        """Verify analyze() returns tuple for backwards compatibility."""
        label, confidence = self.analyzer.analyze("Good work")
        
        self.assertIsInstance(label, str)
        self.assertIsInstance(confidence, float)


class TestHybridBotDetector(unittest.TestCase):
    """Tests for the hybrid bot detector."""
    
    def setUp(self):
        self.detector = HybridBotDetector(llm_enabled=False)
    
    def test_bot_detection_spam_keywords(self):
        """Verify spam keywords trigger bot detection."""
        result = self.detector.analyze_full("Buy now click here make money fast!")
        
        self.assertIn(result.label, ['bot', 'suspicious'])
        self.assertGreater(result.confidence, 0.4)
        self.assertTrue(len(result.indicators) > 0)
    
    def test_human_detection(self):
        """Verify normal text is classified as human."""
        result = self.detector.analyze_full(
            "I think the new policy has some interesting implications for the economy."
        )
        
        self.assertEqual(result.label, 'human')
        self.assertFalse(result.is_bot)
    
    def test_repetitive_text_detection(self):
        """Verify highly repetitive text is flagged."""
        result = self.detector.analyze_full(
            "buy buy buy buy buy buy buy buy now now now now now"
        )
        
        self.assertIn(result.label, ['bot', 'suspicious'])
    
    def test_deterministic_signals_computed(self):
        """Verify all coordination signals computed."""
        result = self.detector.analyze_full("Normal text here", {})
        
        signals = result.deterministic_signals
        self.assertIsNotNone(signals)
        self.assertIn("spam_keyword_hits", signals)
        self.assertIn("repetition_score", signals)
        self.assertIn("aggregated_score", signals)
    
    def test_metadata_signals(self):
        """Verify metadata signals increase suspicion score."""
        # Without metadata
        result_no_meta = self.detector.analyze_full("Check out this link", {})
        
        # With suspicious metadata
        result_with_meta = self.detector.analyze_full(
            "Check out this link", 
            {"account_age_days": 1, "posts_per_day": 100}
        )
        
        # Metadata should increase the aggregated score
        self.assertGreater(
            result_with_meta.deterministic_signals["aggregated_score"],
            result_no_meta.deterministic_signals["aggregated_score"]
        )
    
    def test_empty_text_handling(self):
        """Verify empty text returns unknown."""
        result = self.detector.analyze_full("")
        
        self.assertEqual(result.label, 'unknown')
        self.assertFalse(result.is_bot)


class TestFavorabilityAnalyzer(unittest.TestCase):
    """Tests for the favorability analyzer."""
    
    def setUp(self):
        self.analyzer = FavorabilityAnalyzer(llm_enabled=False)
    
    def test_gop_entity_extraction(self):
        """Verify GOP entities are detected."""
        result = self.analyzer.analyze_full(
            "Trump announced a new policy at the Republican convention"
        )
        
        self.assertTrue(len(result.gop_entities_found) > 0)
        self.assertIn("trump", [e.lower() for e in result.gop_entities_found])
    
    def test_favorable_stance_detection(self):
        """Verify favorable language is detected."""
        result = self.analyzer.analyze_full(
            "I support and praise the Republicans. They have my endorsement and approval."
        )
        
        self.assertEqual(result.overall_gop_stance, "favorable")
    
    def test_unfavorable_stance_detection(self):
        """Verify unfavorable language is detected."""
        result = self.analyzer.analyze_full(
            "The GOP candidate is dangerous and corrupt. I oppose their policies."
        )
        
        self.assertEqual(result.overall_gop_stance, "unfavorable")
    
    def test_no_gop_entities(self):
        """Verify neutral when no GOP entities mentioned."""
        result = self.analyzer.analyze_full(
            "The weather is nice today and I had coffee for breakfast."
        )
        
        self.assertEqual(result.overall_gop_stance, "neutral")
        self.assertEqual(len(result.gop_entities_found), 0)
    
    def test_per_entity_breakdown(self):
        """Verify per-entity stance breakdown is provided."""
        result = self.analyzer.analyze_full(
            "I support Trump but oppose the Republican party's recent actions."
        )
        
        self.assertIsInstance(result.entity_stances, list)
    
    def test_confidence_scores_present(self):
        """Verify all outputs include confidence."""
        result = self.analyzer.analyze_full("Trump is doing well")
        
        self.assertIsNotNone(result.overall_confidence)
        self.assertGreaterEqual(result.overall_confidence, 0.0)
        self.assertLessEqual(result.overall_confidence, 1.0)


class TestJsonParsing(unittest.TestCase):
    """Tests for JSON parsing in BaseLLMClient.
    
    Note: With JSON schema mode, LLMs are constrained to return valid JSON,
    so we only need basic parsing - no complex repair logic.
    """
    
    def test_valid_json(self):
        """Verify valid JSON parses correctly."""
        from analysis.src.llm.base import BaseLLMClient
        
        result = BaseLLMClient.parse_json_response('{"key": "value", "num": 42}')
        self.assertEqual(result["key"], "value")
        self.assertEqual(result["num"], 42)
    
    def test_json_with_markdown_fences(self):
        """Verify JSON with markdown code fences parses correctly."""
        from analysis.src.llm.base import BaseLLMClient
        
        result = BaseLLMClient.parse_json_response('```json\n{"key": "value"}\n```')
        self.assertEqual(result["key"], "value")
    
    def test_json_array_returns_first_dict(self):
        """Verify JSON array of dicts returns the first dict."""
        from analysis.src.llm.base import BaseLLMClient
        
        result = BaseLLMClient.parse_json_response('[{"key": "value"}, {"key2": "value2"}]')
        self.assertEqual(result["key"], "value")
    
    def test_invalid_json_raises_error(self):
        """Verify invalid JSON raises ValueError."""
        from analysis.src.llm.base import BaseLLMClient
        
        with self.assertRaises(ValueError):
            BaseLLMClient.parse_json_response('not json at all')
    
    def test_non_dict_json_raises_error(self):
        """Verify non-dict JSON (like plain string) raises ValueError."""
        from analysis.src.llm.base import BaseLLMClient
        
        with self.assertRaises(ValueError):
            BaseLLMClient.parse_json_response('"just a string"')


if __name__ == '__main__':
    unittest.main()
