"""
Polling data scraper for live GOP favorability from RealClearPolling.

Fetches current polling averages during job runner execution and stores to cache.
"""

import re
import datetime
from typing import Any, Dict

import requests

from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


class PollingDataError(Exception):
    """Raised when polling data cannot be fetched or parsed."""
    pass


class PollingDataScraper:
    """Scrapes GOP favorability polling data from RealClearPolling."""
    
    URL = "https://www.realclearpolling.com/"
    TIMEOUT = 30  # seconds
    USER_AGENT = "CivicLens/1.0 (Political Analysis Tool)"
    
    def fetch_gop_favorability(self) -> Dict[str, Any]:
        """
        Scrape current Republican Party favorability polling data.
        
        Returns:
            Dict with keys:
                - favorable: float (percentage)
                - unfavorable: float (percentage)
                - neutral: float (calculated as 100 - favorable - unfavorable)
                - source: str
                - date: str (ISO format)
                
        Raises:
            PollingDataError: If scraping fails or data unavailable
        """
        logger.info(f"Fetching GOP favorability polling from {self.URL}")
        
        try:
            response = requests.get(
                self.URL,
                headers={"User-Agent": self.USER_AGENT},
                timeout=self.TIMEOUT
            )
            response.raise_for_status()
        except requests.exceptions.RequestException as e:
            raise PollingDataError(f"Failed to fetch polling page: {e}")
        
        html = response.text
        polling_data = self._parse_gop_favorability(html)
        
        logger.info(
            f"Scraped GOP favorability: {polling_data['favorable']}% favorable, "
            f"{polling_data['unfavorable']}% unfavorable"
        )
        
        return polling_data
    
    def _parse_gop_favorability(self, html: str) -> Dict[str, Any]:
        """
        Parse GOP favorability percentages from RealClearPolling HTML.
        
        Looks for the "Republican Party Favorability" section with format:
            Favorable XX.X%
            Unfavorable XX.X%
        """
        # Pattern to find the Republican Party Favorability section
        # The page shows favorable/unfavorable percentages near the section header
        
        # Look for pattern near "Republican Party Favorability" text
        section_pattern = r"Republican Party Favorability.*?Favorable.*?(\d+\.?\d*)%.*?Unfavorable.*?(\d+\.?\d*)%"
        match = re.search(section_pattern, html, re.DOTALL | re.IGNORECASE)
        
        if not match:
            # Try alternate pattern - sometimes order is reversed
            alt_pattern = r"Republican Party Favorability.*?Unfavorable.*?(\d+\.?\d*)%.*?Favorable.*?(\d+\.?\d*)%"
            match = re.search(alt_pattern, html, re.DOTALL | re.IGNORECASE)
            if match:
                unfavorable = float(match.group(1))
                favorable = float(match.group(2))
            else:
                raise PollingDataError(
                    "Could not find Republican Party Favorability data in page. "
                    "Page structure may have changed."
                )
        else:
            favorable = float(match.group(1))
            unfavorable = float(match.group(2))
        
        # Calculate neutral (remaining percentage)
        neutral = max(0.0, 100.0 - favorable - unfavorable)
        
        return {
            "favorable": round(favorable, 1),
            "unfavorable": round(unfavorable, 1),
            "neutral": round(neutral, 1),
            "source": "RealClearPolling (Live)",
            "date": datetime.datetime.now().strftime("%Y-%m-%d"),
        }
