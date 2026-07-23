"""
Constants for analysis/src/etl/documents.py. See that module and
docs/audit-trail/analysis/2026-07-22-pg-etl-authors-documents-queue.md for
how each is used.
"""

from __future__ import annotations

import datetime

# Stamped onto every corpus.documents row. Bump whenever the filter
# keywords, matching semantics, recency rule, or extraction logic change.
ETL_VERSION = "pg-1"

THIRTY_DAYS = datetime.timedelta(days=30)

# Genuinely-invalid published_at bounds (ported from loader.py's
# `is_recent`): before 2020 or more than a day in the future.
MIN_VALID_PUBLISHED_AT = datetime.datetime(2020, 1, 1, tzinfo=datetime.timezone.utc)
FUTURE_SLOP = datetime.timedelta(days=1)

# domain_or_subreddit constant for every X post (fixed, unlike news/reddit).
X_DOMAIN_KEY = "x.com"

US_POLITICAL_KEYWORDS = frozenset([
    # Federal government
    "congress", "senate", "house of representatives", "president", "white house",
    "supreme court", "federal", "administration", "cabinet", "executive order",
    # Political parties
    "republican", "democrat", "gop", "dnc", "rnc", "conservative", "liberal",
    "progressive", "maga", "left-wing", "right-wing",
    # Politicians (common references)
    "trump", "biden", "harris", "pelosi", "mcconnell", "schumer", "desantis",
    "newsom", "aoc", "ocasio-cortez",
    # Political processes
    "election", "vote", "ballot", "poll", "campaign", "primary", "caucus",
    "midterm", "legislation", "bill", "law", "policy", "regulation",
    # Political topics
    "immigration", "border", "tariff", "trade war", "abortion", "gun control",
    "healthcare", "medicare", "medicaid", "social security", "tax",
    "stimulus", "infrastructure", "climate policy", "national guard",
    # Governance
    "governor", "senator", "congressman", "representative", "mayor",
    "attorney general", "secretary of state", "veto", "impeachment",
    "bipartisan", "partisan", "filibuster",
])

EXCLUDE_PATTERNS = [
    r"/sport", r"/football", r"/basketball", r"/baseball", r"/soccer",
    r"/music", r"/entertainment", r"/celebrity", r"/podcast",
    r"/recipes", r"/food", r"/travel", r"/lifestyle",
]

INDEX_CHROME_TERMS = (
    "skip to main content",
    "open navigation menu",
    "close navigation menu",
    "keyboard shortcuts for audio player",
    "expand/collapse submenu",
    "brand studio",
    "newsletters",
    "download our app",
    "watch cbs news",
    "npr shop",
    "terms of use",
    "privacy policy",
    "your privacy choices",
)

HUB_URL_PATTERN = r"^/(?:[a-z]{1,20}/?)?$"
