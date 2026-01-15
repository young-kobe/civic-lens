from typing import List, Dict, Any, Optional
import sqlite3
import json
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)

class ContentLoader:
    def __init__(self, db_path: str):
        self.db_path = db_path

    def _get_conn(self):
        return sqlite3.connect(self.db_path)

    def load_new_raw_content(self) -> int:
        """
        Reads raw tables, normalizes, and inserts into 'docs' if not present.
        Returns count of new docs.
        """
        conn = self._get_conn()
        cursor = conn.cursor()
        
        # 1. Load News
        cursor.execute("""
            SELECT article_id, url_canon, domain, source, raw_hash, title, published_at 
            FROM articles_raw
        """)
        articles = cursor.fetchall()
        
        new_docs = 0
        for row in articles:
            # Check if exists
            cursor.execute("SELECT doc_id FROM docs WHERE ident = ?", (row[1],))
            if cursor.fetchone():
                continue
                
            # Insert
            cursor.execute("""
                INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, title, raw_hash)
                VALUES (?, ?, ?, ?, ?, ?)
            """, ("news", row[1], row[2], row[6], row[5], row[4]))
            new_docs += 1
            
        # 2. Load Reddit Posts
        cursor.execute("""
            SELECT post_id, fullname, subreddit, created_utc, title, body, raw_hash
            FROM reddit_posts_raw
        """)
        posts = cursor.fetchall()
        for row in posts:
            if not row[1]: continue # Skip if no fullname
            cursor.execute("SELECT doc_id FROM docs WHERE ident = ?", (row[1],))
            if cursor.fetchone():
                continue
            
            # Combine title + body for text
            text = f"{row[4] or ''}\n\n{row[5] or ''}".strip()
            
            cursor.execute("""
                INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, title, text, raw_hash)
                VALUES (?, ?, ?, ?, ?, ?, ?)
            """, ("reddit_post", row[1], row[2], row[3], row[4], text, row[6]))
            new_docs += 1
            
        conn.commit()
        logger.info(f"ETL Loaded {new_docs} new documents.")
        return new_docs

    def get_unprocessed_docs(self, task_type: str) -> List[Dict[str, Any]]:
        """
        Returns docs that do not have an entry in ai_outputs for the given task.
        """
        conn = self._get_conn()
        cursor = conn.cursor()
        
        query = f"""
            SELECT d.doc_id, d.text, d.metadata_json, d.title
            FROM docs d
            LEFT JOIN ai_outputs a ON d.doc_id = a.doc_id AND a.task_type = ?
            WHERE a.output_id IS NULL AND d.text IS NOT NULL
            LIMIT 100
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

    def get_all_docs_for_clustering(self) -> List[Dict[str, Any]]:
        conn = self._get_conn()
        cursor = conn.cursor()
        cursor.execute("SELECT doc_id, title, text FROM docs WHERE text IS NOT NULL AND text != ''")
        rows = cursor.fetchall()
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
