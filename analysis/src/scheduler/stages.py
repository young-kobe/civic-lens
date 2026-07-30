"""
Stage scaffolding for the new-stack scheduler (Postgres redesign Phase 7):
`StageSpec`/`StageResult`, per-task input loaders, and the two stage
runners. See docs/audit-trail/analysis/2026-07-23-pg-scheduler-wave4.md.
"""

from __future__ import annotations

import threading
import time
from dataclasses import dataclass
from typing import Any, Callable, Dict, List, Optional, Sequence

from psycopg import Connection

from analysis.src.common import db
from analysis.src.common.entity_resolver import EntityResolver
from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings
from analysis.src.engine.bot_detection import BotDocInput
from analysis.src.engine.citations import CitationDocInput
from analysis.src.engine.claims import ClaimDocInput
from analysis.src.engine.propaganda import PropagandaDocInput
from analysis.src.engine.targets import TargetDocInput
from analysis.src.engine.text import TextDocInput
from analysis.src.llm.client import LLMClient
from analysis.src.llm.gemini import GeminiClient
from analysis.src.llm.ollama import OllamaClient
from analysis.src.llm.openai_compat import OpenAICompatClient
from analysis.src.scheduler.constants import MAX_TASK_ATTEMPTS

logger = get_logger(__name__)


# =============================================================================
# StageSpec / StageResult
# =============================================================================

@dataclass(frozen=True)
class StageSpec:
    """One pipeline stage. `kind='queue'` stages process ops.task_queue rows
    through `worker` via `run_queue_stage`'s claim loop -- `worker` there has
    signature `(doc_input, client, resolver) -> RunOutcome` (an object with
    `run_id`/`status`/`error` attributes -- see `_process_one`). `kind='global'`
    stages call `worker()` (no arguments) exactly once via `run_global_stage`
    -- etl composition, corpus-wide rollups. `input_loader`
    (`(conn, doc_id) -> DocInput`) is required for kind='queue' and unused
    otherwise."""

    name: str
    kind: str  # 'queue' | 'global'
    worker: Callable[..., Any]
    input_loader: Optional[Callable[[Connection, int], Any]] = None

    def __post_init__(self) -> None:
        if self.kind not in ("queue", "global"):
            raise ValueError(f"StageSpec {self.name!r}: kind must be 'queue' or 'global', got {self.kind!r}")
        if self.kind == "queue" and self.input_loader is None:
            raise ValueError(f"StageSpec {self.name!r}: kind='queue' requires an input_loader")


@dataclass
class StageResult:
    """Outcome of one stage run. `claimed`/`done`/`failed` are per-doc
    counts for kind='queue' stages (0/1/0 or 0/0/1 for kind='global'
    stages, which have no queue rows to count). `error` is set only for a
    stage-level failure -- a worker thread aborting outright (e.g. DB
    connectivity lost) or a kind='global' callable raising -- distinct from
    ordinary per-doc `failed` counts, which are expected/retryable via the
    queue's attempts mechanism and never set `error`."""

    name: str
    claimed: int
    done: int
    failed: int
    skipped: int
    duration_seconds: float
    error: Optional[str] = None

    def to_summary(self) -> Dict[str, Any]:
        summary: Dict[str, Any] = {
            "claimed": self.claimed,
            "done": self.done,
            "failed": self.failed,
            "skipped": self.skipped,
            "duration_seconds": round(self.duration_seconds, 3),
        }
        if self.error:
            summary["error"] = self.error
        return summary


# =============================================================================
# LLM client construction -- one backend instance per worker thread
# =============================================================================
#
# llm.client.get_client()/llm.factory.get_llm_client() return process-wide
# singletons. Every backend's complete_once() does `self.total_tokens_used
# += ...` with no lock -- a genuine data race if concurrent worker threads
# share one backend instance. The counter is otherwise unread by anything
# in the pipeline today, but sharing it is still a latent bug, not a
# tolerated one: each worker thread gets its own fresh backend + LLMClient
# instead of the cached singleton.

def _new_llm_client() -> LLMClient:
    """Construct a fresh LLMClient (fresh backend, not factory.py's cached
    singleton) for one worker thread. See module comment above."""
    settings = get_settings()
    backend_name = settings.llm_backend.lower()
    if backend_name == "ollama":
        backend = OllamaClient(
            host=settings.ollama_host, model=settings.ollama_model, timeout=settings.ollama_timeout,
        )
    elif backend_name == "openai_compat":
        backend = OpenAICompatClient(
            base_url=settings.llm_base_url, api_key=settings.llm_api_key, model=settings.llm_model,
            embedding_model=settings.llm_embedding_model, timeout=settings.llm_timeout,
        )
    else:
        backend = GeminiClient(
            api_key=settings.gemini_api_key, model=settings.gemini_model, temperature=settings.gemini_temperature,
        )
    return LLMClient(backend)


