from typing import List, Dict, Any, Optional
import sqlite3
import json
import time
import re
from contextlib import contextmanager
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

# SQLite connection pragmas (match Go-side busy_timeout for cross-language safety)
SQLITE_BUSY_TIMEOUT_MS = 5000

# Commit batch size for ETL bulk inserts
BATCH_COMMIT_SIZE = 100


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

    @contextmanager
    def _get_conn(self):
        """Create a SQLite connection with WAL mode and busy_timeout pragma."""
        conn = sqlite3.connect(self.db_path, check_same_thread=False)
        conn.execute(f"PRAGMA busy_timeout = {SQLITE_BUSY_TIMEOUT_MS}")
        conn.execute("PRAGMA journal_mode = WAL")
        try:
            yield conn
        finally:
            conn.close()

    def load_new_raw_content(self) -> int:
        """
        Reads raw tables, normalizes, and inserts into 'docs' if not present.
        Applies filters:
        - Only content from last 30 days
        - Only US political content
        Uses executemany for batched inserts to reduce DB lock duration.
        Returns count of new docs.
        """
        new_docs = 0
        skipped_old = 0
        skipped_nonpolitical = 0
        
        with self._get_conn() as conn:
            cursor = conn.cursor()
            
            # Cleanup empty text docs to allow reprocessing
            cursor.execute("DELETE FROM docs WHERE source_type='news' AND text IS NULL")
            
            # Calculate raw directory path relative to DB
            raw_root = Path(self.db_path).parent / "raw" / "sha256"
            
            news_count, s_old, s_np = self._load_news_batch(cursor, raw_root)
            new_docs += news_count
            skipped_old += s_old
            skipped_nonpolitical += s_np
            conn.commit()
            
            reddit_count, s_old, s_np = self._load_reddit_batch(cursor)
            new_docs += reddit_count
            skipped_old += s_old
            skipped_nonpolitical += s_np
            conn.commit()
            
            x_count, s_old, s_np = self._load_x_batch(cursor)
            new_docs += x_count
            skipped_old += s_old
            skipped_nonpolitical += s_np
            conn.commit()

        
        logger.info(f"ETL Loaded {new_docs} new documents. Skipped: {skipped_old} old, {skipped_nonpolitical} non-political.")
        return new_docs

    def _extract_text_from_raw(self, raw_hash: str, raw_root: Path) -> Optional[str]:
        """Extract text content from a raw HTML file using trafilatura."""
        if not raw_hash or len(raw_hash) <= 2:
            return None
        
        prefix = raw_hash[:2]
        path = raw_root / prefix / f"{raw_hash}.html"
        if not path.exists():
            return None
        
        try:
            with open(path, 'r', encoding='utf-8', errors='ignore') as f:
                html_content = f.read()
            return trafilatura.extract(html_content)
        except Exception as e:
            logger.warning(f"Failed to extract text for {raw_hash}: {e}")
            return None

    def _flush_batch(self, cursor: sqlite3.Cursor, query: str, batch: List[tuple]) -> int:
        """Execute a batch insert and return the count inserted."""
        if not batch:
            return 0
        cursor.executemany(query, batch)
        return len(batch)

    def _load_news_batch(self, cursor: sqlite3.Cursor, raw_root: Path) -> tuple:
        """Load news articles from articles_raw into docs using batched inserts."""
        cursor.execute("""
            SELECT url_canon, domain, raw_hash, title, published_at 
            FROM articles_raw
        """)
        articles = cursor.fetchall()
        
        insert_query = """
            INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, title, raw_hash, text)
            VALUES (?, ?, ?, ?, ?, ?, ?)
        """
        batch: List[tuple] = []
        new_docs = 0
        skipped_old = 0
        skipped_nonpolitical = 0
        
        for row in articles:
            url_canon, domain, raw_hash, title, published_at = row
            
            cursor.execute("SELECT doc_id FROM docs WHERE ident = ?", (url_canon,))
            if cursor.fetchone():
                continue
            
            if not is_recent(published_at):
                skipped_old += 1
                continue
            
            text = self._extract_text_from_raw(raw_hash, raw_root)
            if not text:
                continue
            
            if not is_us_political_content(text, title or "", url_canon):
                skipped_nonpolitical += 1
                continue
                
            batch.append(("news", url_canon, domain, published_at, title, raw_hash, text))
            
            if len(batch) >= BATCH_COMMIT_SIZE:
                new_docs += self._flush_batch(cursor, insert_query, batch)
                batch.clear()
        
        new_docs += self._flush_batch(cursor, insert_query, batch)
        return new_docs, skipped_old, skipped_nonpolitical

    def _load_reddit_batch(self, cursor: sqlite3.Cursor) -> tuple:
        """Load Reddit posts from reddit_posts_raw into docs using batched inserts."""
        cursor.execute("""
            SELECT fullname, subreddit, created_utc, title, body, raw_hash
            FROM reddit_posts_raw
        """)
        posts = cursor.fetchall()
        
        insert_query = """
            INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, title, text, raw_hash)
            VALUES (?, ?, ?, ?, ?, ?, ?)
        """
        batch: List[tuple] = []
        new_docs = 0
        skipped_old = 0
        skipped_nonpolitical = 0
        
        for row in posts:
            fullname, subreddit, created_utc, title, body, raw_hash = row
            
            if not fullname:
                continue
                
            cursor.execute("SELECT doc_id FROM docs WHERE ident = ?", (fullname,))
            if cursor.fetchone():
                continue
            
            if not is_recent(created_utc):
                skipped_old += 1
                continue
            
            text = f"{title or ''}\n\n{body or ''}".strip()
            
            if not is_us_political_content(text, title or ""):
                skipped_nonpolitical += 1
                continue
            
            batch.append(("reddit_post", fullname, subreddit, created_utc, title, text, raw_hash))
            
            if len(batch) >= BATCH_COMMIT_SIZE:
                new_docs += self._flush_batch(cursor, insert_query, batch)
                batch.clear()
        
        new_docs += self._flush_batch(cursor, insert_query, batch)
        return new_docs, skipped_old, skipped_nonpolitical

    def _load_x_batch(self, cursor: sqlite3.Cursor) -> tuple:
        """Load X posts from x_posts_raw into docs using batched inserts."""
        cursor.execute("""
            SELECT p.tweet_id, p.author_id, p.created_at, p.text, p.lang,
                   p.place_country_code, p.raw_hash,
                   u.location, u.created_at as user_created_at,
                   u.followers_count, u.verified, u.verified_type
            FROM x_posts_raw p
            LEFT JOIN x_users_raw u ON p.author_id = u.user_id
        """)
        x_posts = cursor.fetchall()

        insert_query = """
            INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, text, raw_hash, metadata_json, place_country_code)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """
        batch: List[tuple] = []
        new_docs = 0
        skipped_old = 0
        skipped_nonpolitical = 0

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

            if not is_recent(created_at):
                skipped_old += 1
                continue

            if not is_us_political_content(text, ""):
                skipped_nonpolitical += 1
                continue

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

            batch.append((
                "x_post", tweet_id, "x.com", created_at, text, raw_hash,
                json.dumps(metadata), place_country_code,
            ))

            if len(batch) >= BATCH_COMMIT_SIZE:
                new_docs += self._flush_batch(cursor, insert_query, batch)
                batch.clear()

        new_docs += self._flush_batch(cursor, insert_query, batch)
        return new_docs, skipped_old, skipped_nonpolitical

    def get_unprocessed_docs(self, task_type: str, source_types: Optional[List[str]] = None, batch_size: int = 500) -> List[Dict[str, Any]]:
        """
        Returns docs that do not have an entry in ai_outputs for the given task.
        """
        query = f"""
            SELECT d.doc_id, d.text, d.metadata_json, d.title, d.source_type, d.ident
            FROM docs d
            LEFT JOIN ai_outputs a ON d.doc_id = a.doc_id AND a.task_type = ?
            WHERE a.output_id IS NULL AND d.text IS NOT NULL
        """
        params = [task_type]
        if source_types:
            placeholders = ','.join('?' for _ in source_types)
            query += f" AND d.source_type IN ({placeholders})"
            params.extend(source_types)
        query += " LIMIT ?"
        params.append(batch_size)

        with self._get_conn() as conn:
            cursor = conn.cursor()
            cursor.execute(query, tuple(params))
            rows = cursor.fetchall()

        return [
            {
                "doc_id": r[0],
                "text": r[1],
                "metadata": json.loads(r[2]) if r[2] else {},
                "title": r[3],
                "source_type": r[4],
                "ident": r[5],
            }
            for r in rows
        ]

    def save_ai_output(
        self,
        doc_id: int,
        task: str,
        result: Dict[str, Any],
        confidence: float,
        model_id: str = "",
        prompt_version: str = "",
        system_prompt: Optional[str] = None,
    ):
        """Save an AI analysis output to the database.

        When ``system_prompt`` is provided alongside ``prompt_version`` it is
        upserted into ``prompt_versions`` so the full prompt text for any row
        can be reconstructed by joining on ``ai_outputs.prompt_version``.
        """
        with self._get_conn() as conn:
            cursor = conn.cursor()
            if system_prompt is not None and prompt_version:
                cursor.execute(
                    """
                    INSERT OR IGNORE INTO prompt_versions
                        (prompt_version, task_type, system_prompt, created_at)
                    VALUES (?, ?, ?, strftime('%s','now'))
                    """,
                    (prompt_version, task, system_prompt),
                )
            cursor.execute(
                """
                INSERT INTO ai_outputs
                    (doc_id, task_type, output_json, confidence, model_id, prompt_version, created_at)
                VALUES (?, ?, ?, ?, ?, ?, strftime('%s','now'))
                """,
                (doc_id, task, json.dumps(result), confidence, model_id, prompt_version),
            )
            conn.commit()

