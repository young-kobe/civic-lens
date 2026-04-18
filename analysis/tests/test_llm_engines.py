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

from analysis.src.engine.bot import HybridBotDetector, BotResult
from analysis.src.engine.analyzer import Analyzer




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


class TestAnalyzer(unittest.TestCase):
    """Tests for the unified analyzer's deterministic fallbacks."""

    def setUp(self):
        # Disable LLM for deterministic tests
        self.analyzer = Analyzer(llm_enabled=False)

    def test_deterministic_fallback_positive(self):
        """Verify heuristic works for positive sentiment."""
        result, _ = self.analyzer.analyze_full("I love this amazing and excellent project")
        self.assertEqual(result.label, "POSITIVE")
        self.assertGreater(result.confidence, 0.5)
        self.assertIsInstance(result.evidence_spans, list)
        self.assertIsNotNone(result.deterministic_signals)

    def test_deterministic_fallback_negative(self):
        """Verify heuristic works for negative sentiment."""
        result, _ = self.analyzer.analyze_full("This is terrible and awful, I hate it")
        self.assertEqual(result.label, "NEGATIVE")
        self.assertGreater(result.confidence, 0.5)

    def test_deterministic_fallback_neutral(self):
        """Verify heuristic works for neutral text."""
        result, _ = self.analyzer.analyze_full("The meeting is at 3pm tomorrow")
        self.assertEqual(result.label, "NEUTRAL")

    def test_deterministic_fallback_favorability(self):
        """Verify heuristic works for favorability."""
        # Mentions GOP entity with favorable keyword
        _, fav = self.analyzer.analyze_full("Trump was praised for his speech today")
        self.assertEqual(fav.overall_gop_stance, "favorable")
        self.assertIn("Trump", fav.gop_entities_found)

        # Mentions GOP entity with unfavorable keyword
        _, fav = self.analyzer.analyze_full("McConnell was criticized for his policy")
        self.assertEqual(fav.overall_gop_stance, "unfavorable")
        self.assertIn("McConnell", fav.gop_entities_found)



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
