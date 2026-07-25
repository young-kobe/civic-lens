"""
New-stack pipeline runner (Postgres redesign Phase 7): builds the stage
registry over the real engine modules and drives it via `scheduler/
stages.py`. CLI: `python -m analysis.src.scheduler.pipeline` / `run.sh analyze-pg`.
"""

from __future__ import annotations

import argparse
import os
import time
from typing import Any, Dict, List, Optional

from psycopg.types.json import Jsonb

from analysis.src.common import db
from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings
from analysis.src.engine import account_tier, bot_detection, citations, claims, narrative_clustering, propaganda, targets, text
from analysis.src.etl import authors, documents, queue
from analysis.src.scheduler import stages
from analysis.src.scheduler.constants import STAGE_ORDER, STALE_IN_PROGRESS_MINUTES

logger = get_logger(__name__)


# =============================================================================
# Global-stage workers
# =============================================================================

def _etl_worker() -> Dict[str, int]:
    """Composes etl/authors.py -> etl/documents.py -> etl/queue.py in the
    ordering each module's own docstring prescribes (authors before
    documents so author_id resolves; documents before queue so it has rows
    to seed against)."""
    x_synced = authors.sync_x_authors()
    reddit_synced = authors.sync_reddit_authors()
    doc_result = documents.load_new_documents()
    seeded = queue.seed_pending_tasks()
    return {
        "x_authors_synced": x_synced,
        "reddit_authors_synced": reddit_synced,
        "docs_inserted": doc_result.inserted,
        "queue_rows_seeded": seeded,
    }


def _account_tier_worker() -> Any:
    return account_tier.classify_authors()


def _bot_rollup_worker() -> Any:
    return bot_detection.refresh_author_bot_scores()


def _narratives_worker() -> Any:
    return narrative_clustering.run()


def _leans_worker() -> Any:
    # engine.lean_derivation is being built concurrently with this module
    # (same wave) -- imported here, not at module scope, so `pipeline.py`
    # (and anything importing it) still works before that module lands.
    from analysis.src.engine import lean_derivation
    return lean_derivation.run()


# =============================================================================
# Queue-stage workers -- adapt each engine's own process() signature onto
# stages.py's uniform (doc_input, client, resolver) -> RunOutcome shape.
# process() returns a RunOutcome-shaped object (run_id/status/error) --
# analysis/src/results/store.py's RunHandle.finish() return value, threaded
# through every engine.
# =============================================================================

def _text_worker(doc: Any, client: Any, resolver: Any) -> Any:
    return text.process(doc, client)


def _targets_worker(doc: Any, client: Any, resolver: Any) -> Any:
    return targets.process(doc, client, resolver)


def _propaganda_worker(doc: Any, client: Any, resolver: Any) -> Any:
    return propaganda.process(doc, client)


def _claims_worker(doc: Any, client: Any, resolver: Any) -> Any:
    return claims.process(doc, client)


def _bot_worker(doc: Any, client: Any, resolver: Any) -> Any:
    return bot_detection.process(doc, client)


def _citations_worker(doc: Any, client: Any, resolver: Any) -> Any:
    return citations.process(doc)


def _build_registry() -> Dict[str, stages.StageSpec]:
    """Assembles the concrete StageSpec registry. Queried fresh per
    run_pipeline() call (not at module import time) because the targets
    stage's tracked_targets list needs a live DB connection."""
    with db.connection() as conn:
        tracked_targets = stages.load_tracked_target_names(conn)
    targets_input_loader = stages.build_targets_input_loader(tracked_targets)

    return {
        "etl": stages.StageSpec("etl", "global", _etl_worker),
        "account_tier": stages.StageSpec("account_tier", "global", _account_tier_worker),
        "bot": stages.StageSpec("bot", "queue", _bot_worker, stages.load_bot_input),
        "text": stages.StageSpec("text", "queue", _text_worker, stages.load_text_input),
        "targets": stages.StageSpec("targets", "queue", _targets_worker, targets_input_loader),
        "propaganda": stages.StageSpec("propaganda", "queue", _propaganda_worker, stages.load_propaganda_input),
        "citations": stages.StageSpec("citations", "queue", _citations_worker, stages.load_citations_input),
        "claims": stages.StageSpec("claims", "queue", _claims_worker, stages.load_claims_input),
        "bot_rollup": stages.StageSpec("bot_rollup", "global", _bot_rollup_worker),
        "narratives": stages.StageSpec("narratives", "global", _narratives_worker),
        "leans": stages.StageSpec("leans", "global", _leans_worker),
    }


# =============================================================================
# ops.pipeline_runs bookkeeping
# =============================================================================

_INSERT_PIPELINE_RUN_SQL = "INSERT INTO ops.pipeline_runs (started_at, status) VALUES (now(), 'running') RETURNING pipeline_run_id"

