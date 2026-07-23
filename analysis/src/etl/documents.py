"""
analysis/src/etl/documents.py — normalizes raw.* into corpus.documents +
subtype tables. Ports loader.py's US-politics keyword filter (word-boundary
matched here, not substring), index-page detector, and 30-day recency rule;
adds an additive domain_filter (deny/allow/cap) from data/seeds.yaml plus
outlet/subreddit entity-FK backfill. Run after authors.py's
sync_x_authors(). Full rationale:
docs/audit-trail/analysis/2026-07-22-pg-etl-authors-documents-queue.md
"""

from __future__ import annotations

import datetime
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional
from urllib.parse import urlparse

import trafilatura
import yaml
from psycopg import Connection

from analysis.src.common import db
from analysis.src.common.logger import get_logger
from analysis.src.common.canonicalize import canonicalize_news_domain, canonicalize_subreddit
from analysis.src.etl.constants import (
    ADMITTED,
    DENIED_DOMAIN,
    ETL_VERSION,
    EXCLUDE_PATTERNS,
    FUTURE_SLOP,
    HIGH_TITLECASE_SHARE,
    HUB_URL_PATTERN,
    INDEX_CHROME_TERMS,
    INDEX_PAGE,
    LOW_PUNCT_PER_100_WORDS,
    MIN_VALID_PUBLISHED_AT,
    MIN_WORDS_FOR_LENGTH_SIGNALS,
    NAV_CHROME_HITS_GENERIC,
    NAV_CHROME_HITS_HUB,
    NOT_POLITICAL,
    STALE,
    THIRTY_DAYS,
    US_POLITICAL_KEYWORDS,
    VERY_LOW_PUNCT_PER_100_WORDS,
    X_DOMAIN_KEY,
)

logger = get_logger(__name__)

REPO_ROOT = Path(__file__).resolve().parents[3]
_DEFAULT_SEEDS_PATH = REPO_ROOT / "data" / "seeds.yaml"
_DEFAULT_RAW_ROOT = REPO_ROOT / "data" / "raw" / "sha256"

# ---------------------------------------------------------------------------
# US-politics content filter
# ---------------------------------------------------------------------------

# Longest-first: regex alternation picks the first matching alternative at
# a position, not the longest, so ordering avoids a shorter keyword shadowing
# a multi-word phrase that contains it.
_KEYWORD_PATTERN = re.compile(
    r"\b(?:" + "|".join(re.escape(k) for k in sorted(US_POLITICAL_KEYWORDS, key=len, reverse=True)) + r")\b"
)

_HUB_URL_RE = re.compile(HUB_URL_PATTERN)


def is_us_political_content(text: str, title: str = "", url: str = "") -> bool:
    """Word-boundary match against US_POLITICAL_KEYWORDS, after the
    URL-path exclusion check."""
    if not text and not title:
        return False

    url_lower = url.lower()
    for pattern in EXCLUDE_PATTERNS:
        if re.search(pattern, url_lower):
            return False

    combined = f"{title} {text}".lower()
    return bool(_KEYWORD_PATTERN.search(combined))


@dataclass(frozen=True)
class _TextStats:
    """Signals computed once from (text, url) and shared by the
    is_index_page predicates below."""

    word_count: int
    punct_per_100_words: float
    titlecase_share: float
    chrome_hits: int
    hub_url: bool


def _compute_text_stats(text: str, url: str) -> _TextStats:
    words = text.split()
    word_count = len(words)

    lowered = text.lower()
    chrome_hits = sum(term in lowered for term in INDEX_CHROME_TERMS)

    sentence_ends = len(re.findall(r"[.!?](?:\s|$)", text))
    punct_per_100_words = (sentence_ends / word_count * 100) if word_count else 0.0

    alpha_words = [w for w in words if w[:1].isalpha()]
    titlecase_share = (
        sum(w[0].isupper() for w in alpha_words) / len(alpha_words)
        if alpha_words else 0.0
    )

    path = urlparse(url).path or "/"
    hub_url = bool(_HUB_URL_RE.match(path))

    return _TextStats(
        word_count=word_count,
        punct_per_100_words=punct_per_100_words,
        titlecase_share=titlecase_share,
        chrome_hits=chrome_hits,
        hub_url=hub_url,
    )


def _hub_url_with_thin_prose(stats: _TextStats) -> bool:
    """URL path is just a section hub (e.g. `/politics/`) and the prose is
    sparsely punctuated the way a list of links/headlines is."""
    return stats.hub_url and stats.punct_per_100_words < LOW_PUNCT_PER_100_WORDS


