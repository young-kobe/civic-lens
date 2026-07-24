"""Review service -- the only writer of analysis.evals / analysis.golden_labels.
Queue over unreviewed current runs, verdict submission, per-task agreement stats.
Contract details: docs/audit-trail/api/2026-07-24-phase9-wiring-review-docs.md."""

from __future__ import annotations

from typing import Any, Dict, List, Optional

from analysis.src.common import db
from analysis.src.review.constants import MIN_PUBLIC_REVIEW_N, TEXT_PREVIEW_CHARS

VALID_VERDICTS = ("correct", "incorrect", "uncertain")


def get_queue(
    task: str,
    *,
    source_type: Optional[str] = None,
    confidence_max: Optional[float] = None,
    limit: int = 20,
    offset: int = 0,
) -> List[Dict[str, Any]]:
    """Up to `limit` unreviewed current runs for `task`, lowest-confidence
    first -- the rows where the model is least sure are the most useful to
    put a human eye on. 'Unreviewed' = no analysis.evals row for that
    run_id yet."""
    params: Dict[str, Any] = {"task": task, "limit": limit, "offset": offset}
    extra_filters = ""
    if source_type is not None:
        extra_filters += " AND d.source_type = %(source_type)s::corpus.source_type"
        params["source_type"] = source_type
    if confidence_max is not None:
        extra_filters += " AND r.confidence <= %(confidence_max)s"
        params["confidence_max"] = confidence_max

    sql = f"""
        SELECT r.run_id AS run_id, r.doc_id AS doc_id, r.confidence AS confidence,
               r.model_id AS model_id, pv.prompt_version AS prompt_version,
               r.raw_response AS raw_response, r.completed_at AS completed_at,
               d.source_type::text AS source_type, d.admission_class::text AS admission_class,
               d.source_url AS source_url, d.title AS title, d.body AS body
        FROM analysis.runs r
        JOIN corpus.documents d ON d.doc_id = r.doc_id
        LEFT JOIN analysis.prompt_versions pv ON pv.prompt_version_id = r.prompt_version_id
        LEFT JOIN analysis.evals e ON e.run_id = r.run_id
        WHERE r.task = %(task)s::analysis.task
          AND r.is_current AND r.status = 'done'::analysis.run_status
          AND e.eval_id IS NULL
          {extra_filters}
        ORDER BY r.confidence ASC NULLS LAST, r.run_id
        LIMIT %(limit)s OFFSET %(offset)s
    """
    with db.connection() as conn:
        rows = conn.execute(sql, params).fetchall()
    return [_queue_item(row) for row in rows]


def _queue_item(row: Dict[str, Any]) -> Dict[str, Any]:
    body = row["body"] or ""
    return {
        "run_id": row["run_id"],
        "doc_id": row["doc_id"],
        "confidence": row["confidence"],
        "model_id": row["model_id"],
        "prompt_version": row["prompt_version"],
        "raw_response": row["raw_response"],
        "completed_at": row["completed_at"],
        "doc": {
            "source_type": row["source_type"],
            "admission_class": row["admission_class"],
            "source_url": row["source_url"],
            "title": row["title"],
            "text_preview": body[:TEXT_PREVIEW_CHARS],
            "text_truncated": len(body) > TEXT_PREVIEW_CHARS,
        },
    }


def submit(
    run_id: int,
    verdict: str,
    *,
    reviewer_id: Optional[str] = None,
    notes: Optional[str] = None,
    is_golden: bool = False,
    expected_label: Optional[str] = None,
) -> Dict[str, Any]:
    """Insert (or correct) the verdict for ONE run_id. When `is_golden`,
    also mints/refreshes the run's (doc_id, task) analysis.golden_labels row
    in the SAME transaction. Raises ValueError if `run_id` doesn't exist, if
    `is_golden` is set without `expected_label`, or if `is_golden` targets an
    author-scoped run (account_tier) -- golden_labels is keyed by doc_id,
    which those runs don't have."""
    if verdict not in VALID_VERDICTS:
        raise ValueError(f"verdict must be one of {VALID_VERDICTS}, got {verdict!r}")
    if is_golden and not expected_label:
        raise ValueError("is_golden=True requires expected_label")

    with db.connection() as conn:
        run = conn.execute(
            "SELECT doc_id, task::text AS task FROM analysis.runs WHERE run_id = %(run_id)s",
            {"run_id": run_id},
        ).fetchone()
        if run is None:
            raise ValueError(f"run_id {run_id} not found")
        if is_golden and run["doc_id"] is None:
            raise ValueError(
                f"is_golden=True requires a doc-scoped run -- task {run['task']!r} "
                "is author-scoped (no doc_id to key analysis.golden_labels on)"
            )

        eval_sql = """
            INSERT INTO analysis.evals (run_id, verdict, reviewer_id, notes)
            VALUES (%(run_id)s, %(verdict)s::analysis.verdict, %(reviewer_id)s, %(notes)s)
            ON CONFLICT (run_id) DO UPDATE SET
                verdict = excluded.verdict, reviewer_id = excluded.reviewer_id,
                notes = excluded.notes, reviewed_at = now()
            RETURNING eval_id, reviewed_at
        """
        eval_row = conn.execute(eval_sql, {
            "run_id": run_id, "verdict": verdict, "reviewer_id": reviewer_id, "notes": notes,
        }).fetchone()

        if is_golden:
            golden_sql = """
                INSERT INTO analysis.golden_labels (doc_id, task, expected_label, source_eval_id)
                VALUES (%(doc_id)s, %(task)s::analysis.task, %(expected_label)s, %(eval_id)s)
                ON CONFLICT (doc_id, task) DO UPDATE SET
                    expected_label = excluded.expected_label,
                    source_eval_id = excluded.source_eval_id
            """
            conn.execute(golden_sql, {
                "doc_id": run["doc_id"], "task": run["task"],
                "expected_label": expected_label, "eval_id": eval_row["eval_id"],
            })

    return {"eval_id": eval_row["eval_id"], "run_id": run_id, "reviewed_at": eval_row["reviewed_at"]}