_FINISH_PIPELINE_RUN_SQL = """
    UPDATE ops.pipeline_runs
    SET completed_at = now(), status = %(status)s, stage_summary = %(stage_summary)s
    WHERE pipeline_run_id = %(pipeline_run_id)s
"""


def _start_pipeline_run() -> int:
    with db.connection() as conn:
        with conn.cursor() as cur:
            cur.execute(_INSERT_PIPELINE_RUN_SQL)
            return cur.fetchone()["pipeline_run_id"]


def _finish_pipeline_run(pipeline_run_id: int, *, status: str, stage_summary: Dict[str, Any]) -> None:
    with db.connection() as conn:
        with conn.cursor() as cur:
            cur.execute(_FINISH_PIPELINE_RUN_SQL, {
                "pipeline_run_id": pipeline_run_id, "status": status, "stage_summary": Jsonb(stage_summary),
            })


# =============================================================================
# Orchestrator
# =============================================================================

def run_pipeline(
    tasks: Optional[List[str]] = None,
    limit: Optional[int] = None,
    budget_seconds: Optional[float] = None,
) -> Dict[str, Any]:
    """Run one pipeline pass. `tasks` (if given) is a subset of
    STAGE_ORDER's names; stages always run in STAGE_ORDER regardless of the
    order `tasks` lists them in -- pipeline order encodes real dependencies
    (narratives after claims, leans after text/targets/narratives,
    bot_rollup after bot), not caller preference.

    A stage's per-doc `failed` count (expected, retryable via the queue's
    attempts mechanism) does NOT fail the pipeline_runs row; only a stage-
    level `error` (a worker thread aborting, or a kind='global' callable
    raising) does. Records one ops.pipeline_runs row: 'running' at start,
    'done'/'failed' + the accumulated stage_summary in a finally block so
    the row always closes out even if a stage raises past its own
    try/except (a bug, not an expected failure mode).
    """
    stage_names = set(tasks) if tasks else set(STAGE_ORDER)
    unknown = stage_names - set(STAGE_ORDER)
    if unknown:
        raise ValueError(f"Unknown stage name(s) {sorted(unknown)} -- choices are {STAGE_ORDER}")
    ordered_stage_names = [name for name in STAGE_ORDER if name in stage_names]

    queue.reset_stale_in_progress(older_than_minutes=STALE_IN_PROGRESS_MINUTES)
    settings = get_settings()
    concurrency = settings.analyze_concurrency
    budget_deadline = time.monotonic() + budget_seconds if budget_seconds is not None else None

    registry = _build_registry()
    pipeline_run_id = _start_pipeline_run()
    stage_summary: Dict[str, Any] = {}
    status = "done"
    try:
        for name in ordered_stage_names:
            spec = registry[name]
            if spec.kind == "queue":
                result = stages.run_queue_stage(
                    spec, concurrency=concurrency, limit=limit, budget_deadline=budget_deadline,
                )
            else:
                result = stages.run_global_stage(spec)
            stage_summary[name] = result.to_summary()
            if result.error:
                status = "failed"
            logger.info(f"stage {name}: {result.to_summary()}")
        return stage_summary
    except Exception as exc:
        status = "failed"
        stage_summary["_pipeline_error"] = str(exc)
        raise
    finally:
        _finish_pipeline_run(pipeline_run_id, status=status, stage_summary=stage_summary)


# =============================================================================
# CLI
# =============================================================================

def _budget_seconds_default() -> Optional[float]:
    raw = os.environ.get("CIVIC_BUDGET_SECONDS")
    return float(raw) if raw else None


def main() -> int:
    parser = argparse.ArgumentParser(description="Civic Lens Postgres-stack analysis pipeline")
    parser.add_argument(
        "--tasks", type=str,
        help=f"Comma-separated stage names ({', '.join(STAGE_ORDER)}). Defaults to all, in pipeline order.",
    )
    parser.add_argument("--limit", type=int, default=None, help="Cap docs processed per stage this run.")
    parser.add_argument(
        "--budget-seconds", type=float, default=_budget_seconds_default(),
        help="Soft wall-clock budget in seconds -- stop claiming new work once exceeded, let in-flight "
             "work finish. Also reads CIVIC_BUDGET_SECONDS. Unset = no budget.",
    )
    args = parser.parse_args()
    tasks = [t.strip() for t in args.tasks.split(",")] if args.tasks else None

    logger.info(
        f"pipeline starting. tasks={tasks or 'all'} limit={args.limit or 'none'} "
        f"budget_seconds={args.budget_seconds or 'none'}"
    )
    summary = run_pipeline(tasks=tasks, limit=args.limit, budget_seconds=args.budget_seconds)
    logger.info(f"pipeline complete: {summary}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
