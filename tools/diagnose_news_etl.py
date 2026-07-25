"""Diagnose why the news ETL admits zero documents.

Read-only. Runs the real admission predicates from analysis/src/etl/documents.py
against real raw.articles rows, so the verdicts here are the ones the ETL
itself would reach -- no reimplementation, nothing to drift.

    cd /opt/civic-lens
    PYTHONPATH=$PWD analysis/.venv/bin/python tools/diagnose_news_etl.py

Context: the 2026-07-25 ETL run reported inserted=0 with
rejections={'stale': 4016, 'denied_domain': 55, 'not_political': 2842} against
16,942 raw.articles rows -- 6,913 accounted for, 10,029 unexplained, and a
100% rejection rate on the politics filter for everything that reached it.
"""

from __future__ import annotations

import datetime
from collections import Counter
from pathlib import Path

from analysis.src.common import db
from analysis.src.common.settings import get_settings
from analysis.src.etl.documents import (
    _admit_news_posttext,
    _admit_news_pretext,
    _extract_text_from_raw,
    is_us_political_content,
    load_domain_filter_config,
)

SAMPLE_SIZE = 10


def main() -> None:
    settings = get_settings()
    root = Path(settings.raw_store_dir)
    now = datetime.datetime.now(datetime.timezone.utc)
    cfg = load_domain_filter_config()

    print("=" * 72)
    print("raw_store_dir setting :", settings.raw_store_dir)
    print("resolved absolute     :", root.resolve())
    print("exists                :", root.is_dir())
    print("denied domains        :", len(getattr(cfg, "deny", []) or []))
    print("allowlisted domains   :", len(getattr(cfg, "allow", []) or []))
    print("per-domain cap        :", cfg.max_docs_per_domain_per_window)

    with db.connection() as conn:
        with conn.cursor() as cur:
            cur.execute("SELECT count(*) AS n FROM raw.articles")
            total = cur.fetchone()["n"]
            cur.execute(
                """
                SELECT count(*) AS n FROM raw.articles a
                WHERE NOT EXISTS (
                    SELECT 1 FROM corpus.documents d
                    WHERE d.source_type = 'news' AND d.natural_key = a.url_canon
                )
                """
            )
            candidate_rows = cur.fetchone()["n"]
            cur.execute(
                "SELECT source_type, count(*) AS n FROM corpus.documents GROUP BY 1 ORDER BY 1"
            )
            composition = cur.fetchall()

        print("=" * 72)
        print("raw.articles total            :", total)
        print("rows the ETL query would scan :", candidate_rows)
        print("corpus.documents composition  :",
              {r["source_type"]: r["n"] for r in composition})
        if candidate_rows != total:
            print("NOTE: scan count < total means some url_canon values already")
            print("      exist in corpus.documents as source_type='news'.")

        # Replay the real admission pipeline over every row, tallying verdicts.
        with conn.cursor() as cur:
            cur.execute(
                """
                SELECT a.url_canon, a.domain, a.raw_hash, a.title, a.published_at
                FROM raw.articles a
                WHERE NOT EXISTS (
                    SELECT 1 FROM corpus.documents d
                    WHERE d.source_type = 'news' AND d.natural_key = a.url_canon
                )
                """
            )
            rows = cur.fetchall()

    tally: Counter[str] = Counter()
    would_admit = []
    politics_failures = []

    for row in rows:
        domain_key = (row["domain"] or "").lower()
        pre = _admit_news_pretext(domain_key, row["published_at"], cfg, now)
        if not pre.admitted:
            tally[pre.reason] += 1
            continue

        text = _extract_text_from_raw(row["raw_hash"], root)
        if not text:
            tally["extraction_failed"] += 1
            continue

        post = _admit_news_posttext(text, row["title"], row["url_canon"], domain_key, cfg)
        if not post.admitted:
            tally[post.reason] += 1
            if post.reason == "not_political" and len(politics_failures) < SAMPLE_SIZE:
                politics_failures.append((row, text))
            continue

        tally["ADMITTED"] += 1
        would_admit.append(row)

    print("=" * 72)
    print("replayed verdicts over", len(rows), "rows:")
    for reason, count in tally.most_common():
        print(f"  {reason:<20} {count}")
    print("  (sum)              ", sum(tally.values()))

    if politics_failures:
        print("=" * 72)
        print(f"sample of rows rejected as not_political ({len(politics_failures)}):")
        for row, text in politics_failures:
            print("-" * 72)
            print("url    :", (row["url_canon"] or "")[:110])
            print("domain :", row["domain"])
            print("title  :", (row["title"] or "<none>")[:110])
            print("published_at:", row["published_at"])
            print("text len:", len(text))
            print("political(text+title):",
                  is_us_political_content(text, row["title"] or "", row["url_canon"]))
            print("political(title only):",
                  is_us_political_content("", row["title"] or "", row["url_canon"]))
            snippet = " ".join(text[:300].split())
            print("snippet:", snippet)

    print("=" * 72)
    print("would admit:", len(would_admit))
    if would_admit:
        print("first few:")
        for row in would_admit[:5]:
            print("  -", (row["url_canon"] or "")[:100])


if __name__ == "__main__":
    main()
