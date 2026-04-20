"""
Tests for polling data scraper.
"""

import unittest
from unittest.mock import patch, Mock

from analysis.src.etl.polling import PollingDataScraper, PollingDataError


RCP_TABLE_HTML = """
<table class="w-full">
  <thead><tr><th>Poll</th><th>Date</th><th>Sample</th><th>Favorable</th><th>Unfavorable</th><th>Spread</th></tr></thead>
  <tbody>
    <tr><td>CNN/SSRS</td><td>3/21 - 3/24</td><td>1208 A</td><td>39.0</td><td>55.0</td><td>-16.0</td></tr>
    <tr class="!bg-secondary font-bold"><td>RCP Average</td><td>11/3 - 3/30</td><td>--</td><td>38.9</td><td>54.3</td><td>-15.4</td></tr>
  </tbody>
</table>
"""

LEGACY_HTML = """
<div>
    <h2>Republican Party Favorability</h2>
    <div>Favorable 42.5%</div>
    <div>Unfavorable 51.3%</div>
</div>
"""


class TestPollingDataScraper(unittest.TestCase):
    """Tests for PollingDataScraper."""

    def test_parse_rcp_average_table(self):
        """Test parsing the RCP Average row from a table."""
        scraper = PollingDataScraper()
        result = scraper._parse_rcp_average(RCP_TABLE_HTML, "gop")

        self.assertEqual(result["favorable"], 38.9)
        self.assertEqual(result["unfavorable"], 54.3)
        self.assertIn("neutral", result)
        self.assertEqual(result["source"], "RealClearPolling (Live)")
        self.assertEqual(result["party"], "gop")

    def test_parse_legacy_format(self):
        """Test fallback parsing with Favorable/Unfavorable percentage format."""
        scraper = PollingDataScraper()
        result = scraper._parse_rcp_average(LEGACY_HTML, "gop")

        self.assertEqual(result["favorable"], 42.5)
        self.assertEqual(result["unfavorable"], 51.3)

    def test_parse_reversed_order(self):
        """Test parsing when unfavorable comes before favorable."""
        html = """
        <div>
            Unfavorable 55.0%
            Favorable 40.0%
        </div>
        """
        scraper = PollingDataScraper()
        result = scraper._parse_rcp_average(html, "gop")

        self.assertEqual(result["favorable"], 40.0)
        self.assertEqual(result["unfavorable"], 55.0)

    def test_parse_missing_data(self):
        """Test that PollingDataError is raised when data not found."""
        scraper = PollingDataScraper()
        html = "<html><body>No polling data here</body></html>"

        with self.assertRaises(PollingDataError):
            scraper._parse_rcp_average(html, "gop")

    def test_validate_percentages_rejects_bad_values(self):
        """Test that validation rejects non-sensical totals."""
        scraper = PollingDataScraper()
        with self.assertRaises(PollingDataError):
            scraper._validate_percentages(5.0, 5.0)  # total=10, too low

    def test_build_url_gop(self):
        """Test URL generation for GOP."""
        scraper = PollingDataScraper()
        url = scraper._build_url("gop")
        self.assertIn("republican-party", url)

    def test_build_url_democratic(self):
        """Test URL generation for Democratic party."""
        scraper = PollingDataScraper()
        url = scraper._build_url("democratic")
        self.assertIn("democratic-party", url)

    def test_build_url_unsupported(self):
        """Test that unsupported party raises PollingDataError."""
        scraper = PollingDataScraper()
        with self.assertRaises(PollingDataError):
            scraper._build_url("libertarian")

    @patch('analysis.src.etl.polling.requests.get')
    def test_fetch_gop_favorability_success(self, mock_get):
        """Test successful fetch and parse."""
        scraper = PollingDataScraper()

        mock_response = Mock()
        mock_response.text = RCP_TABLE_HTML
        mock_response.raise_for_status = Mock()
        mock_get.return_value = mock_response

        result = scraper.fetch_gop_favorability()

        self.assertEqual(result["favorable"], 38.9)
        self.assertEqual(result["unfavorable"], 54.3)
        mock_get.assert_called_once()

    @patch('analysis.src.etl.polling.requests.get')
    def test_fetch_party_favorability_democratic(self, mock_get):
        """Test fetching Democratic party favorability."""
        scraper = PollingDataScraper()

        mock_response = Mock()
        mock_response.text = RCP_TABLE_HTML.replace("38.9", "35.6").replace("54.3", "55.6")
        mock_response.raise_for_status = Mock()
        mock_get.return_value = mock_response

        result = scraper.fetch_party_favorability("democratic")

        self.assertEqual(result["party"], "democratic")
        mock_get.assert_called_once()
        call_url = mock_get.call_args[0][0]
        self.assertIn("democratic-party", call_url)

    @patch('analysis.src.etl.polling.time.sleep')
    @patch('analysis.src.etl.polling.requests.get')
    def test_fetch_retries_on_failure(self, mock_get, mock_sleep):
        """Test that network errors trigger retries. ``time.sleep`` is
        mocked so the test runs in milliseconds instead of waiting 9s of
        real backoff between retries."""
        import requests as req

        scraper = PollingDataScraper()
        mock_get.side_effect = req.exceptions.ConnectionError("Network error")

        with self.assertRaises(PollingDataError):
            scraper.fetch_gop_favorability()

        # Should have been called MAX_RETRIES + 1 times (initial + retries)
        self.assertEqual(mock_get.call_count, 3)
        # Each failed attempt (except the last) sleeps before retrying.
        self.assertEqual(mock_sleep.call_count, 2)

    def test_backward_compatibility_parse(self):
        """Test legacy _parse_gop_favorability still works."""
        scraper = PollingDataScraper()
        result = scraper._parse_gop_favorability(LEGACY_HTML)
        self.assertEqual(result["favorable"], 42.5)


if __name__ == '__main__':
    unittest.main()
