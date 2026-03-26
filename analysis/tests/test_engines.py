
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

if __name__ == '__main__':
    unittest.main()