def _unpunctuated_link_list(stats: _TextStats) -> bool:
    """Long enough to be a real page, but almost no sentence-ending
    punctuation anywhere — reads as a list of links, not prose."""
    return (
        stats.word_count >= MIN_WORDS_FOR_LENGTH_SIGNALS
        and stats.punct_per_100_words < VERY_LOW_PUNCT_PER_100_WORDS
    )


def _headline_list(stats: _TextStats) -> bool:
    """Long enough, mostly Title Case, and light on punctuation — reads as
    a stacked list of headlines rather than an article."""
    return (
        stats.word_count >= MIN_WORDS_FOR_LENGTH_SIGNALS
        and stats.titlecase_share >= HIGH_TITLECASE_SHARE
        and stats.punct_per_100_words < LOW_PUNCT_PER_100_WORDS
    )


def _nav_chrome_heavy(stats: _TextStats) -> bool:
    """Enough site-chrome phrases (nav labels, cookie/privacy boilerplate)
    to indicate a template shell rather than an article. The bar is lower
    once the URL is already hub-shaped."""
    return stats.chrome_hits >= NAV_CHROME_HITS_GENERIC or (
        stats.hub_url and stats.chrome_hits >= NAV_CHROME_HITS_HUB
    )


def is_index_page(text: str, url: str = "") -> bool:
    """Detect a section-index / hub page masquerading as an article."""
    stats = _compute_text_stats(text, url)
    if stats.word_count == 0:
        return False
    return (
        _hub_url_with_thin_prose(stats)
        or _unpunctuated_link_list(stats)
        or _headline_list(stats)
        or _nav_chrome_heavy(stats)
    )


def stamp_published_at(
    published_at: Optional[datetime.datetime], now: datetime.datetime
) -> datetime.datetime:
    """NULL published_at is stamped with ETL time (`now`) rather than left
    NULL — published_at is NOT NULL and every window filter is
    `published_at >= cutoff`, so a NULL would never surface the doc."""
    return now if published_at is None else published_at


def is_recent(
    published_at: Optional[datetime.datetime],
    now: Optional[datetime.datetime] = None,
    max_age: datetime.timedelta = THIRTY_DAYS,
) -> bool:
    """Check if content was published within the allowed time window.

    NULL published_at passes (stamped with ETL time at insert instead).
    Genuinely-invalid dates (pre-2020 / more than a day in the future) are
    still rejected.
    """
    if published_at is None:
        return True
    now = now or datetime.datetime.now(datetime.timezone.utc)
    if published_at < MIN_VALID_PUBLISHED_AT:
        return False
    if published_at > now + FUTURE_SLOP:
        return False
    return (now - published_at) <= max_age


# ---------------------------------------------------------------------------
# domain_filter (deny/allow/cap) — data/seeds.yaml section
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class DomainFilterConfig:
    """Parsed `domain_filter` section of data/seeds.yaml. All three keys are
    optional; absent = unfiltered/uncapped."""

    deny: frozenset[str] = field(default_factory=frozenset)
    allow: frozenset[str] = field(default_factory=frozenset)
    max_docs_per_domain_per_window: int = 0  # 0 = uncapped


def load_domain_filter_config(seeds_path: Optional[Path] = None) -> DomainFilterConfig:
    """Load the additive `domain_filter` section of data/seeds.yaml.

    domain_filter:
      deny: [www.comparecards.com]   # always rejected, before content filtering
      allow: [example.com]           # bypasses is_us_political_content, NOT recency
      max_docs_per_domain_per_window: 50   # 0/absent = uncapped

    A missing file or missing section returns the all-defaults config
    rather than raising.
    """
    path = seeds_path or _DEFAULT_SEEDS_PATH
    if not path.exists():
        return DomainFilterConfig()
    data = yaml.safe_load(path.read_text()) or {}
    section = data.get("domain_filter") or {}
    deny = frozenset(str(d).lower() for d in (section.get("deny") or []))
    allow = frozenset(str(d).lower() for d in (section.get("allow") or []))
    cap = int(section.get("max_docs_per_domain_per_window") or 0)
    return DomainFilterConfig(deny=deny, allow=allow, max_docs_per_domain_per_window=cap)