# =============================================================================
# Per-task input loaders
# =============================================================================

_TEXT_INPUT_SQL = "SELECT doc_id, source_type, title, body FROM corpus.documents WHERE doc_id = %(doc_id)s"


def load_text_input(conn: Connection, doc_id: int) -> TextDocInput:
    row = conn.execute(_TEXT_INPUT_SQL, {"doc_id": doc_id}).fetchone()
    if row is None:
        raise ValueError(f"load_text_input: doc_id={doc_id} not found in corpus.documents")
    return TextDocInput(doc_id=row["doc_id"], source_type=row["source_type"], title=row["title"], text=row["body"])


_PROPAGANDA_INPUT_SQL = "SELECT doc_id, source_type, title, body FROM corpus.documents WHERE doc_id = %(doc_id)s"


def load_propaganda_input(conn: Connection, doc_id: int) -> PropagandaDocInput:
    row = conn.execute(_PROPAGANDA_INPUT_SQL, {"doc_id": doc_id}).fetchone()
    if row is None:
        raise ValueError(f"load_propaganda_input: doc_id={doc_id} not found in corpus.documents")
    return PropagandaDocInput(
        doc_id=row["doc_id"], source_type=row["source_type"], title=row["title"], text=row["body"],
    )


_CLAIMS_INPUT_SQL = "SELECT doc_id, body FROM corpus.documents WHERE doc_id = %(doc_id)s"


def load_claims_input(conn: Connection, doc_id: int) -> ClaimDocInput:
    row = conn.execute(_CLAIMS_INPUT_SQL, {"doc_id": doc_id}).fetchone()
    if row is None:
        raise ValueError(f"load_claims_input: doc_id={doc_id} not found in corpus.documents")
    return ClaimDocInput(doc_id=row["doc_id"], text=row["body"])


_CITATIONS_INPUT_SQL = """
    SELECT d.doc_id, d.source_type, d.body,
           x.referenced_tweet_id, x.referenced_tweet_type
    FROM corpus.documents d
    LEFT JOIN corpus.x_posts x USING (doc_id)
    WHERE d.doc_id = %(doc_id)s
"""


def load_citations_input(conn: Connection, doc_id: int) -> CitationDocInput:
    row = conn.execute(_CITATIONS_INPUT_SQL, {"doc_id": doc_id}).fetchone()
    if row is None:
        raise ValueError(f"load_citations_input: doc_id={doc_id} not found in corpus.documents")
    return CitationDocInput(
        doc_id=row["doc_id"], source_type=row["source_type"], text=row["body"],
        referenced_tweet_id=row["referenced_tweet_id"], referenced_tweet_type=row["referenced_tweet_type"],
    )


_BOT_INPUT_SQL = """
    SELECT d.doc_id, d.source_type, d.body,
           a.followers_count, a.following_count, a.verified_type, a.account_created_at,
           x.place_country_code
    FROM corpus.documents d
    LEFT JOIN corpus.authors a USING (author_id)
    LEFT JOIN corpus.x_posts x USING (doc_id)
    WHERE d.doc_id = %(doc_id)s
"""


def load_bot_input(conn: Connection, doc_id: int) -> BotDocInput:
    row = conn.execute(_BOT_INPUT_SQL, {"doc_id": doc_id}).fetchone()
    if row is None:
        raise ValueError(f"load_bot_input: doc_id={doc_id} not found in corpus.documents")
    return BotDocInput(
        doc_id=row["doc_id"], source_type=row["source_type"], text=row["body"],
        followers_count=row["followers_count"], following_count=row["following_count"],
        verified_type=row["verified_type"], account_created_at=row["account_created_at"],
        place_country_code=row["place_country_code"],
    )


_TARGETS_INPUT_SQL = "SELECT doc_id, source_type, body FROM corpus.documents WHERE doc_id = %(doc_id)s"

# tracked_targets is the same canonical-name list for every doc in a targets
# stage run (built once from the curated registry, not per doc). No
# hardcoded party-collective strings are added (corpus.entities has no
# populated kind='collective' rows yet; the open decision lives in
# docs/todos/recompute-acceptance-and-tuning.md -- fabricating them here
# would violate the never-fabricate-values rule).
_TRACKED_TARGET_NAMES_SQL = """
    SELECT display_name FROM corpus.entities
    WHERE kind = 'official' AND editorial AND active
    ORDER BY display_name
"""


