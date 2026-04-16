
import os
import sys
import unittest

# Ensure project root is in path
current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir)) 
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine.bot import HybridBotDetector
from analysis.src.engine.clustering import ContentClusterer
from analysis.src.engine.analyzer import Analyzer

class TestEngines(unittest.TestCase):
    def test_bot_detector(self):
        detector = HybridBotDetector()
        
        # Test bot case
        score, label = detector.analyze("buy now click here make money", {})
        self.assertIn(label, ['bot', 'suspicious'])
        self.assertGreater(score, 0.5)
        
        # Test human case
        score, label = detector.analyze("This is a thoughtful comment about civic policy.", {})
        self.assertEqual(label, 'human')
        self.assertLess(score, 0.4)

    def test_clustering(self):
        clusterer = ContentClusterer()
        docs = [
            {"doc_id": 1, "title": "Apple", "text": "apple banana cherry fruit"},
            {"doc_id": 2, "title": "Banana", "text": "apple banana cherry fruit"},
            {"doc_id": 3, "title": "Dog", "text": "dog cat mouse pet"},
            {"doc_id": 4, "title": "Cat", "text": "dog cat mouse pet"}
        ]
        
        clusters = clusterer.cluster_documents(docs)
        self.assertEqual(len(clusters), 2)
        sizes = sorted([c['size'] for c in clusters])
        self.assertEqual(sizes, [2, 2])


class TestAnalyzer(unittest.TestCase):
    """Tests for heuristic-only Analyzer classification."""

    def setUp(self):
        self.analyzer = Analyzer(llm_enabled=False)

    def test_derogatory_text_is_negative(self):
        """Derogatory labels directed at a political group should classify as NEGATIVE."""
        sent, _ = self.analyzer.analyze_full("Democrats are satanists and traitors")
        self.assertEqual(sent.label, "NEGATIVE")
        self.assertGreater(sent.confidence, 0.5)

    def test_empty_text_is_neutral(self):
        """Empty text should classify as NEUTRAL with zero confidence."""
        sent, _ = self.analyzer.analyze_full("")
        self.assertEqual(sent.label, "NEUTRAL")
        self.assertEqual(sent.confidence, 0.0)

    def test_clearly_positive_text(self):
        """Clearly positive text should classify as POSITIVE."""
        sent, _ = self.analyzer.analyze_full("This reform is excellent and beneficial for progress")
        self.assertEqual(sent.label, "POSITIVE")
        self.assertGreater(sent.confidence, 0.5)

    def test_clearly_negative_text(self):
        """Hostile language should classify as NEGATIVE."""
        sent, _ = self.analyzer.analyze_full("This is a terrible disaster and a complete failure")
        self.assertEqual(sent.label, "NEGATIVE")
        self.assertGreater(sent.confidence, 0.5)

    def test_neutral_text(self):
        """Neutral factual text with no sentiment words should be NEUTRAL."""
        sent, _ = self.analyzer.analyze_full("The committee met on Tuesday to discuss the schedule")
        self.assertEqual(sent.label, "NEUTRAL")

if __name__ == '__main__':
    unittest.main()
