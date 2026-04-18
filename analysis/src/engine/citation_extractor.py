"""
Cross-source citation extractor for Civic Lens.

Deterministic (no LLM). Writes two kinds of edge to ``narrative_citations``:
  - X quote/reply/retweet edges driven by ``x_posts_raw.referenced_tweet_id``
  - URL citation edges extracted from doc text, matched against ``docs.ident``
    when we own the target and stored as ``target_url`` otherwise.

Per-doc idempotency is tracked via an ``ai_outputs`` row with
``task_type='citations'``, so re-running the job skips already-processed docs.
"""

import re
import sqlite3
import time
from typing import List, Optional, Set, Tuple
from urllib.parse import urlparse, urlunparse

from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


# Loose URL matcher — canonicalization (below) is what actually decides matches.
_URL_REGEX = re.compile(r'https?://[^\s<>"\'\)\]]+', re.IGNORECASE)

# Map X API's referenced_tweet_type → our link_type enum.
_X_REFERENCE_LINK_TYPE = {
    "quoted": "quote",
    "replied_to": "reply",
    "retweeted": "retweet",
}

# Trailing punctuation that often bleeds into URLs captured from prose.
_URL_TRAILING_PUNCT = ".,;:!?"

CITATION_MARKER_PROMPT_VERSION = "citation-v1"
CITATION_MODEL_ID = "citation-extractor"


def canonicalize_url(url: str) -> str:
    """Normalize a URL for matching against ``docs.ident`` (canonical news URLs)."""
    raw = url.strip().rstrip(_URL_TRAILING_PUNCT)
    try:
        parsed = urlparse(raw)
    except ValueError:
        return raw
    if not parsed.scheme or not parsed.netloc:
        return raw

    host = parsed.netloc.lower()
    if host.startswith("www."):
        host = host[4:]
    path = parsed.path.rstrip("/") or "/"
    return urlunparse((parsed.scheme.lower(), host, path, parsed.params, parsed.query, ""))


class CitationExtractor:
    """Extracts cross-source citation edges, one doc at a time."""

    def __init__(self, db_path: str):
        self.db_path = db_path

    def extract_batch(self, docs: List[dict]) -> Tuple[int, int]:
        """Process a batch of docs. Returns (docs_processed, edges_written)."""
        total_edges = 0
        now = int(time.time())

        conn = sqlite3.connect(self.db_path)
        conn.execute("PRAGMA busy_timeout = 5000")
        conn.execute("PRAGMA journal_mode = WAL")
        try:
            cursor = conn.cursor()
            for i, doc in enumerate(docs, 1):
                edges = self._extract_single(cursor, doc, now)
                # Write the per-doc processed marker so we skip on next run.
                cursor.execute(
                    """
                    INSERT INTO ai_outputs
                        (doc_id, task_type, output_json, confidence, model_id, prompt_version, created_at)
                    VALUES (?, 'citations', ?, 1.0, ?, ?, strftime('%s','now'))
                    """,
                    (
                        doc["doc_id"],
                        f'{{"edges_written": {edges}}}',
                        CITATION_MODEL_ID,
                        CITATION_MARKER_PROMPT_VERSION,
                    ),
                )
                total_edges += edges
                logger.info(
                    f"[citations {i}/{len(docs)}] doc={doc['doc_id']} "
                    f"type={doc.get('source_type')} edges={edges}"
                )
                # Commit per-doc so a crash mid-batch doesn't lose work.
                conn.commit()
        finally:
            conn.close()

        return len(docs), total_edges

    def _extract_single(self, cursor: sqlite3.Cursor, doc: dict, now: int) -> int:
        edges = 0
        doc_id = doc["doc_id"]
        source_type = doc.get("source_type")
        ident = doc.get("ident")
        text = doc.get("text") or ""

        if source_type == "x_post" and ident:
            edges += self._extract_x_references(cursor, doc_id, ident, now)
        if text:
            edges += self._extract_url_citations(cursor, doc_id, text, now)
        return edges

    def _extract_x_references(
        self,
        cursor: sqlite3.Cursor,
        source_doc_id: int,
        tweet_id: str,
        now: int,
    ) -> int:
        cursor.execute(
            """
            SELECT referenced_tweet_id, referenced_tweet_type
            FROM x_posts_raw
            WHERE tweet_id = ?
              AND referenced_tweet_id IS NOT NULL
              AND referenced_tweet_type IS NOT NULL
            """,
            (tweet_id,),
        )
        row = cursor.fetchone()
        if not row:
            return 0

        ref_id, ref_type = row
        link_type = _X_REFERENCE_LINK_TYPE.get(ref_type)
        if not link_type:
            return 0

        cursor.execute("SELECT doc_id FROM docs WHERE ident = ?", (ref_id,))
        target = cursor.fetchone()
        if target is None:
            # We don't know the external tweet URL without re-fetching, so skip
            # un-ingested X references rather than writing half-useful edges.
            return 0

        cursor.execute(
            """
            INSERT INTO narrative_citations
                (source_doc_id, target_doc_id, target_url, link_type, discovered_at)
            VALUES (?, ?, NULL, ?, ?)
            """,
            (source_doc_id, target[0], link_type, now),
        )
        return 1

    def _extract_url_citations(
        self,
        cursor: sqlite3.Cursor,
        source_doc_id: int,
        text: str,
        now: int,
    ) -> int:
        urls = _URL_REGEX.findall(text)
        if not urls:
            return 0

        seen: Set[str] = set()
        written = 0
        for raw in urls:
            canon = canonicalize_url(raw)
            if canon in seen:
                continue
            seen.add(canon)

            cursor.execute("SELECT doc_id FROM docs WHERE ident = ?", (canon,))
            match = cursor.fetchone()
            target_doc_id: Optional[int] = match[0] if match else None
            if target_doc_id == source_doc_id:
                continue  # Self-reference.

            cursor.execute(
                """
                INSERT INTO narrative_citations
                    (source_doc_id, target_doc_id, target_url, link_type, discovered_at)
                VALUES (?, ?, ?, 'url_citation', ?)
                """,
                (
                    source_doc_id,
                    target_doc_id,
                    canon if target_doc_id is None else None,
                    now,
                ),
            )
            written += 1
        return written