def load_tracked_target_names(conn: Connection) -> List[str]:
    rows = conn.execute(_TRACKED_TARGET_NAMES_SQL).fetchall()
    return [row["display_name"] for row in rows]


def build_targets_input_loader(tracked_targets: Sequence[str]) -> Callable[[Connection, int], TargetDocInput]:
    """Closes over one stage run's tracked_targets list (loaded once via
    `load_tracked_target_names`) to produce the `(conn, doc_id)` loader
    `run_queue_stage` calls per claimed row."""

    def _load(conn: Connection, doc_id: int) -> TargetDocInput:
        row = conn.execute(_TARGETS_INPUT_SQL, {"doc_id": doc_id}).fetchone()
        if row is None:
            raise ValueError(f"load_targets_input: doc_id={doc_id} not found in corpus.documents")
        return TargetDocInput(
            doc_id=row["doc_id"], source_type=row["source_type"], text=row["body"],
            tracked_targets=tracked_targets,
        )

    return _load


# =============================================================================
# ops.task_queue claim / requeue / outcome SQL
# =============================================================================

_REQUEUE_FAILED_SQL = """
    UPDATE ops.task_queue
    SET status = 'pending', updated_at = now()
    WHERE task = %(task)s::analysis.task AND status = 'failed' AND attempts < %(max_attempts)s
"""


def requeue_failed_under_max_attempts(task: str) -> int:
    """Reprocess-not-delete: UPDATE failed rows under MAX_TASK_ATTEMPTS back
    to pending at the start of that task's stage run. Rows already at max
    attempts stay 'failed' for manual reset (ops.task_queue's documented
    contract). Returns the number of rows requeued."""
    with db.connection() as conn:
        with conn.cursor() as cur:
            cur.execute(_REQUEUE_FAILED_SQL, {"task": task, "max_attempts": MAX_TASK_ATTEMPTS})
            requeued = cur.rowcount
    logger.info(f"stage {task}: requeued {requeued} failed row(s) under max attempts")
    return requeued


_CLAIM_ONE_SQL = """
    UPDATE ops.task_queue
    SET status = 'in_progress', claimed_at = now(), updated_at = now()
    WHERE (doc_id, task) IN (
        SELECT doc_id, task FROM ops.task_queue
        WHERE task = %(task)s::analysis.task AND status = 'pending'
        ORDER BY doc_id
        LIMIT 1
        FOR UPDATE SKIP LOCKED
    )
    RETURNING doc_id
"""


def _claim_one(task: str) -> Optional[int]:
    with db.connection() as conn:
        with conn.cursor() as cur:
            cur.execute(_CLAIM_ONE_SQL, {"task": task})
            row = cur.fetchone()
    return row["doc_id"] if row else None


_MARK_DONE_SQL = """
    UPDATE ops.task_queue SET status = 'done', updated_at = now()
    WHERE doc_id = %(doc_id)s AND task = %(task)s::analysis.task
"""

_MARK_FAILED_SQL = """
    UPDATE ops.task_queue
    SET status = 'failed', attempts = attempts + 1, last_error = %(error)s, updated_at = now()
    WHERE doc_id = %(doc_id)s AND task = %(task)s::analysis.task
"""


def _mark_done(task: str, doc_id: int) -> None:
    with db.connection() as conn:
        with conn.cursor() as cur:
            cur.execute(_MARK_DONE_SQL, {"doc_id": doc_id, "task": task})


def _mark_failed(task: str, doc_id: int, error: str) -> None:
    with db.connection() as conn:
        with conn.cursor() as cur:
            cur.execute(_MARK_FAILED_SQL, {"doc_id": doc_id, "task": task, "error": error})


# =============================================================================
# Claim loop (kind='queue' stages)
# =============================================================================

class _StageProgress:
    """Shared, lock-guarded counters for one run_queue_stage() call.
    `try_reserve`/`release_reservation` bound `claimed` at `limit` (a slot
    is reserved before the claim SQL runs and given back if nothing was
    actually pending, so `claimed` never exceeds `limit`). `errors` holds
    stage-level failures (a worker thread aborting outright), never
    ordinary per-doc failures -- see StageResult.error's docstring."""

    def __init__(self, limit: Optional[int], budget_deadline: Optional[float]):
        self._lock = threading.Lock()
        self.limit = limit
        self.budget_deadline = budget_deadline
        self.claimed = 0
        self.done = 0
        self.failed = 0
        self.errors: List[str] = []

    def budget_exceeded(self) -> bool:
        return self.budget_deadline is not None and time.monotonic() >= self.budget_deadline

    def try_reserve(self) -> bool:
        with self._lock:
            if self.limit is not None and self.claimed >= self.limit:
                return False
            self.claimed += 1
            return True

    def release_reservation(self) -> None:
        with self._lock:
            self.claimed -= 1

    def record_done(self) -> None:
        with self._lock:
            self.done += 1

    def record_failed(self) -> None:
        with self._lock:
            self.failed += 1

    def record_error(self, message: str) -> None:
        with self._lock:
            self.errors.append(message)


