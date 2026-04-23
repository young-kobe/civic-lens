"""
Review service for human-in-the-loop AI output evaluation.

Reads from ``ai_outputs`` and writes to ``ai_output_evals``. Surfaces a
prioritized queue (lowest-confidence rows first — those are the most useful
to review), accepts human labels + corrections + golden-set markers, and
returns per-task stats so a reviewer can see progress and observed accuracy
on the rows they have looked at.

This service is the only writer for ``ai_output_evals``; the table was
created schema-only in walkthrough 030 and is wired here.
"""

import json
import sqlite3
import time
from typing import Any, Dict, List, Optional

from analysis.src.common.logger import get_logger
from analysis.src.reporting.aggregators.base import X_AUTHOR_JOIN_SQL, get_connection
from analysis.src.reporting.aggregators.narrative import _build_doc_url

logger = get_logger(__name__)


# Maximum doc text shown in the review queue payload — full text loads on demand.
_TEXT_PREVIEW_CHARS = 1200


class ReviewService:
    """Reads the unreviewed AI-output queue and persists human reviews."""

    def __init__(self, db_path: str):
        self.db_path = db_path

    def get_queue(
        self,
        task_type: str,
        source_type: Optional[str] = None,
        confidence_max: Optional[float] = None,
        limit: int = 20,
        offset: int = 0,
    ) -> List[Dict[str, Any]]:
        """Return up to ``limit`` unreviewed AI outputs for the given task.

        Ordered lowest-confidence-first: the rows where the model is least
        sure are the most valuable to put a human eye on.
        """
        # LEFT JOIN x_posts_raw + x_users_raw so we can synthesize an X
        # permalink for x_post docs. Invariant C1: every evidence surface
        # must link back to the original. This join is a flagged duplication
        # hotspot; see docs/todos/backend-aggregator-audit.md §1.
        sql = f"""
            SELECT a.output_id, a.doc_id, a.task_type, a.output_json, a.confidence,
                   a.model_id, a.prompt_version, a.created_at,
                   d.source_type, d.domain_or_subreddit, d.title, d.text, d.ident,
                   u.username
            FROM ai_outputs a
            JOIN docs d ON d.doc_id = a.doc_id
            LEFT JOIN ai_output_evals e ON e.ai_output_id = a.output_id
            {X_AUTHOR_JOIN_SQL}
            WHERE a.task_type = ?
              AND e.ai_output_id IS NULL
        """
        params: List[Any] = [task_type]
        if source_type:
            sql += " AND d.source_type = ?"
            params.append(source_type)
        if confidence_max is not None:
            sql += " AND a.confidence <= ?"
            params.append(confidence_max)
        sql += " ORDER BY a.confidence ASC LIMIT ? OFFSET ?"
        params.extend([limit, offset])

        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            cursor.execute(sql, params)
            rows = cursor.fetchall()

        items = []
        for row in rows:
            (output_id, doc_id, t, output_json, conf, model_id, prompt_version,
             created_at, src, domain, title, text, ident, x_handle) = row
            try:
                payload = json.loads(output_json) if output_json else {}
            except json.JSONDecodeError:
                payload = {}
            text = text or ""
            items.append({
                "ai_output_id": output_id,
                "doc_id": doc_id,
                "task_type": t,
                "model_id": model_id or "",
                "prompt_version": prompt_version or "",
                "model_confidence": conf,
                "model_output": payload,
                "created_at": created_at,
                "doc": {
                    "source_type": src,
                    "domain": domain,
                    "title": title,
                    "ident": ident,
                    "url": _build_doc_url(src, domain, ident, x_handle=x_handle),
                    "text_preview": text[:_TEXT_PREVIEW_CHARS],
                    "text_truncated": len(text) > _TEXT_PREVIEW_CHARS,
                },
            })
        return items

    def submit(
        self,
        ai_output_id: int,
        is_correct: Optional[int],
        human_label: Optional[str],
        human_confidence: Optional[float],
        is_golden: bool,
        reviewer_id: Optional[str],
        notes: Optional[str],
    ) -> Dict[str, Any]:
        """Persist a review. Replaces any existing review for the same output."""
        now = int(time.time())
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            # Resolve doc_id and task_type from the underlying ai_outputs row —
            # so the eval row carries enough denormalized context for fast joins.
            cursor.execute(
                "SELECT doc_id, task_type FROM ai_outputs WHERE output_id = ?",
                (ai_output_id,),
            )
            ref = cursor.fetchone()
            if not ref:
                raise ValueError(f"ai_output_id {ai_output_id} not found")
            doc_id, task_type = ref

            cursor.execute(
                """
                INSERT OR REPLACE INTO ai_output_evals
                    (ai_output_id, doc_id, task_type, human_label, human_confidence,
                     is_correct, is_golden, reviewer_id, reviewed_at, notes)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    ai_output_id, doc_id, task_type,
                    human_label, human_confidence,
                    is_correct, 1 if is_golden else 0,
                    reviewer_id, now, notes,
                ),
            )
            conn.commit()
        return {"ai_output_id": ai_output_id, "reviewed_at": now}

    def get_stats(self, task_type: Optional[str] = None) -> Dict[str, Any]:
        """Per-task review progress and observed accuracy."""
        sql_total = "SELECT task_type, COUNT(*) FROM ai_outputs"
        sql_eval = """
            SELECT task_type,
                   COUNT(*) AS reviewed,
                   SUM(CASE WHEN is_correct = 1 THEN 1 ELSE 0 END) AS correct,
                   SUM(CASE WHEN is_correct = 0 THEN 1 ELSE 0 END) AS incorrect,
                   SUM(CASE WHEN is_golden = 1 THEN 1 ELSE 0 END) AS golden
            FROM ai_output_evals
        """
        params: List[Any] = []
        if task_type:
            sql_total += " WHERE task_type = ?"
            sql_eval += " WHERE task_type = ?"
            params.append(task_type)
        sql_total += " GROUP BY task_type"
        sql_eval += " GROUP BY task_type"

        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            cursor.execute(sql_total, params)
            totals = {t: c for t, c in cursor.fetchall()}
            cursor.execute(sql_eval, params)
            evals = {row[0]: row for row in cursor.fetchall()}

        out = []
        all_tasks = set(totals.keys()) | set(evals.keys())
        for t in sorted(all_tasks):
            total = totals.get(t, 0)
            ev = evals.get(t, (t, 0, 0, 0, 0))
            _, reviewed, correct, incorrect, golden = ev
            reviewed = reviewed or 0
            correct = correct or 0
            incorrect = incorrect or 0
            golden = golden or 0
            scored = correct + incorrect
            accuracy_pct = round((correct / scored) * 100, 1) if scored > 0 else None
            out.append({
                "task_type": t,
                "total_outputs": total,
                "reviewed": reviewed,
                "correct": correct,
                "incorrect": incorrect,
                "golden": golden,
                "accuracy_pct": accuracy_pct,
            })
        return {"per_task": out}
