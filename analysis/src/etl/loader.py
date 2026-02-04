from typing import List, Dict, Any, Optional
import sqlite3
import json
import time
import re
from pathlib import Path
import trafilatura
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)

# Only include content related to US politics
# Matches both federal and state-level political topics
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

# Patterns to exclude (non-political content)
EXCLUDE_PATTERNS = [
    r"/sport", r"/football", r"/basketball", r"/baseball", r"/soccer",
    r"/music", r"/entertainment", r"/celebrity", r"/podcast",
    r"/recipes", r"/food", r"/travel", r"/lifestyle",
]

# 30 days in seconds
THIRTY_DAYS_SECONDS = 30 * 24 * 60 * 60


def is_us_political_content(text: str, title: str = "", url: str = "") -> bool:
    """Check if content is related to US politics."""
    if not text and not title:
        return False
    
    # Check for exclusion patterns in URL
    url_lower = url.lower()
    for pattern in EXCLUDE_PATTERNS:
        if re.search(pattern, url_lower):
            return False
    
    # Combine text and title for keyword search
    combined = f"{title} {text}".lower()
    
    # Check for political keywords
    for keyword in US_POLITICAL_KEYWORDS:
        if keyword in combined:
            return True
    
    return False


def is_recent(published_at: Optional[int], max_age_seconds: int = THIRTY_DAYS_SECONDS) -> bool:
    """Check if content was published within the allowed time window."""
    if published_at is None or published_at == 0:
        # If no publish date, assume it's recent (to avoid filtering valid content)
        return True
    
    now = int(time.time())
    age = now - published_at
    
    # Also reject obviously invalid dates (before 2020 or in the future)
    if published_at < 1577836800:  # Jan 1, 2020
        return False
    if published_at > now + 86400:  # More than 1 day in future
        return False
    
    return age <= max_age_seconds


