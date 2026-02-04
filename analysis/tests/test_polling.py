"""
Tests for polling data scraper.
"""

import unittest
from unittest.mock import patch, Mock

from analysis.src.etl.polling import PollingDataScraper, PollingDataError


class TestPollingDataScraper(unittest.TestCase):
    """Tests for PollingDataScraper."""
    
    def test_parse_gop_favorability_success(self):
        """Test parsing valid HTML with GOP favorability data."""
        scraper = PollingDataScraper()
        
        # Sample HTML mimicking RealClearPolling structure
        html = """
        <div>
            <h2>Republican Party Favorability</h2>
            <div>Favorable 42.5%</div>
            <div>Unfavorable 51.3%</div>
        </div>
        """
        
        result = scraper._parse_gop_favorability(html)
        
        self.assertEqual(result["favorable"], 42.5)
        self.assertEqual(result["unfavorable"], 51.3)
        self.assertIn("neutral", result)
        self.assertEqual(result["source"], "RealClearPolling (Live)")
        self.assertIn("date", result)
    
    def test_parse_gop_favorability_alternate_order(self):
        """Test parsing when unfavorable comes before favorable."""
        scraper = PollingDataScraper()
        
        html = """
        <div>
            <h2>Republican Party Favorability</h2>
            <div>Unfavorable 55.0%</div>
            <div>Favorable 40.0%</div>
        </div>
        """
        
        result = scraper._parse_gop_favorability(html)
        
        self.assertEqual(result["favorable"], 40.0)
        self.assertEqual(result["unfavorable"], 55.0)
    
    def test_parse_gop_favorability_missing_data(self):
        """Test that PollingDataError is raised when data not found."""
        scraper = PollingDataScraper()
        
        html = "<html><body>No polling data here</body></html>"
        
        with self.assertRaises(PollingDataError):
            scraper._parse_gop_favorability(html)
    
    @patch('analysis.src.etl.polling.requests.get')
    def test_fetch_gop_favorability_success(self, mock_get):
        """Test successful fetch and parse."""
        scraper = PollingDataScraper()
        
        mock_response = Mock()
        mock_response.text = """
        <div>
            Republican Party Favorability
            View Polls
            Favorable 40.2%
            Unfavorable 53.2%
        </div>
        """
        mock_response.raise_for_status = Mock()
        mock_get.return_value = mock_response
        
        result = scraper.fetch_gop_favorability()
        
        self.assertEqual(result["favorable"], 40.2)
        self.assertEqual(result["unfavorable"], 53.2)
        mock_get.assert_called_once()
    
    @patch('analysis.src.etl.polling.requests.get')
    def test_fetch_gop_favorability_network_error(self, mock_get):
        """Test that network errors raise PollingDataError."""
        import requests
        
        scraper = PollingDataScraper()
        mock_get.side_effect = requests.exceptions.ConnectionError("Network error")
        
        with self.assertRaises(PollingDataError):
            scraper.fetch_gop_favorability()


if __name__ == '__main__':
    unittest.main()