def get_stats(*, task: Optional[str] = None) -> Dict[str, Any]:
    """Per-task counts of current-done runs, reviewed evals, and observed
    agreement. `scored` (correct+incorrect) excludes verdict='uncertain'
    from the accuracy denominator -- an explicit "can't tell" is not
    evidence either way."""
    params: Dict[str, Any] = {"task": task} if task is not None else {}
    run_filter = " AND task = %(task)s::analysis.task" if task is not None else ""
    eval_filter = " AND r.task = %(task)s::analysis.task" if task is not None else ""

    totals_sql = (
        "SELECT task::text AS task, COUNT(*) AS total FROM analysis.runs "
        f"WHERE is_current AND status = 'done'::analysis.run_status{run_filter} GROUP BY task"
    )
    evals_sql = (
        "SELECT r.task::text AS task, COUNT(*) AS reviewed, "
        "SUM((e.verdict = 'correct')::int) AS correct, "
        "SUM((e.verdict = 'incorrect')::int) AS incorrect, "
        "SUM((e.verdict = 'uncertain')::int) AS uncertain "
        f"FROM analysis.evals e JOIN analysis.runs r ON r.run_id = e.run_id WHERE true{eval_filter} "
        "GROUP BY r.task"
    )
    golden_sql = (
        "SELECT task::text AS task, COUNT(*) AS golden FROM analysis.golden_labels "
        f"WHERE true{run_filter} GROUP BY task"
    )

    with db.connection() as conn:
        totals = {row["task"]: row["total"] for row in conn.execute(totals_sql, params).fetchall()}
        evals = {row["task"]: row for row in conn.execute(evals_sql, params).fetchall()}
        golden = {row["task"]: row["golden"] for row in conn.execute(golden_sql, params).fetchall()}

    per_task = []
    for t in sorted(set(totals) | set(evals) | set(golden)):
        ev = evals.get(t)
        reviewed = ev["reviewed"] if ev else 0
        correct = (ev["correct"] or 0) if ev else 0
        incorrect = (ev["incorrect"] or 0) if ev else 0
        uncertain = (ev["uncertain"] or 0) if ev else 0
        scored = correct + incorrect
        accuracy_pct = round((correct / scored) * 100, 1) if scored > 0 else None
        per_task.append({
            "task": t,
            "total_runs": totals.get(t, 0),
            "reviewed": reviewed,
            "correct": correct,
            "incorrect": incorrect,
            "uncertain": uncertain,
            "golden": golden.get(t, 0),
            "accuracy_pct": accuracy_pct,
        })
    return {"per_task": per_task}


def get_public_accuracy() -> Dict[str, Any]:
    """Public per-task human-agreement overlay: accuracy withheld (None)
    below MIN_PUBLIC_REVIEW_N scored (correct+incorrect) reviews so a thin
    sample can't render as "100% accurate". Reviewer identity and per-row
    labels stay private (those stay behind the admin-gated /review/stats)."""
    per_task = []
    for row in get_stats()["per_task"]:
        scored = row["correct"] + row["incorrect"]
        low = scored < MIN_PUBLIC_REVIEW_N
        per_task.append({
            "taskType": row["task"],
            "reviewed": row["reviewed"],
            "scored": scored,
            "golden": row["golden"],
            "accuracyPct": None if low else row["accuracy_pct"],
            "lowSample": low,
        })
    return {"perTask": per_task, "minReviewN": MIN_PUBLIC_REVIEW_N}