def select_within_domain_cap(
    candidates: list[dict],
    existing_counts: dict[str, int],
    cap: int,
) -> tuple[list[dict], int]:
    """Enforce `cap` as a ceiling on TOTAL rows per domain_key within the
    current window (existing_counts[domain_key] + newly admitted), not a
    per-run ceiling. Within a domain, admits the highest-`cap` remaining
    slots by published_at descending; published_at=None sorts as newest
    (stamp_published_at will set it to ETL time = now). cap<=0 is uncapped:
    returns `candidates` unchanged and 0 skipped.

    Returns (admitted_candidates, skipped_count).
    """
    if cap <= 0:
        return candidates, 0

    by_domain: dict[str, list[dict]] = {}
    for c in candidates:
        by_domain.setdefault(c["domain_key"], []).append(c)

    admitted: list[dict] = []
    skipped = 0
    for domain_key, rows in by_domain.items():
        remaining = cap - existing_counts.get(domain_key, 0)
        if remaining <= 0:
            skipped += len(rows)
            continue
        rows_sorted = sorted(
            rows,
            key=lambda r: r["published_at"] or datetime.datetime.max.replace(tzinfo=datetime.timezone.utc),
            reverse=True,
        )
        admitted.extend(rows_sorted[:remaining])
        skipped += max(0, len(rows_sorted) - remaining)
    return admitted, skipped


# ---------------------------------------------------------------------------
# Text extraction (news) / source URL construction (reddit, x)
# ---------------------------------------------------------------------------

def _extract_text_from_raw(raw_hash: Optional[str], raw_root: Path) -> Optional[str]:
    """Extract article text from a content-addressed raw HTML file."""
    if not raw_hash or len(raw_hash) <= 2:
        return None
    path = raw_root / raw_hash[:2] / f"{raw_hash}.html"
    if not path.exists():
        return None
    try:
        html_content = path.read_text(encoding="utf-8", errors="ignore")
        return trafilatura.extract(html_content)
    except (OSError, UnicodeError) as e:
        logger.warning(f"Failed to extract text for {raw_hash}: {e}")
        return None


def _reddit_post_id(fullname: str) -> str:
    """Strip the t3_/t1_ Reddit fullname prefix."""
    return fullname.replace("t3_", "").replace("t1_", "")


def _build_reddit_source_url(fullname: str, subreddit: Optional[str]) -> str:
    return f"https://reddit.com/r/{subreddit or 'all'}/comments/{_reddit_post_id(fullname)}"


def _build_x_source_url(tweet_id: str) -> str:
    """Handle-independent status URL — doesn't depend on author-sync ordering."""
    return f"https://x.com/i/web/status/{tweet_id}"


# ---------------------------------------------------------------------------
# Admission gate — shared predicates + per-source verdict functions.
#
# News, reddit, and x apply different check subsets (see each admit_*
# docstring), so this is per-source functions composing shared predicates
# rather than one function with source flags:
#   - news checks deny, recency, index-page, and political.
#   - reddit checks deny, recency, and political (no index-page: reddit
#     bodies are user text, not scraped HTML that can look like a nav hub).
#   - x checks recency and political only; its deny check is a single
#     up-front lookup by the caller before any row is even queried (see
#     _gather_x_candidates), so a denied X ingestion increments no counter
#     — that is current behavior, preserved as-is.
# News additionally splits into a pretext gate (deny, recency — cheap,
# no I/O) and a posttext gate (index-page, political — needs the
# extracted article text), because text extraction is a disk read that
# must happen strictly between those two check groups: it should not run
# for a row already rejected on domain/recency, and it must run before the
# content checks. Reddit/X build their candidate text from already-fetched
# row data (no I/O), so each gets a single admit function.
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class AdmissionVerdict:
    admitted: bool
    reason: str


_ADMITTED = AdmissionVerdict(True, ADMITTED)


def _check_domain_denied(domain_key: str, cfg: DomainFilterConfig) -> Optional[AdmissionVerdict]:
    if domain_key in cfg.deny:
        return AdmissionVerdict(False, DENIED_DOMAIN)
    return None


def _check_stale(published_at: Optional[datetime.datetime], now: datetime.datetime) -> Optional[AdmissionVerdict]:
    if not is_recent(published_at, now):
        return AdmissionVerdict(False, STALE)
    return None


def _check_index_page(text: str, url: str) -> Optional[AdmissionVerdict]:
    if is_index_page(text, url):
        return AdmissionVerdict(False, INDEX_PAGE)
    return None


def _check_nonpolitical(
    text: str, title: str, url: str, domain_key: str, cfg: DomainFilterConfig
) -> Optional[AdmissionVerdict]:
    if domain_key in cfg.allow:
        return None
    if not is_us_political_content(text, title, url):
        return AdmissionVerdict(False, NOT_POLITICAL)
    return None


