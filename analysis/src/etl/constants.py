"""
Constants for analysis/src/etl/documents.py. See that module and
docs/audit-trail/analysis/2026-07-22-pg-etl-authors-documents-queue.md for
how each is used.
"""

from __future__ import annotations

import datetime

# Stamped onto every corpus.documents row. Bump whenever the filter
# keywords, matching semantics, recency rule, or extraction logic change.
# pg-2 (2026-07-23): X admission gains the official_record recency bypass.
ETL_VERSION = "pg-2"

THIRTY_DAYS = datetime.timedelta(days=30)

# Genuinely-invalid published_at bounds: before 2020 or more than a day in
# the future.
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

# is_index_page signal thresholds (see documents.py's _TextStats predicates).
LOW_PUNCT_PER_100_WORDS = 4.0
VERY_LOW_PUNCT_PER_100_WORDS = 1.0
HIGH_TITLECASE_SHARE = 0.65
MIN_WORDS_FOR_LENGTH_SIGNALS = 30
NAV_CHROME_HITS_GENERIC = 3  # chrome hits needed on any page
NAV_CHROME_HITS_HUB = 2  # lower bar once the URL is already hub-shaped

# AdmissionVerdict.reason values (see documents.py's admit_* gates).
DENIED_DOMAIN = "denied_domain"
STALE = "stale"
INDEX_PAGE = "index_page"
NOT_POLITICAL = "not_political"
ADMITTED = "admitted"

# Not an admission verdict: a news row whose raw_hash resolved to no
# readable/extractable file. Tallied in DocLoadResult.rejections only, so a
# misconfigured raw store (the 2026-07-23 zero-news prod incident) is loud
# in the ETL summary instead of silently zeroing the news corpus.
EXTRACTION_FAILED = "extraction_failed"

# corpus.documents.admission_class values (data/pg-migrations/0003_admission_class.sql).
ADMISSION_SAMPLED = "sampled"
ADMISSION_OFFICIAL_RECORD = "official_record"

# Lifetime (not per-window) cap on official_record docs per author -- an
# active official's public-record posts bypass the 30-day recency filter
# entirely, so without a ceiling one prolific account could dominate the
# corpus. Tuning knob: flagged for owner review, not derived from data.
OFFICIAL_RECORD_PER_AUTHOR_CAP = 200
