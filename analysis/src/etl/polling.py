"""
Polling data scraper for live party favorability from RealClearPolling.

Fetches current polling averages during job runner execution and stores to cache.
Supports dynamic party targeting (GOP, Democratic, etc.).
"""

import re
import time
import datetime
from typing import Any, Dict

import requests

from analysis.src.common.logger import get_logger

logger = get_logger(__name__)

# URL pattern for RealClearPolling party favorability pages
RCP_BASE_URL = "https://www.realclearpolling.com/polls/favorability"

# Supported party slug mappings
PARTY_SLUGS: Dict[str, str] = {
    "gop": "republican-party",
    "republican": "republican-party",
    "democratic": "democratic-party",
    "democrat": "democratic-party",
}

MAX_RETRIES = 2
RETRY_BACKOFF_SECONDS = 3
REQUEST_TIMEOUT = 30
MIN_TOTAL_PCT = 30.0
MAX_TOTAL_PCT = 100.0


class PollingDataError(Exception):
    """Raised when polling data cannot be fetched or parsed."""
    pass


class PollingDataScraper:
    """Scrapes party favorability polling data from RealClearPolling."""

    USER_AGENT = "CivicLens/1.0 (Political Analysis Tool)"

    def _build_url(self, party: str) -> str:
        """Build the polling URL for a given party identifier."""
        slug = PARTY_SLUGS.get(party.lower())
        if not slug:
            raise PollingDataError(
                f"Unsupported party '{party}'. "
                f"Supported: {', '.join(PARTY_SLUGS.keys())}"
            )
        return f"{RCP_BASE_URL}/{slug}"

    def fetch_party_favorability(
        self, party: str = "gop"
    ) -> Dict[str, Any]:
        """
        Scrape current party favorability polling data.

        Args:
            party: Party identifier (gop, republican, democratic, democrat).

        Returns:
            Dict with keys: favorable, unfavorable, neutral, source, date, party

        Raises:
            PollingDataError: If scraping fails or data unavailable
        """
        url = self._build_url(party)
        logger.info(f"Fetching {party} favorability polling from {url}")

        html = self._fetch_with_retry(url)
        polling_data = self._parse_rcp_average(html, party)

        logger.info(
            f"Scraped {party} favorability: "
            f"{polling_data['favorable']}% favorable, "
            f"{polling_data['unfavorable']}% unfavorable"
        )
        return polling_data

    def fetch_gop_favorability(self) -> Dict[str, Any]:
        """Backward-compatible alias for fetch_party_favorability('gop')."""
        return self.fetch_party_favorability("gop")

    def _fetch_with_retry(self, url: str) -> str:
        """Fetch URL content with retry and backoff."""
        last_error = None
        for attempt in range(MAX_RETRIES + 1):
            try:
                response = requests.get(
                    url,
                    headers={"User-Agent": self.USER_AGENT},
                    timeout=REQUEST_TIMEOUT,
                )
                response.raise_for_status()
                return response.text
            except requests.exceptions.RequestException as exc:
                last_error = exc
                if attempt < MAX_RETRIES:
                    wait = RETRY_BACKOFF_SECONDS * (attempt + 1)
                    logger.warning(
                        f"Polling fetch attempt {attempt + 1} failed: {exc}. "
                        f"Retrying in {wait}s..."
                    )
                    time.sleep(wait)
        raise PollingDataError(f"Failed after {MAX_RETRIES + 1} attempts: {last_error}")

    def _parse_rcp_average(self, html: str, party: str) -> Dict[str, Any]:
        """
        Parse the RCP Average row from the polling table.

        The table contains rows for individual polls plus an 'RCP Average' row.
        Columns: Pollster | Date | Sample | Favorable | Unfavorable | Spread
        """
        favorable, unfavorable = self._extract_rcp_row(html)
        self._validate_percentages(favorable, unfavorable)

        neutral = max(0.0, 100.0 - favorable - unfavorable)

        return {
            "favorable": round(favorable, 1),
            "unfavorable": round(unfavorable, 1),
            "neutral": round(neutral, 1),
            "source": "RealClearPolling (Live)",
            "date": datetime.datetime.now().strftime("%Y-%m-%d"),
            "party": party.lower(),
        }

    def _extract_rcp_row(self, html: str) -> tuple[float, float]:
        """Extract favorable/unfavorable from the RCP Average table row."""
        # Find the table row containing RCP Average
        row_pattern = r"<tr[^>]*>(?:(?!</tr>).)*?RCP\s*Average(?:(?!</tr>).)*?</tr>"
        row_match = re.search(row_pattern, html, re.DOTALL | re.IGNORECASE)

        if row_match:
            row_html = row_match.group(0)
            # Extract all <td> cell contents
            cells = re.findall(r"<td[^>]*>(.*?)</td>", row_html, re.DOTALL)
            # Strip HTML tags from cell content
            clean_cells = [re.sub(r"<[^>]+>", "", c).strip() for c in cells]
            # Columns: [Pollster, Date, Sample, Favorable, Unfavorable, Spread]
            if len(clean_cells) >= 5:
                try:
                    favorable = float(clean_cells[3])
                    unfavorable = float(clean_cells[4])
                    return favorable, unfavorable
                except (ValueError, IndexError):
                    pass

        # Fallback: Favorable XX.X% / Unfavorable XX.X% text pattern
        fav_pattern = (
            r"Favorable.*?(\d+\.?\d*)%"
            r".*?"
            r"Unfavorable.*?(\d+\.?\d*)%"
        )
        match = re.search(fav_pattern, html, re.DOTALL | re.IGNORECASE)
        if match:
            return float(match.group(1)), float(match.group(2))

        # Reversed order fallback
        rev_pattern = (
            r"Unfavorable.*?(\d+\.?\d*)%"
            r".*?"
            r"Favorable.*?(\d+\.?\d*)%"
        )
        match = re.search(rev_pattern, html, re.DOTALL | re.IGNORECASE)
        if match:
            return float(match.group(2)), float(match.group(1))

        raise PollingDataError(
            "Could not find RCP Average or favorability data in page. "
            "Page structure may have changed."
        )

    @staticmethod
    def _validate_percentages(favorable: float, unfavorable: float) -> None:
        """Reject non-sensical percentage values."""
        total = favorable + unfavorable
        if total < MIN_TOTAL_PCT or total > MAX_TOTAL_PCT:
            raise PollingDataError(
                f"Parsed percentages look invalid: "
                f"favorable={favorable}, unfavorable={unfavorable} "
                f"(total={total}, expected {MIN_TOTAL_PCT}-{MAX_TOTAL_PCT})"
            )

    # Legacy compatibility alias
    def _parse_gop_favorability(self, html: str) -> Dict[str, Any]:
        """Legacy method - delegates to _parse_rcp_average."""
        return self._parse_rcp_average(html, "gop")