def _admit_news_pretext(
    domain_key: str, published_at: Optional[datetime.datetime],
    cfg: DomainFilterConfig, now: datetime.datetime,
) -> AdmissionVerdict:
    """News, phase 1 (before text extraction): deny -> recency."""
    return _check_domain_denied(domain_key, cfg) or _check_stale(published_at, now) or _ADMITTED


def _admit_news_posttext(
    text: str, title: Optional[str], url: str, domain_key: str, cfg: DomainFilterConfig,
) -> AdmissionVerdict:
    """News, phase 2 (after text extraction succeeds): index-page -> political."""
    return (
        _check_index_page(text, url)
        or _check_nonpolitical(text, title or "", url, domain_key, cfg)
        or _ADMITTED
    )


def _admit_reddit(
    title: Optional[str], text: str, domain_key: str,
    created_utc: Optional[datetime.datetime], cfg: DomainFilterConfig, now: datetime.datetime,
) -> AdmissionVerdict:
    """Reddit: deny -> recency -> political."""
    return (
        _check_domain_denied(domain_key, cfg)
        or _check_stale(created_utc, now)
        or _check_nonpolitical(text, title or "", "", domain_key, cfg)
        or _ADMITTED
    )


def _admit_x(
    text: str, created_at: Optional[datetime.datetime], cfg: DomainFilterConfig, now: datetime.datetime,
) -> AdmissionVerdict:
    """X: recency -> political. No deny check (see module-level note above)."""
    return (
        _check_stale(created_at, now)
        or _check_nonpolitical(text, "", "", X_DOMAIN_KEY, cfg)
        or _ADMITTED
    )


# ---------------------------------------------------------------------------
# Load result + shared per-domain-count helper
# ---------------------------------------------------------------------------

@dataclass
class DocLoadResult:
    inserted: int = 0
    skipped_old: int = 0
    skipped_nonpolitical: int = 0
    skipped_index: int = 0
    skipped_denied: int = 0
    skipped_capped: int = 0
    rejections: dict[str, int] = field(default_factory=dict)

    def record_rejection(self, reason: str) -> None:
        """Tally an admit() rejection: increments the matching named
        counter (unchanged, existing behavior) and the reason-keyed
        `rejections` tally (observability addition)."""
        if reason == DENIED_DOMAIN:
            self.skipped_denied += 1
        elif reason == STALE:
            self.skipped_old += 1
        elif reason == INDEX_PAGE:
            self.skipped_index += 1
        elif reason == NOT_POLITICAL:
            self.skipped_nonpolitical += 1
        self.rejections[reason] = self.rejections.get(reason, 0) + 1

    def __add__(self, other: "DocLoadResult") -> "DocLoadResult":
        merged_rejections = dict(self.rejections)
        for reason, count in other.rejections.items():
            merged_rejections[reason] = merged_rejections.get(reason, 0) + count
        return DocLoadResult(
            inserted=self.inserted + other.inserted,
            skipped_old=self.skipped_old + other.skipped_old,
            skipped_nonpolitical=self.skipped_nonpolitical + other.skipped_nonpolitical,
            skipped_index=self.skipped_index + other.skipped_index,
            skipped_denied=self.skipped_denied + other.skipped_denied,
            skipped_capped=self.skipped_capped + other.skipped_capped,
            rejections=merged_rejections,
        )


def _existing_domain_counts(
    conn: Connection, source_type: str, domain_keys: set[str], now: datetime.datetime
) -> dict[str, int]:
    """Count corpus.documents rows already in the 30-day window per
    lower(domain_or_subreddit), for the cap check."""
    if not domain_keys:
        return {}
    window_start = now - THIRTY_DAYS
    with conn.cursor() as cur:
        cur.execute(
            """
            SELECT lower(domain_or_subreddit) AS domain_key, COUNT(*) AS n
            FROM corpus.documents
            WHERE source_type = %s AND published_at >= %s
              AND lower(domain_or_subreddit) = ANY(%s)
            GROUP BY lower(domain_or_subreddit)
            """,
            (source_type, window_start, list(domain_keys)),
        )
        return {row["domain_key"]: row["n"] for row in cur.fetchall()}


# ---------------------------------------------------------------------------
# Entity FK resolution — corpus.news_articles.outlet_entity_id /
# corpus.reddit_posts.subreddit_entity_id, backfilled post-insert so an
# outlet/subreddit curated into corpus.entities later still gets picked up.
# ---------------------------------------------------------------------------

