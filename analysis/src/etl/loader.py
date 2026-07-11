from typing import List, Dict, Any, Optional
import sqlite3
import json
import time
import re
from contextlib import contextmanager
from pathlib import Path
from urllib.parse import urlparse
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

# Section-index / hub pages (site homepages, "/politics/" fronts) leak through
# the crawler as if they were articles: trafilatura returns the nav-menu text,
# which then passes the political-keyword filter because menus name political
# topics ("Immigration", "Trump Administration"). Menu text is detectable
# without an LLM: near-zero sentence punctuation, dominated by Title Case
# fragments, root/section URLs, and recurring site-chrome vocabulary.
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

# Site root or one short all-alpha segment ("/", "/politics/", "/us"). Article
# URLs carry dated or hyphenated slugs and never match.
_HUB_URL_RE = re.compile(r"^/(?:[a-z]{1,20}/?)?$")


def is_index_page(text: str, url: str = "") -> bool:
    """Detect a section-index / hub page masquerading as an article.

    Deterministic by design (invariant: code answers when code can answer).
    Signals are combined so that no single noisy one drops a real article:
    real articles have sentence punctuation every few words and mostly
    lowercase prose, so they clear every rule below.
    """
    words = text.split()
    n = len(words)
    if n == 0:
        return False  # empty text is handled (skipped) separately

    lowered = text.lower()
    chrome_hits = sum(term in lowered for term in INDEX_CHROME_TERMS)

    # Sentence enders per 100 words. Abbreviations ("U.S.") inflate this
    # slightly, which is why thresholds below stay a few points apart from
    # real-article density (~5+ per 100 words).
    punct = len(re.findall(r"[.!?](?:\s|$)", text))
    punct_per_100 = punct / n * 100

    alpha_words = [w for w in words if w[:1].isalpha()]
    titlecase_share = (
        sum(w[0].isupper() for w in alpha_words) / len(alpha_words)
        if alpha_words else 0.0
    )

    path = urlparse(url).path or "/"
    hub_url = bool(_HUB_URL_RE.match(path))

    if hub_url and (punct_per_100 < 4.0 or chrome_hits >= 2):
        return True
    if n >= 30 and punct_per_100 < 1.0:
        return True
    if n >= 30 and titlecase_share >= 0.65 and punct_per_100 < 4.0:
        return True
    return chrome_hits >= 3

# ETL logic version stamped onto every docs row (invariant B1 / audit A-9).
# Bump this whenever the filter keywords, the 30-day rule, or the extraction
# logic below change, so docs produced by different ETL logic are
# distinguishable. Mirrors the prompt_version constants in engine/prompts.py.
# v2: index/hub-page filter for news (is_index_page).
ETL_VERSION = "etl-v2"

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


def stamp_published_at(published_at: Optional[int], now: int) -> int:
    """Resolve the timestamp a doc is stored under (audit A-8 policy).

    NULL/0 published_at is stamped with the ETL time (``now``, our best proxy
    for a continuously-crawled doc's discovery time) rather than left NULL.
    Chosen over rejecting the doc: dropping real content because the source
    omitted a date is its own honesty failure, and every cached aggregate
    window filters ``published_at >= cutoff`` — a NULL satisfies no window, so
    these docs used to be bot/sentiment/claim-scored (paid LLM calls) yet
    appear nowhere in the UI. Stamping ETL-time keeps them visible and their
    analysis useful. Valid timestamps are returned unchanged; obviously
    invalid ones are rejected upstream by ``is_recent``.
    """
    if published_at is None or published_at == 0:
        return now
    return published_at


