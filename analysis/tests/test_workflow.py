#!/usr/bin/env python3
"""
Test script for the Civic Lens analysis workflow.

Usage:
    python test_workflow.py

This script tests the complete analysis pipeline:
1. Verify database connectivity and schema
2. Run ETL to extract text from raw HTML
3. Run feature computation
4. Validate outputs

Results labeled with data source and confidence where applicable.
"""

import sqlite3
import json
import os
import sys
from pathlib import Path

# Resolve paths relative to this script
# Resolve paths relative to this script
SCRIPT_DIR = Path(__file__).parent.absolute()
PROJECT_ROOT = SCRIPT_DIR.parent.parent # analysis/tests -> analysis -> root
DB_PATH = PROJECT_ROOT / "data" / "news.db"
RAW_DIR = PROJECT_ROOT / "data" / "raw" / "sha256"

def log(msg, level="INFO"):
    """Simple logging helper."""
    print(f"[{level}] {msg}")

def test_database_connection():
    """Test 1: Verify database exists and has expected tables."""
    log("Testing database connection...")
    
    if not DB_PATH.exists():
        log(f"Database not found at {DB_PATH}", "ERROR")
        return False
    
    conn = sqlite3.connect(str(DB_PATH))
    cursor = conn.cursor()
    
    # Check for expected tables (these come from the Go migration)
    cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
    tables = [row[0] for row in cursor.fetchall()]
    
    log(f"Found tables: {tables}")
    
    # Core tables from Go ingest migration
    required_tables = ['pages', 'articles_raw']
    missing = [t for t in required_tables if t not in tables]
    
    if missing:
        log(f"Missing core ingestion tables: {missing}", "ERROR")
        log("Run: .\\civic-ingest.exe migrate", "INFO")
        conn.close()
        return False
    
    # Create docs table for Python analysis pipeline (if missing)
    if 'docs' not in tables:
        log("'docs' table does not exist. Creating for analysis pipeline...", "INFO")
        cursor.execute("""
            CREATE TABLE docs (
                doc_id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_type TEXT NOT NULL CHECK(source_type IN ('news', 'reddit')),
                ident TEXT UNIQUE NOT NULL,
                domain_or_subreddit TEXT,
                published_at INTEGER,
                fetched_at INTEGER,
                title TEXT,
                text TEXT,
                raw_hash TEXT NOT NULL,
                metadata_json TEXT
            )
        """)
        cursor.execute("CREATE INDEX IF NOT EXISTS idx_docs_published_at ON docs(published_at)")
        conn.commit()
        log("Created 'docs' table.", "INFO")
    
    # Report counts
    cursor.execute("SELECT COUNT(*) FROM pages")
    pages_count = cursor.fetchone()[0]
    
    cursor.execute("SELECT COUNT(*) FROM articles_raw")
    articles_count = cursor.fetchone()[0]
    
    cursor.execute("SELECT COUNT(*) FROM docs")
    docs_count = cursor.fetchone()[0]
    
    log(f"Database stats: pages={pages_count}, articles_raw={articles_count}, docs={docs_count}")
    
    conn.close()
    return True

def test_raw_file_access():
    """Test 2: Verify raw files exist and are accessible."""
    log("Testing raw file access...")
    
    if not RAW_DIR.exists():
        log(f"Raw directory not found at {RAW_DIR}", "ERROR")
        return False
    
    # Get a sample article from DB
    conn = sqlite3.connect(str(DB_PATH))
    cursor = conn.cursor()
    
    cursor.execute("SELECT url_canon, raw_hash FROM articles_raw LIMIT 5")
    samples = cursor.fetchall()
    conn.close()
    
    if not samples:
        log("No articles in articles_raw table yet.", "WARN")
        return True  # Not an error, just no data
    
    found = 0
    for url, raw_hash in samples:
        if not raw_hash:
            continue
        # raw_hash structure: full sha256, stored as sha256/<prefix>/<hash>.html
        prefix = raw_hash[:2]
        file_path = RAW_DIR / prefix / f"{raw_hash}.html"
        if file_path.exists():
            found += 1
            log(f"Found: {file_path.name} ({file_path.stat().st_size} bytes)")
        else:
            log(f"Missing raw file: {file_path}", "WARN")
    
    log(f"Raw file check: {found}/{len(samples)} files found")
    return found > 0 or len(samples) == 0

def test_etl_pipeline():
    """Test 3: Run ETL to extract text from raw HTML."""
    log("Testing ETL pipeline...")
    
    try:
        import trafilatura
    except ImportError:
        log("trafilatura not installed. Run: pip install trafilatura", "ERROR")
        return False
    
    conn = sqlite3.connect(str(DB_PATH))
    cursor = conn.cursor()
    
    # Find unprocessed articles
    cursor.execute("""
        SELECT r.url_canon, r.raw_hash, r.published_at, r.domain, r.title
        FROM articles_raw r
        LEFT JOIN docs d ON r.url_canon = d.ident
        WHERE d.ident IS NULL AND r.raw_hash IS NOT NULL
        LIMIT 10
    """)
    rows = cursor.fetchall()
    
    if not rows:
        log("No new articles to process (either all processed or none ingested).")
        cursor.execute("SELECT COUNT(*) FROM docs WHERE source_type='news'")
        existing = cursor.fetchone()[0]
        log(f"Existing docs in 'docs' table: {existing}")
        conn.close()
        return True
    
    processed = 0
    errors = 0
    
    for url, raw_hash, pub, domain, title in rows:
        prefix = raw_hash[:2]
        file_path = RAW_DIR / prefix / f"{raw_hash}.html"
        
        if not file_path.exists():
            log(f"Missing: {file_path}", "WARN")
            errors += 1
            continue
        
        try:
            with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
                html = f.read()
            
            text = trafilatura.extract(html)
            
            if text and len(text) > 100:  # Sanity check
                cursor.execute("""
                    INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, title, text, raw_hash)
                    VALUES (?, ?, ?, ?, ?, ?, ?)
                """, ('news', url, domain, pub, title, text, raw_hash))
                processed += 1
                log(f"Extracted {len(text)} chars from {domain}")
            else:
                log(f"No text extracted from {url}", "WARN")
                errors += 1
                
        except Exception as e:
            log(f"ETL error for {url}: {e}", "ERROR")
            errors += 1
    
    conn.commit()
    conn.close()
    
    log(f"ETL complete: {processed} processed, {errors} errors")
    return processed > 0 or errors == 0