def _load_entity_lookup(conn: Connection, kind: str) -> dict[str, int]:
    """Build {canonicalized entity_key or alias: entity_id} for every active
    corpus.entities row of `kind`."""
    with conn.cursor() as cur:
        cur.execute(
            "SELECT entity_id, entity_key FROM corpus.entities "
            "WHERE kind = %s::corpus.entity_kind AND active = true",
            (kind,),
        )
        rows = cur.fetchall()
    lookup = {row["entity_key"]: row["entity_id"] for row in rows}
    entity_ids = [row["entity_id"] for row in rows]
    if entity_ids:
        with conn.cursor() as cur:
            cur.execute(
                "SELECT entity_id, alias FROM corpus.entity_aliases WHERE entity_id = ANY(%s)",
                (entity_ids,),
            )
            for row in cur.fetchall():
                lookup[row["alias"]] = row["entity_id"]
    return lookup


def _backfill_outlet_entity_links(conn: Connection) -> int:
    """Resolve corpus.news_articles.outlet_entity_id for every row still
    NULL, matching the canonicalized `domain` against an active
    kind='outlet' entity/alias. Never overwrites an already-resolved row;
    stays NULL when unmatched. Returns the number of rows resolved."""
    entity_map = _load_entity_lookup(conn, "outlet")
    if not entity_map:
        return 0
    updated = 0
    with conn.cursor() as cur:
        cur.execute("SELECT doc_id, domain FROM corpus.news_articles WHERE outlet_entity_id IS NULL")
        rows = cur.fetchall()
        for row in rows:
            entity_id = entity_map.get(canonicalize_news_domain(row["domain"]))
            if entity_id is None:
                continue
            cur.execute(
                "UPDATE corpus.news_articles SET outlet_entity_id = %s WHERE doc_id = %s",
                (entity_id, row["doc_id"]),
            )
            updated += 1
    return updated


def _backfill_subreddit_entity_links(conn: Connection) -> int:
    """Same contract as `_backfill_outlet_entity_links`, for
    corpus.reddit_posts.subreddit_entity_id / kind='subreddit'."""
    entity_map = _load_entity_lookup(conn, "subreddit")
    if not entity_map:
        return 0
    updated = 0
    with conn.cursor() as cur:
        cur.execute("SELECT doc_id, subreddit FROM corpus.reddit_posts WHERE subreddit_entity_id IS NULL")
        rows = cur.fetchall()
        for row in rows:
            entity_id = entity_map.get(canonicalize_subreddit(row["subreddit"]))
            if entity_id is None:
                continue
            cur.execute(
                "UPDATE corpus.reddit_posts SET subreddit_entity_id = %s WHERE doc_id = %s",
                (entity_id, row["doc_id"]),
            )
            updated += 1
    return updated


# ---------------------------------------------------------------------------
# News
# ---------------------------------------------------------------------------

@dataclass
class _NewsCandidate:
    url_canon: str
    domain: Optional[str]
    raw_hash: str
    title: Optional[str]
    published_at: Optional[datetime.datetime]
    extraction_version: Optional[str]
    text: str
    domain_key: str


def _gather_news_candidates(
    conn: Connection, cfg: DomainFilterConfig, raw_root: Path, now: datetime.datetime
) -> tuple[list[_NewsCandidate], DocLoadResult]:
    result = DocLoadResult()
    candidates: list[_NewsCandidate] = []
    with conn.cursor() as cur:
        cur.execute(
            """
            SELECT a.url_canon, a.domain, a.raw_hash, a.title, a.published_at,
                   a.extraction_version
            FROM raw.articles a
            WHERE NOT EXISTS (
                SELECT 1 FROM corpus.documents d
                WHERE d.source_type = 'news' AND d.natural_key = a.url_canon
            )
            """
        )
        rows = cur.fetchall()

    for row in rows:
        domain_key = (row["domain"] or "").lower()
        pretext_verdict = _admit_news_pretext(domain_key, row["published_at"], cfg, now)
        if not pretext_verdict.admitted:
            result.record_rejection(pretext_verdict.reason)
            continue

        text = _extract_text_from_raw(row["raw_hash"], raw_root)
        if not text:
            continue  # extraction failure — not a policy rejection, not counted

        posttext_verdict = _admit_news_posttext(text, row["title"], row["url_canon"], domain_key, cfg)
        if not posttext_verdict.admitted:
            result.record_rejection(posttext_verdict.reason)
            continue

        candidates.append(_NewsCandidate(
            url_canon=row["url_canon"], domain=row["domain"], raw_hash=row["raw_hash"],
            title=row["title"], published_at=row["published_at"],
            extraction_version=row["extraction_version"], text=text, domain_key=domain_key,
        ))
    return candidates, result