def is_recent(published_at: Optional[int], max_age_seconds: int = THIRTY_DAYS_SECONDS) -> bool:
    """Check if content was published within the allowed time window.

    NULL/0 published_at passes here (kept, then stamped with ETL-time by
    ``stamp_published_at`` at insert so the doc lands in time windows — see
    audit A-8). Genuinely-invalid dates (pre-2020 / future) are still rejected.
    """
    if published_at is None or published_at == 0:
        # No publish date: keep it (stamped ETL-time at insert), don't drop it.
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
        # Match the Go ingestor, which opens with foreign_keys(on). sqlite3
        # defaults FK enforcement OFF, so without this the loader's
        # DELETE FROM docs and every FK-bearing child write run unenforced
        # (audit D-5).
        conn.execute("PRAGMA foreign_keys = ON")
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
        skipped_index = 0

        with self._get_conn() as conn:
            cursor = conn.cursor()

            # Cleanup empty text docs to allow reprocessing
            cursor.execute("DELETE FROM docs WHERE source_type='news' AND text IS NULL")

            # Calculate raw directory path relative to DB
            raw_root = Path(self.db_path).parent / "raw" / "sha256"

            news_count, s_old, s_np, s_idx = self._load_news_batch(cursor, raw_root)
            new_docs += news_count
            skipped_old += s_old
            skipped_nonpolitical += s_np
            skipped_index += s_idx
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

        
        logger.info(
            f"ETL [{ETL_VERSION}] Loaded {new_docs} new documents. "
            f"Skipped: {skipped_old} old, {skipped_nonpolitical} non-political, "
            f"{skipped_index} index pages."
        )
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
        except (OSError, UnicodeError) as e:
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

        # Preload existing news idents once to avoid N+1 SELECTs on large batches.
        cursor.execute("SELECT ident FROM docs WHERE source_type = 'news'")
        existing_idents = {row[0] for row in cursor.fetchall()}

        insert_query = """
            INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, title, raw_hash, text, etl_version)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """
        batch: List[tuple] = []
        new_docs = 0
        skipped_old = 0
        skipped_nonpolitical = 0
        skipped_index = 0
        now = int(time.time())

        for row in articles:
            url_canon, domain, raw_hash, title, published_at = row

            if url_canon in existing_idents:
                continue

            if not is_recent(published_at):
                skipped_old += 1
                continue

            text = self._extract_text_from_raw(raw_hash, raw_root)
            if not text:
                continue

            # Before the political filter: nav menus name political topics,
            # so index pages would otherwise pass the keyword gate.
            if is_index_page(text, url_canon):
                skipped_index += 1
                continue

            if not is_us_political_content(text, title or "", url_canon):
                skipped_nonpolitical += 1
                continue

            batch.append(("news", url_canon, domain, stamp_published_at(published_at, now), title, raw_hash, text, ETL_VERSION))
            existing_idents.add(url_canon)

            if len(batch) >= BATCH_COMMIT_SIZE:
                new_docs += self._flush_batch(cursor, insert_query, batch)
                batch.clear()

        new_docs += self._flush_batch(cursor, insert_query, batch)
        return new_docs, skipped_old, skipped_nonpolitical, skipped_index

    def _load_reddit_batch(self, cursor: sqlite3.Cursor) -> tuple:
        """Load Reddit posts from reddit_posts_raw into docs using batched inserts."""
        cursor.execute("""
            SELECT fullname, subreddit, created_utc, title, body, raw_hash
            FROM reddit_posts_raw
        """)
        posts = cursor.fetchall()

        # Preload existing reddit idents once to avoid N+1 SELECTs.
        cursor.execute(
            "SELECT ident FROM docs WHERE source_type IN ('reddit_post', 'reddit_comment')"
        )
        existing_idents = {row[0] for row in cursor.fetchall()}

        insert_query = """
            INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, title, text, raw_hash, etl_version)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """
        batch: List[tuple] = []
        new_docs = 0
        skipped_old = 0
        skipped_nonpolitical = 0
        now = int(time.time())

        for row in posts:
            fullname, subreddit, created_utc, title, body, raw_hash = row

            if not fullname or fullname in existing_idents:
                continue

            if not is_recent(created_utc):
                skipped_old += 1
                continue

            text = f"{title or ''}\n\n{body or ''}".strip()

            if not is_us_political_content(text, title or ""):
                skipped_nonpolitical += 1
                continue

            batch.append(("reddit_post", fullname, subreddit, stamp_published_at(created_utc, now), title, text, raw_hash, ETL_VERSION))
            existing_idents.add(fullname)

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

        # Preload existing X tweet idents once to avoid N+1 SELECTs.
        cursor.execute("SELECT ident FROM docs WHERE source_type = 'x_post'")
        existing_idents = {row[0] for row in cursor.fetchall()}

        insert_query = """
            INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, text, raw_hash, metadata_json, etl_version)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """
        batch: List[tuple] = []
        new_docs = 0
        skipped_old = 0
        skipped_nonpolitical = 0
        now = int(time.time())

        for row in x_posts:
            (tweet_id, author_id, created_at, text, lang,
             place_country_code, raw_hash,
             user_location, user_created_at,
             followers_count, verified, verified_type) = row

            if not tweet_id or tweet_id in existing_idents:
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

            # place_country_code stays in metadata_json (bot.py reads it from
            # there for the foreign-origin flag); the dropped docs column
            # (audit D-10) is no longer written.
            batch.append((
                "x_post", tweet_id, "x.com", stamp_published_at(created_at, now), text, raw_hash,
                json.dumps(metadata), ETL_VERSION,
            ))
            existing_idents.add(tweet_id)

            if len(batch) >= BATCH_COMMIT_SIZE:
                new_docs += self._flush_batch(cursor, insert_query, batch)
                batch.clear()

        new_docs += self._flush_batch(cursor, insert_query, batch)
        return new_docs, skipped_old, skipped_nonpolitical

    def get_unprocessed_docs(self, task_type: str, source_types: Optional[List[str]] = None, batch_size: int = 500) -> List[Dict[str, Any]]:
        """
        Returns docs whose ``doc_task_state`` row for the given task is
        missing or 'failed'. The state table is the work queue; ai_outputs
        row existence no longer gates processing (migration 022), so a task
        can be reprocessed by deleting its state rows without touching the
        append-only output log.

        ``raw_hash`` is included so callers can de-dup scoring at the
        content-hash level (e.g. syndicated AP wire stories reprinted by
        multiple outlets share the same raw_hash) — score once, fan result
        to siblings. See ``job_runner.run_propaganda_detection`` for the
        current consumer.
        """
        query = f"""
            SELECT d.doc_id, d.text, d.metadata_json, d.title, d.source_type,
                   d.ident, d.raw_hash
            FROM docs d
            LEFT JOIN doc_task_state s ON d.doc_id = s.doc_id AND s.task_type = ?
            WHERE (s.doc_id IS NULL OR s.status = 'failed') AND d.text IS NOT NULL
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
                "raw_hash": r[6],
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
        user_prompt_template: Optional[str] = None,
        inference_method: Optional[str] = None,
        label: Optional[str] = None,
    ):
        """Save an AI analysis output to the database.

        When ``system_prompt`` (and optionally ``user_prompt_template``) is
        provided alongside ``prompt_version`` they are upserted into
        ``prompt_versions`` so the full prompt text for any row can be
        reconstructed by joining on ``ai_outputs.prompt_version``.
        Walkthrough 041 completes the B1 reproducibility invariant by
        populating the previously-unused ``user_prompt_template`` column.

        ``inference_method`` (walkthrough 038) distinguishes rows from a
        validated LLM response ('llm') from deterministic-fallback rows
        ('heuristic') or rows from a purely deterministic engine
        ('deterministic'). None is allowed for backward compatibility; the
        column will land NULL.

        ``label`` (migration 023) is the row's primary categorical verdict,
        projected out of ``result`` so readers can filter/GROUP BY an indexed
        column instead of parsing JSON. Pass it for tasks with a scalar
        verdict (sentiment label, bot label, favorability stance); leave
        None for list-shaped tasks (claims, targets, propaganda).

        Returns the new row's output_id so callers can attach normalized
        projections to it (target_mentions).
        """
        with self._get_conn() as conn:
            cursor = conn.cursor()
            if system_prompt is not None and prompt_version:
                # Upsert so a later run that supplies user_prompt_template
                # can fill it in even if the system_prompt row already exists.
                cursor.execute(
                    """
                    INSERT INTO prompt_versions
                        (prompt_version, task_type, system_prompt,
                         user_prompt_template, created_at)
                    VALUES (?, ?, ?, ?, strftime('%s','now'))
                    ON CONFLICT(prompt_version) DO UPDATE SET
                        -- task_type is intentionally NOT rewritten here. Sentiment
                        -- and favorability share one prompt_version but save with
                        -- different task values; rewriting on every conflict made
                        -- the audit column flip-flop twice per doc (audit A-13).
                        -- First writer wins; the prompt text is identical so
                        -- reconstruction is unaffected.
                        system_prompt = excluded.system_prompt,
                        user_prompt_template = COALESCE(
                            excluded.user_prompt_template,
                            prompt_versions.user_prompt_template
                        )
                    """,
                    (prompt_version, task, system_prompt, user_prompt_template),
                )
            cursor.execute(
                """
                INSERT INTO ai_outputs
                    (doc_id, task_type, output_json, confidence, model_id,
                     prompt_version, inference_method, label, created_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, strftime('%s','now'))
                """,
                (doc_id, task, json.dumps(result), confidence, model_id,
                 prompt_version, inference_method, label),
            )
            output_id = cursor.lastrowid
            self.upsert_task_state(cursor, doc_id, task, "done", prompt_version)
            conn.commit()
            return output_id

    @staticmethod
    def upsert_task_state(
        cursor,
        doc_id: int,
        task: str,
        status: str,
        prompt_version: str = "",
    ):
        """Upsert the ``doc_task_state`` work-queue row for (doc_id, task).

        The state table (migration 022) is what get_unprocessed_docs queues
        off; ai_outputs is an append-only result log. Static so engines that
        manage their own transaction (citation_extractor) can write the state
        row atomically with their data writes.
        """
        cursor.execute(
            """
            INSERT INTO doc_task_state
                (doc_id, task_type, status, prompt_version, attempts,
                 last_attempt_at)
            VALUES (?, ?, ?, ?, 1, strftime('%s','now'))
            ON CONFLICT(doc_id, task_type) DO UPDATE SET
                status = excluded.status,
                prompt_version = excluded.prompt_version,
                attempts = doc_task_state.attempts + 1,
                last_attempt_at = excluded.last_attempt_at
            """,
            (doc_id, task, status, prompt_version),
        )

    def mark_task_failed(self, doc_id: int, task: str, prompt_version: str = ""):
        """Record a failed attempt for (doc_id, task).

        'failed' rows still re-queue in get_unprocessed_docs — this exists so
        retries are observable (attempts, last_attempt_at) instead of the old
        persist-nothing pattern where a doc failing every run was invisible
        (audit A-3).
        """
        with self._get_conn() as conn:
            cursor = conn.cursor()
            self.upsert_task_state(cursor, doc_id, task, "failed", prompt_version)
            conn.commit()

    # ---- target mentions (migration 025) ----

    @staticmethod
    def build_target_mentions(targets: List[Dict[str, Any]], resolve) -> List[Dict[str, Any]]:
        """Shape extracted target dicts into target_mentions rows, resolving
        entity identity NOW so it is frozen as of extraction.

        ``resolve`` is a callable raw_name -> (entity_key, entity_kind,
        entity_party); the registry lives in the reporting layer, so callers
        inject it rather than etl importing upward. Unresolved targets keep
        a row with entity_key NULL — they must stay countable, not vanish.
        """
        mentions = []
        for t in targets:
            if not isinstance(t, dict):
                continue
            raw = t.get("target")
            stance = t.get("stance")
            if not raw or stance not in ("positive", "negative", "neutral", "mixed"):
                continue
            try:
                confidence = float(t.get("confidence", 0.0))
            except (TypeError, ValueError):
                continue
            entity_key, entity_kind, entity_party = resolve(raw)
            mentions.append({
                "raw_target": raw,
                "entity_key": entity_key,
                "entity_kind": entity_kind,
                "entity_party": entity_party,
                "stance": stance,
                "topic": t.get("topic"),
                "confidence": confidence,
                "evidence_json": json.dumps(t.get("evidence_spans") or []),
            })
        return mentions

    def save_target_mentions(self, output_id: int, doc_id: int, mentions: List[Dict[str, Any]]):
        """Persist the normalized mention rows for one ai_outputs row."""
        if not mentions:
            return
        with self._get_conn() as conn:
            cursor = conn.cursor()
            self._insert_target_mentions(cursor, output_id, doc_id, mentions)
            conn.commit()

    @staticmethod
    def _insert_target_mentions(cursor, output_id: int, doc_id: int, mentions: List[Dict[str, Any]]):
        cursor.executemany(
            """
            INSERT INTO target_mentions
                (output_id, doc_id, raw_target, entity_key, entity_kind,
                 entity_party, stance, topic, confidence, evidence_json,
                 created_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, strftime('%s','now'))
            """,
            [
                (output_id, doc_id, m["raw_target"], m["entity_key"],
                 m["entity_kind"], m["entity_party"], m["stance"], m["topic"],
                 m["confidence"], m["evidence_json"])
                for m in mentions
            ],
        )

    def backfill_target_mentions(self, resolve) -> int:
        """Materialize mention rows for latest target_sentiment outputs that
        have none — the pre-migration-025 corpus, plus any reprocessed rows.
        Deterministic and idempotent (anti-join on existing mentions); no
        LLM involved. Returns the number of outputs materialized.
        """
        with self._get_conn() as conn:
            cursor = conn.cursor()
            cursor.execute(
                """
                SELECT a.output_id, a.doc_id, a.output_json
                FROM ai_outputs_latest a
                LEFT JOIN target_mentions m ON m.output_id = a.output_id
                WHERE a.task_type = 'target_sentiment'
                  AND m.mention_id IS NULL
                  AND json_array_length(a.output_json, '$.targets') > 0
                """
            )
            rows = cursor.fetchall()
            materialized = 0
            for output_id, doc_id, output_json in rows:
                try:
                    targets = (json.loads(output_json) or {}).get("targets") or []
                except json.JSONDecodeError:
                    continue
                mentions = self.build_target_mentions(targets, resolve)
                if mentions:
                    self._insert_target_mentions(cursor, output_id, doc_id, mentions)
                    materialized += 1
            conn.commit()
            return materialized