def _process_one(
    spec: StageSpec, progress: _StageProgress, client: LLMClient, resolver: EntityResolver, doc_id: int,
) -> None:
    """`spec.worker` returns a RunOutcome-shaped object (`run_id`, `status`
    ('done'|'failed'), `error`) -- analysis/src/results/store.py's
    `RunHandle.finish()` return value, threaded through every engine's
    process(). A raise before that object exists (loader SQL error, DB
    down mid-call) is also the failed path, using the exception text as
    last_error since there is no run row to read one from."""
    try:
        with db.connection() as conn:
            doc_input = spec.input_loader(conn, doc_id)
        outcome = spec.worker(doc_input, client, resolver)
    except Exception as exc:
        logger.warning(f"stage {spec.name}: doc_id={doc_id} raised before a run row existed: {exc}")
        _mark_failed(spec.name, doc_id, str(exc))
        progress.record_failed()
        return

    if outcome.status == "done":
        _mark_done(spec.name, doc_id)
        progress.record_done()
    else:
        _mark_failed(spec.name, doc_id, outcome.error or f"run {outcome.run_id} ended status={outcome.status!r}")
        progress.record_failed()


def _worker_loop(spec: StageSpec, progress: _StageProgress, resolver: EntityResolver) -> None:
    try:
        # Fresh backend + LLMClient per thread, not the process-wide
        # singleton -- see _new_llm_client's module comment (every
        # backend's complete_once() does an unlocked `self.total_tokens_
        # used += ...`, a genuine race if two threads shared one backend).
        client = _new_llm_client()
        while True:
            if progress.budget_exceeded():
                return
            if not progress.try_reserve():
                return
            doc_id = _claim_one(spec.name)
            if doc_id is None:
                progress.release_reservation()
                return
            _process_one(spec, progress, client, resolver, doc_id)
    except Exception as exc:
        # A worker thread dying is not a per-doc failure (those are caught
        # in _process_one) -- it means something systemic broke (DB
        # connectivity, etc). threading.Thread swallows an uncaught
        # exception silently on join(); record it so run_queue_stage can
        # surface it instead (Rule: fail loud).
        logger.error(f"stage {spec.name}: worker thread aborted: {exc}")
        progress.record_error(str(exc))


def run_queue_stage(
    spec: StageSpec, *, concurrency: int, limit: Optional[int], budget_deadline: Optional[float],
) -> StageResult:
    """Requeue eligible failed rows, then run `concurrency` worker threads
    against ops.task_queue for `spec.name` until no pending row claims, the
    shared `limit` is reached, or `budget_deadline` passes (in-flight work
    always finishes -- only new claims stop)."""
    requeue_failed_under_max_attempts(spec.name)
    started = time.monotonic()
    progress = _StageProgress(limit=limit, budget_deadline=budget_deadline)
    # EntityResolver is read-only after construction (a plain dict lookup),
    # so one instance is safely shared across worker threads -- unlike the
    # LLM client (see _new_llm_client's docstring), it does not need one
    # per thread.
    resolver = EntityResolver()
    threads = [
        threading.Thread(target=_worker_loop, args=(spec, progress, resolver))
        for _ in range(max(1, concurrency))
    ]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    return StageResult(
        name=spec.name, claimed=progress.claimed, done=progress.done, failed=progress.failed,
        skipped=0, duration_seconds=time.monotonic() - started,
        error="; ".join(progress.errors) if progress.errors else None,
    )


# =============================================================================
# Global stage runner (kind='global' stages)
# =============================================================================

def run_global_stage(spec: StageSpec) -> StageResult:
    """Call `spec.worker()` once. A raised exception is the whole stage's
    failure (there is no per-item retry concept for a corpus-wide pass), so
    it is recorded as StageResult.error, not a per-doc `failed` count."""
    started = time.monotonic()
    try:
        spec.worker()
        return StageResult(
            name=spec.name, claimed=0, done=1, failed=0, skipped=0,
            duration_seconds=time.monotonic() - started,
        )
    except Exception as exc:
        logger.error(f"stage {spec.name}: failed: {exc}")
        return StageResult(
            name=spec.name, claimed=0, done=0, failed=1, skipped=0,
            duration_seconds=time.monotonic() - started, error=str(exc),
        )