def _insert_news_candidates(
    conn: Connection, candidates: list[_NewsCandidate], cfg: DomainFilterConfig, now: datetime.datetime
) -> tuple[int, int]:
    domain_keys = {c.domain_key for c in candidates}
    existing = (
        _existing_domain_counts(conn, "news", domain_keys, now)
        if cfg.max_docs_per_domain_per_window else {}
    )
    admitted, capped = select_within_domain_cap(
        [{"domain_key": c.domain_key, "published_at": c.published_at, "candidate": c} for c in candidates],
        existing, cfg.max_docs_per_domain_per_window,
    )

    inserted = 0
    with conn.cursor() as cur:
        for row in admitted:
            c: _NewsCandidate = row["candidate"]
            published = stamp_published_at(c.published_at, now)
            cur.execute(
                """
                INSERT INTO corpus.documents
                    (source_type, natural_key, domain_or_subreddit, author_id,
                     published_at, title, body, source_url, raw_hash, etl_version)
                VALUES ('news', %s, %s, NULL, %s, %s, %s, %s, %s, %s)
                ON CONFLICT (source_type, natural_key) DO NOTHING
                RETURNING doc_id
                """,
                (c.url_canon, c.domain, published, c.title, c.text, c.url_canon, c.raw_hash, ETL_VERSION),
            )
            doc_row = cur.fetchone()
            if doc_row is None:
                continue
            cur.execute(
                """
                INSERT INTO corpus.news_articles (doc_id, url_canon, domain, extraction_version)
                VALUES (%s, %s, %s, %s)
                ON CONFLICT (doc_id) DO NOTHING
                """,
                (doc_row["doc_id"], c.url_canon, c.domain, c.extraction_version),
            )
            inserted += 1
    return inserted, capped


def _load_news(conn: Connection, cfg: DomainFilterConfig, raw_root: Path, now: datetime.datetime) -> DocLoadResult:
    candidates, result = _gather_news_candidates(conn, cfg, raw_root, now)
    result.inserted, result.skipped_capped = _insert_news_candidates(conn, candidates, cfg, now)
    _backfill_outlet_entity_links(conn)
    return result


# ---------------------------------------------------------------------------
# Reddit
# ---------------------------------------------------------------------------

@dataclass
class _RedditCandidate:
    fullname: str
    subreddit: Optional[str]
    created_utc: Optional[datetime.datetime]
    title: Optional[str]
    text: str
    raw_hash: str
    score: Optional[int]
    num_comments: Optional[int]
    domain_key: str


def _gather_reddit_candidates(
    conn: Connection, cfg: DomainFilterConfig, now: datetime.datetime
) -> tuple[list[_RedditCandidate], DocLoadResult]:
    result = DocLoadResult()
    candidates: list[_RedditCandidate] = []
    with conn.cursor() as cur:
        cur.execute(
            """
            SELECT r.fullname, r.subreddit, r.created_utc, r.title, r.body,
                   r.raw_hash, r.score, r.num_comments
            FROM raw.reddit_posts r
            WHERE NOT EXISTS (
                SELECT 1 FROM corpus.documents d
                WHERE d.source_type = 'reddit_post' AND d.natural_key = r.fullname
            )
            """
        )
        rows = cur.fetchall()

    for row in rows:
        domain_key = (row["subreddit"] or "").lower()
        text = f"{row['title'] or ''}\n\n{row['body'] or ''}".strip()
        verdict = _admit_reddit(row["title"], text, domain_key, row["created_utc"], cfg, now)
        if not verdict.admitted:
            result.record_rejection(verdict.reason)
            continue
        candidates.append(_RedditCandidate(
            fullname=row["fullname"], subreddit=row["subreddit"], created_utc=row["created_utc"],
            title=row["title"], text=text, raw_hash=row["raw_hash"], score=row["score"],
            num_comments=row["num_comments"], domain_key=domain_key,
        ))
    return candidates, result