class ContentLoader:
    def __init__(self, db_path: str):
        self.db_path = db_path

    def _get_conn(self):
        return sqlite3.connect(self.db_path)

    def load_new_raw_content(self) -> int:
        """
        Reads raw tables, normalizes, and inserts into 'docs' if not present.
        Applies filters:
        - Only content from last 30 days
        - Only US political content
        Returns count of new docs.
        """
        conn = self._get_conn()
        cursor = conn.cursor()
        
        # Cleanup empty text docs to allow reprocessing
        cursor.execute("DELETE FROM docs WHERE source_type='news' AND text IS NULL")
        
        # Calculate raw directory path relative to DB
        # db_path: data/civic_lens.db -> raw_root: data/raw/sha256
        raw_root = Path(self.db_path).parent / "raw" / "sha256"
        
        # 1. Load News
        cursor.execute("""
            SELECT url_canon, domain, raw_hash, title, published_at 
            FROM articles_raw
        """)
        articles = cursor.fetchall()
        
        new_docs = 0
        skipped_old = 0
        skipped_nonpolitical = 0
        
        for row in articles:
            url_canon, domain, raw_hash, title, published_at = row
            
            # Check if exists
            cursor.execute("SELECT doc_id FROM docs WHERE ident = ?", (url_canon,))
            if cursor.fetchone():
                continue
            
            # Filter: 30-day recency check
            if not is_recent(published_at):
                skipped_old += 1
                continue
            
            text = None
            
            # Extract text from raw HTML file
            if raw_hash and len(raw_hash) > 2:
                prefix = raw_hash[:2]
                path = raw_root / prefix / f"{raw_hash}.html"
                if path.exists():
                    try:
                        with open(path, 'r', encoding='utf-8', errors='ignore') as f:
                            html = f.read()
                        text = trafilatura.extract(html)
                    except Exception as e:
                        logger.warning(f"Failed to extract text for {raw_hash}: {e}")
            
            if not text:
                continue
            
            # Filter: US political content only
            if not is_us_political_content(text, title or "", url_canon):
                skipped_nonpolitical += 1
                continue
                
            # Insert
            cursor.execute("""
                INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, title, raw_hash, text)
                VALUES (?, ?, ?, ?, ?, ?, ?)
            """, ("news", url_canon, domain, published_at, title, raw_hash, text))
            new_docs += 1
            
        # 2. Load Reddit Posts
        cursor.execute("""
            SELECT fullname, subreddit, created_utc, title, body, raw_hash
            FROM reddit_posts_raw
        """)
        posts = cursor.fetchall()
        for row in posts:
            fullname, subreddit, created_utc, title, body, raw_hash = row
            
            if not fullname:
                continue
                
            cursor.execute("SELECT doc_id FROM docs WHERE ident = ?", (fullname,))
            if cursor.fetchone():
                continue
            
            # Filter: 30-day recency check
            if not is_recent(created_utc):
                skipped_old += 1
                continue
            
            # Combine title + body for text
            text = f"{title or ''}\n\n{body or ''}".strip()
            
            # Filter: US political content only
            if not is_us_political_content(text, title or ""):
                skipped_nonpolitical += 1
                continue
            
            cursor.execute("""
                INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, title, text, raw_hash)
                VALUES (?, ?, ?, ?, ?, ?, ?)
            """, ("reddit_post", fullname, subreddit, created_utc, title, text, raw_hash))
            new_docs += 1
        
        # 3. Load X Posts
        cursor.execute("""
            SELECT p.tweet_id, p.author_id, p.created_at, p.text, p.lang,
                   p.place_country_code, p.raw_hash,
                   u.location, u.created_at as user_created_at,
                   u.followers_count, u.verified, u.verified_type
            FROM x_posts_raw p
            LEFT JOIN x_users_raw u ON p.author_id = u.user_id
        """)
        x_posts = cursor.fetchall()
        for row in x_posts:
            (tweet_id, author_id, created_at, text, lang, 
             place_country_code, raw_hash,
             user_location, user_created_at, 
             followers_count, verified, verified_type) = row
            
            if not tweet_id:
                continue
            
            cursor.execute("SELECT doc_id FROM docs WHERE ident = ?", (tweet_id,))
            if cursor.fetchone():
                continue
            
            # Filter: 30-day recency check
            if not is_recent(created_at):
                skipped_old += 1
                continue
            
            # Filter: US political content only
            if not is_us_political_content(text, ""):
                skipped_nonpolitical += 1
                continue
            
            # Build metadata for bot/origin detection
            metadata = {
                "platform": "x",
                "lang": lang,
                "author_id": author_id,
                "place_country_code": place_country_code,
                "user_location": user_location,
                "user_created_at": user_created_at,
                "user_followers": followers_count,
                "user_verified": bool(verified),
                "user_verified_type": verified_type,
            }
            
            cursor.execute("""
                INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, text, raw_hash, metadata_json)
                VALUES (?, ?, ?, ?, ?, ?, ?)
            """, ("x_post", tweet_id, "x.com", created_at, text, raw_hash, json.dumps(metadata)))
            new_docs += 1
            
        conn.commit()
        logger.info(f"ETL Loaded {new_docs} new documents. Skipped: {skipped_old} old, {skipped_nonpolitical} non-political.")
        return new_docs

    def get_unprocessed_docs(self, task_type: str) -> List[Dict[str, Any]]:
        """
        Returns docs that do not have an entry in ai_outputs for the given task.
        Increased batch size for better throughput.
        """
        conn = self._get_conn()
        cursor = conn.cursor()
        
        # Increased from 100 to 500 for better coverage
        # Safe because heuristics are used when LLM is disabled (free).
        # When LLM is enabled, Gemini Flash free tier has generous limits.
        query = f"""
            SELECT d.doc_id, d.text, d.metadata_json, d.title
            FROM docs d
            LEFT JOIN ai_outputs a ON d.doc_id = a.doc_id AND a.task_type = ?
            WHERE a.output_id IS NULL AND d.text IS NOT NULL
            LIMIT 500
        """
        cursor.execute(query, (task_type,))
        rows = cursor.fetchall()
        
        return [{"doc_id": r[0], "text": r[1], "metadata": json.loads(r[2]) if r[2] else {}, "title": r[3]} for r in rows]

    def save_ai_output(self, doc_id: int, task: str, result: Dict[str, Any], confidence: float):
        conn = self._get_conn()
        cursor = conn.cursor()
        cursor.execute("""
            INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, created_at)
            VALUES (?, ?, ?, ?, strftime('%s','now'))
        """, (doc_id, task, json.dumps(result), confidence))
        conn.commit()

    def get_all_docs_for_clustering(self, max_age_days: int = 90) -> List[Dict[str, Any]]:
        """
        Get docs for clustering with time and content filtering.
        
        Args:
            max_age_days: Only include docs published within this many days (default 90)
        
        Filters:
            - Excludes sports/entertainment URLs via pattern matching
            - Only includes docs within time window
        """
        cutoff = int(time.time()) - (max_age_days * 86400)
        
        conn = self._get_conn()
        cursor = conn.cursor()
        cursor.execute("""
            SELECT doc_id, title, text FROM docs 
            WHERE text IS NOT NULL AND text != ''
            AND (published_at IS NULL OR published_at >= ?)
            AND ident NOT LIKE '%/sport%'
            AND ident NOT LIKE '%/football%'
            AND ident NOT LIKE '%/basketball%'
            AND ident NOT LIKE '%/baseball%'
            AND ident NOT LIKE '%/soccer%'
            AND ident NOT LIKE '%/entertainment%'
            AND ident NOT LIKE '%/music%'
            AND ident NOT LIKE '%/celebrity%'
        """, (cutoff,))
        rows = cursor.fetchall()
        logger.info(f"Clustering: {len(rows)} docs after filtering (max_age={max_age_days}d)")
        return [{"doc_id": r[0], "title": r[1], "text": r[2]} for r in rows]

    def save_clusters(self, clusters: List[Dict[str, Any]]):
        conn = self._get_conn()
        cursor = conn.cursor()
        
        # version ID
        version = "v1-tfidf"
        created_at = 0 # handled by SQL time usually but here we need consistent batch
        
        for c in clusters:
            cursor.execute("INSERT INTO clusters (name, created_at, clustering_version) VALUES (?, ?, ?)", (c['name'], created_at, version))
            cluster_id = cursor.lastrowid
            
            for doc_id in c['doc_ids']:
                cursor.execute("INSERT INTO cluster_assignments (cluster_id, doc_id, score) VALUES (?, ?, 1.0)", (cluster_id, doc_id))
        
        conn.commit()