def test_feature_computation():
    """Test 4: Run feature computation on docs."""
    log("Testing feature computation...")
    
    try:
        import textstat
        from textblob import TextBlob
    except ImportError as e:
        log(f"Missing dependency: {e}. Run: pip install textstat textblob", "ERROR")
        return False
    
    conn = sqlite3.connect(str(DB_PATH))
    cursor = conn.cursor()
    
    # Get docs without features computed
    cursor.execute("""
        SELECT doc_id, text, metadata_json FROM docs 
        WHERE text IS NOT NULL
        LIMIT 10
    """)
    rows = cursor.fetchall()
    
    if not rows:
        log("No docs to compute features for.")
        return True
    
    import hashlib
    
    updated = 0
    for doc_id, text, meta_str in rows:
        try:
            meta = json.loads(meta_str) if meta_str else {}
        except:
            meta = {}
        
        # Skip if already computed
        if 'readability_fk' in meta and meta['readability_fk'] is not None:
            continue
        
        # Compute features
        try:
            meta['readability_fk'] = textstat.flesch_kincaid_grade(text)
            meta['sentiment_polarity'] = TextBlob(text).sentiment.polarity
            meta['text_hash'] = hashlib.sha256(text.encode('utf-8')).hexdigest()
            meta['feature_version'] = 'v1.0'
            
            cursor.execute("UPDATE docs SET metadata_json=? WHERE doc_id=?", 
                         (json.dumps(meta), doc_id))
            updated += 1
            
        except Exception as e:
            log(f"Feature error for doc {doc_id}: {e}", "WARN")
    
    conn.commit()
    conn.close()
    
    log(f"Feature computation: updated {updated} docs")
    return True

def test_output_summary():
    """Test 5: Summary report of processed data."""
    log("Generating summary report...")
    
    conn = sqlite3.connect(str(DB_PATH))
    cursor = conn.cursor()
    
    # Get counts
    cursor.execute("SELECT COUNT(*) FROM docs WHERE source_type='news'")
    news = cursor.fetchone()[0]
    
    cursor.execute("SELECT COUNT(*) FROM docs WHERE source_type='reddit'")
    reddit = cursor.fetchone()[0]
    
    # Sample doc with features
    cursor.execute("""
        SELECT ident, title, metadata_json FROM docs 
        WHERE metadata_json IS NOT NULL AND metadata_json != '{}'
        LIMIT 1
    """)
    sample = cursor.fetchone()
    
    print("\n" + "="*60)
    print("ANALYSIS WORKFLOW TEST SUMMARY")
    print("="*60)
    print(f"  News docs processed:   {news}")
    print(f"  Reddit docs processed: {reddit}")
    print(f"  Total docs:            {news + reddit}")
    
    if sample:
        print("\n  Sample document with features:")
        print(f"    URL: {sample[0][:60]}...")
        print(f"    Title: {sample[1][:50] if sample[1] else 'N/A'}...")
        try:
            meta = json.loads(sample[2])
            print(f"    Readability (FK grade): {meta.get('readability_fk', 'N/A')}")
            print(f"    Sentiment polarity: {meta.get('sentiment_polarity', 'N/A')}")
        except:
            pass
    
    print("="*60)
    conn.close()
    return True

def main():
    print("\n" + "="*60)
    print("CIVIC LENS ANALYSIS WORKFLOW TEST")
    print("="*60 + "\n")
    
    tests = [
        ("Database Connection", test_database_connection),
        ("Raw File Access", test_raw_file_access),
        ("ETL Pipeline", test_etl_pipeline),
        ("Feature Computation", test_feature_computation),
        ("Output Summary", test_output_summary),
    ]
    
    results = []
    for name, test_fn in tests:
        print(f"\n--- {name} ---")
        try:
            result = test_fn()
            results.append((name, result))
        except Exception as e:
            log(f"Test failed with exception: {e}", "ERROR")
            results.append((name, False))
    
    print("\n" + "="*60)
    print("TEST RESULTS")
    print("="*60)
    for name, passed in results:
        status = "PASS" if passed else "FAIL"
        print(f"  [{status}] {name}")
    
    all_passed = all(r[1] for r in results)
    print("="*60)
    print(f"Overall: {'ALL TESTS PASSED' if all_passed else 'SOME TESTS FAILED'}")
    print("="*60 + "\n")
    
    return 0 if all_passed else 1

if __name__ == "__main__":
    sys.exit(main())