def _insert_reddit_candidates(
    conn: Connection, candidates: list[_RedditCandidate], cfg: DomainFilterConfig, now: datetime.datetime
) -> tuple[int, int]:
    domain_keys = {c.domain_key for c in candidates}
    existing = (
        _existing_domain_counts(conn, "reddit_post", domain_keys, now)
        if cfg.max_docs_per_domain_per_window else {}
    )
    admitted, capped = select_within_domain_cap(
        [{"domain_key": c.domain_key, "published_at": c.created_utc, "candidate": c} for c in candidates],
        existing, cfg.max_docs_per_domain_per_window,
    )

    inserted = 0
    with conn.cursor() as cur:
        for row in admitted:
            c: _RedditCandidate = row["candidate"]
            published = stamp_published_at(c.created_utc, now)
            source_url = _build_reddit_source_url(c.fullname, c.subreddit)
            cur.execute(
                """
                INSERT INTO corpus.documents
                    (source_type, natural_key, domain_or_subreddit, author_id,
                     published_at, title, body, source_url, raw_hash, etl_version)
                VALUES ('reddit_post', %s, %s, NULL, %s, %s, %s, %s, %s, %s)
                ON CONFLICT (source_type, natural_key) DO NOTHING
                RETURNING doc_id
                """,
                (c.fullname, c.subreddit, published, c.title, c.text, source_url, c.raw_hash, ETL_VERSION),
            )
            doc_row = cur.fetchone()
            if doc_row is None:
                continue
            cur.execute(
                """
                INSERT INTO corpus.reddit_posts (doc_id, fullname, subreddit, score, num_comments)
                VALUES (%s, %s, %s, %s, %s)
                ON CONFLICT (doc_id) DO NOTHING
                """,
                (doc_row["doc_id"], c.fullname, c.subreddit, c.score, c.num_comments),
            )
            inserted += 1
    return inserted, capped


def _load_reddit(conn: Connection, cfg: DomainFilterConfig, now: datetime.datetime) -> DocLoadResult:
    candidates, result = _gather_reddit_candidates(conn, cfg, now)
    result.inserted, result.skipped_capped = _insert_reddit_candidates(conn, candidates, cfg, now)
    _backfill_subreddit_entity_links(conn)
    return result


# ---------------------------------------------------------------------------
# X
# ---------------------------------------------------------------------------

@dataclass
class _XCandidate:
    tweet_id: str
    author_platform_id: str
    created_at: datetime.datetime
    text: str
    lang: Optional[str]
    place_country_code: Optional[str]
    raw_hash: str
    conversation_id: Optional[str]
    retweet_count: Optional[int]
    reply_count: Optional[int]
    like_count: Optional[int]
    quote_count: Optional[int]
    is_official_tier: Optional[bool]
    referenced_tweet_id: Optional[str]
    referenced_tweet_type: Optional[str]


def _gather_x_candidates(
    conn: Connection, cfg: DomainFilterConfig, now: datetime.datetime
) -> tuple[list[_XCandidate], DocLoadResult]:
    result = DocLoadResult()
    candidates: list[_XCandidate] = []

    if X_DOMAIN_KEY in cfg.deny:
        return candidates, result

    with conn.cursor() as cur:
        cur.execute(
            """
            SELECT p.tweet_id, p.author_id, p.created_at, p.text, p.lang,
                   p.place_country_code, p.raw_hash, p.conversation_id,
                   p.retweet_count, p.reply_count, p.like_count, p.quote_count,
                   p.is_official_tier, p.referenced_tweet_id, p.referenced_tweet_type
            FROM raw.x_posts p
            WHERE NOT EXISTS (
                SELECT 1 FROM corpus.documents d
                WHERE d.source_type = 'x_post' AND d.natural_key = p.tweet_id
            )
            """
        )
        rows = cur.fetchall()

    for row in rows:
        verdict = _admit_x(row["text"], row["created_at"], cfg, now)
        if not verdict.admitted:
            result.record_rejection(verdict.reason)
            continue
        candidates.append(_XCandidate(
            tweet_id=row["tweet_id"], author_platform_id=row["author_id"], created_at=row["created_at"],
            text=row["text"], lang=row["lang"], place_country_code=row["place_country_code"],
            raw_hash=row["raw_hash"], conversation_id=row["conversation_id"],
            retweet_count=row["retweet_count"], reply_count=row["reply_count"],
            like_count=row["like_count"], quote_count=row["quote_count"],
            is_official_tier=row["is_official_tier"],
            referenced_tweet_id=row["referenced_tweet_id"],
            referenced_tweet_type=row["referenced_tweet_type"],
        ))
    return candidates, result


def _resolve_x_author_ids(conn: Connection, platform_author_ids: set[str]) -> dict[str, int]:
    """Read-only lookup against corpus.authors; an unsynced author yields
    no entry (caller treats as NULL)."""
    if not platform_author_ids:
        return {}
    with conn.cursor() as cur:
        cur.execute(
            "SELECT platform_author_id, author_id FROM corpus.authors "
            "WHERE platform = 'x' AND platform_author_id = ANY(%s)",
            (list(platform_author_ids),),
        )
        return {row["platform_author_id"]: row["author_id"] for row in cur.fetchall()}


def _insert_x_candidates(
    conn: Connection, candidates: list[_XCandidate], cfg: DomainFilterConfig, now: datetime.datetime
) -> tuple[int, int]:
    cap = cfg.max_docs_per_domain_per_window
    existing = _existing_domain_counts(conn, "x_post", {X_DOMAIN_KEY}, now) if cap else {}
    admitted, capped = select_within_domain_cap(
        [{"domain_key": X_DOMAIN_KEY, "published_at": c.created_at, "candidate": c} for c in candidates],
        existing, cap,
    )
    author_map = _resolve_x_author_ids(conn, {row["candidate"].author_platform_id for row in admitted})

    inserted = 0
    with conn.cursor() as cur:
        for row in admitted:
            c: _XCandidate = row["candidate"]
            author_id = author_map.get(c.author_platform_id)
            source_url = _build_x_source_url(c.tweet_id)
            cur.execute(
                """
                INSERT INTO corpus.documents
                    (source_type, natural_key, domain_or_subreddit, author_id,
                     published_at, title, body, source_url, raw_hash, etl_version)
                VALUES ('x_post', %s, %s, %s, %s, NULL, %s, %s, %s, %s)
                ON CONFLICT (source_type, natural_key) DO NOTHING
                RETURNING doc_id
                """,
                (c.tweet_id, X_DOMAIN_KEY, author_id, c.created_at, c.text, source_url, c.raw_hash, ETL_VERSION),
            )
            doc_row = cur.fetchone()
            if doc_row is None:
                continue
            cur.execute(
                """
                INSERT INTO corpus.x_posts
                    (doc_id, tweet_id, conversation_id, lang, place_country_code,
                     retweet_count, reply_count, like_count, quote_count, is_official_tier,
                     referenced_tweet_id, referenced_tweet_type)
                VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                ON CONFLICT (doc_id) DO NOTHING
                """,
                (doc_row["doc_id"], c.tweet_id, c.conversation_id, c.lang, c.place_country_code,
                 c.retweet_count, c.reply_count, c.like_count, c.quote_count, c.is_official_tier,
                 c.referenced_tweet_id, c.referenced_tweet_type),
            )
            inserted += 1
    return inserted, capped


def _load_x(conn: Connection, cfg: DomainFilterConfig, now: datetime.datetime) -> DocLoadResult:
    candidates, result = _gather_x_candidates(conn, cfg, now)
    result.inserted, result.skipped_capped = _insert_x_candidates(conn, candidates, cfg, now)
    return result


# ---------------------------------------------------------------------------
# Public entry point
# ---------------------------------------------------------------------------

def load_new_documents(
    seeds_path: Optional[Path] = None, raw_root: Optional[Path] = None
) -> DocLoadResult:
    """Normalize raw.* into corpus.documents + subtype tables. One committed
    transaction per source type (news, then reddit, then x); a failure in
    one does not roll back an earlier one's committed gains.

    Idempotent: ON CONFLICT DO NOTHING on corpus.documents (source_type,
    natural_key) and on every subtype table (doc_id).
    """
    cfg = load_domain_filter_config(seeds_path)
    root = raw_root or _DEFAULT_RAW_ROOT
    now = datetime.datetime.now(datetime.timezone.utc)

    with db.connection() as conn:
        news_result = _load_news(conn, cfg, root, now)
    with db.connection() as conn:
        reddit_result = _load_reddit(conn, cfg, now)
    with db.connection() as conn:
        x_result = _load_x(conn, cfg, now)

    total = news_result + reddit_result + x_result
    logger.info(
        f"ETL [{ETL_VERSION}] inserted={total.inserted} "
        f"skipped_old={total.skipped_old} skipped_nonpolitical={total.skipped_nonpolitical} "
        f"skipped_index={total.skipped_index} skipped_denied={total.skipped_denied} "
        f"skipped_capped={total.skipped_capped} rejections={total.rejections}"
    )
    return total
