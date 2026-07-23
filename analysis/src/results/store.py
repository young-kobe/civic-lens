"""
The result store — the ONLY writer of `analysis.*` result tables (Postgres
redesign Phase 5, plan `has-our-aggregate-method-async-frog`). Engines call
`open_run()`, accumulate typed results on the returned `RunHandle` via its
`save_*` methods, then `finish()` once to commit. Run-lifecycle semantics
(is_current flip ordering, advisory-lock concurrency, failed-run rule,
traceability contract): docs/DATABASE_SCHEMA.md's "Result-store write
semantics" section.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import List, Optional, Tuple

from psycopg.types.json import Jsonb

from analysis.src.common import db
from analysis.src.results.constants import PROMPT_REQUIRED_METHODS, TERMINAL_STATUSES


# =============================================================================
# Store-owned result dataclasses — one per analysis.* result table, fields
# matching the DDL columns store.py itself does not fill in (run_id, doc_id
# where it is implied by the run's own doc_id, timestamps).
# =============================================================================

@dataclass(frozen=True)
class SentimentRow:
    label: str  # analysis.sentiment_label
    score: Optional[float] = None
    sarcasm_detected: Optional[bool] = None
    evidence_spans: Optional[List[str]] = None


@dataclass(frozen=True)
class FavorabilityStanceRow:
    entity_id: int
    stance: str  # analysis.favorability_label
    score: Optional[float] = None
    evidence_spans: Optional[List[str]] = None


@dataclass(frozen=True)
class TargetMentionRow:
    raw_target: str
    stance: str  # analysis.sentiment_label
    confidence: float
    entity_id: Optional[int] = None
    topic: Optional[str] = None
    evidence_spans: Optional[List[str]] = None


@dataclass(frozen=True)
class PropagandaTechniqueRow:
    technique: str  # analysis.propaganda_technique
    evidence_span: str
    confidence: Optional[float] = None


@dataclass(frozen=True)
class PropagandaResultRow:
    density: Optional[float] = None
    summary: Optional[str] = None
    techniques_validated: int = 0
    techniques_dropped: int = 0


@dataclass(frozen=True)
class ClaimRow:
    claim_text: str
    topic: Optional[str] = None
    confidence: Optional[float] = None


@dataclass(frozen=True)
class BotSignalsRow:
    label: str  # analysis.bot_label
    score: Optional[float] = None
    llm_text_likelihood: Optional[float] = None
    burstiness: Optional[float] = None
    type_token_ratio: Optional[float] = None
    template_score: Optional[float] = None


@dataclass(frozen=True)
class CitationRow:
    link_type: str  # analysis.link_type
    target_doc_id: Optional[int] = None
    target_url: Optional[str] = None

    def __post_init__(self) -> None:
        if (self.target_doc_id is None) == (self.target_url is None):
            raise ValueError(
                "CitationRow requires exactly one of target_doc_id/target_url"
            )


class RunHandle:
    """
    Accumulates one analysis run's typed results in memory. Nothing reaches
    Postgres until `finish()`. Construct only via `open_run()`.
    """

    def __init__(
        self,
        task: str,
        doc_id: Optional[int],
        author_id: Optional[int],
        model_id: str,
        prompt_version: Optional[str],
        inference_method: str,
    ) -> None:
        self.task = task
        self.doc_id = doc_id
        self.author_id = author_id
        self.model_id = model_id
        self.prompt_version = prompt_version
        self.inference_method = inference_method
        self._sentiment: Optional[SentimentRow] = None
        self._favorability_stances: List[FavorabilityStanceRow] = []
        self._target_mentions: List[TargetMentionRow] = []
        self._propaganda: Optional[
            Tuple[PropagandaResultRow, List[PropagandaTechniqueRow]]
        ] = None
        self._claims: List[ClaimRow] = []
        self._bot_signals: Optional[BotSignalsRow] = None
        self._citations: List[CitationRow] = []

    def save_sentiment(self, row: SentimentRow) -> None:
        self._sentiment = row

    def save_favorability_stances(self, rows: List[FavorabilityStanceRow]) -> None:
        self._favorability_stances = list(rows)

    def save_target_mentions(self, rows: List[TargetMentionRow]) -> None:
        self._target_mentions = list(rows)

    def save_propaganda(
        self, result: PropagandaResultRow, techniques: List[PropagandaTechniqueRow]
    ) -> None:
        self._propaganda = (result, list(techniques))

    def save_claims(self, rows: List[ClaimRow]) -> None:
        self._claims = list(rows)

    def save_bot_signals(self, row: BotSignalsRow) -> None:
        if self.doc_id is None:
            raise ValueError("bot_signals requires a doc-scoped run")
        self._bot_signals = row

    def save_citations(self, rows: List[CitationRow]) -> None:
        if self.doc_id is None:
            raise ValueError("citations requires a doc-scoped run (source_doc_id = doc_id)")
        self._citations = list(rows)

    def finish(
        self,
        status: str,
        confidence: Optional[float] = None,
        raw_response: Optional[dict] = None,
        error: Optional[str] = None,
    ) -> int:
        """
        Commit this run and its accumulated results in one transaction.
        Returns the new run_id.

        Raises ValueError before touching the database if:
        - `status` is not 'done'/'failed'.
        - `status == 'done'` and `inference_method` is llm/hybrid but no
          `prompt_version` was given to `open_run()` (traceability contract).
        - `prompt_version` was given but is not registered (see
          `register_prompt_version()`).
        """
        if status not in TERMINAL_STATUSES:
            raise ValueError(f"finish() status must be one of {TERMINAL_STATUSES}, got {status!r}")
        if (
            status == "done"
            and self.inference_method in PROMPT_REQUIRED_METHODS
            and not self.prompt_version
        ):
            raise ValueError(
                f"traceability contract: a succeeded {self.inference_method} run "
                "requires prompt_version (deterministic runs may omit it)"
            )

        is_current = status == "done"
        subject_col = "doc_id" if self.doc_id is not None else "author_id"
        subject_id = self.doc_id if self.doc_id is not None else self.author_id
        lock_key = f"{self.task}:{subject_col}:{subject_id}"

        raw_response_param = Jsonb(raw_response) if raw_response else None

        with db.connection() as conn:
            # Transaction-scoped advisory lock: serializes concurrent
            # finish() calls for the same (subject, task) so the flip+insert
            # below never races against another connection's flip+insert.
            conn.execute("SELECT pg_advisory_xact_lock(hashtext(%s)::bigint)", (lock_key,))

            prompt_version_id = self._resolve_prompt_version_id(conn)

            if is_current:
                conn.execute(
                    f"UPDATE analysis.runs SET is_current = false "
                    f"WHERE task = %s::analysis.task AND {subject_col} = %s AND is_current",
                    (self.task, subject_id),
                )

            row = conn.execute(
                """
                INSERT INTO analysis.runs
                    (task, doc_id, author_id, status, model_id, prompt_version_id,
                     inference_method, confidence, is_current, raw_response, error,
                     started_at, completed_at)
                VALUES (%s::analysis.task, %s, %s, %s::analysis.run_status, %s, %s,
                        %s::analysis.inference_method, %s, %s, %s, %s, now(), now())
                RETURNING run_id
                """,
                (
                    self.task, self.doc_id, self.author_id, status, self.model_id,
                    prompt_version_id, self.inference_method, confidence, is_current,
                    raw_response_param, error,
                ),
            ).fetchone()
            run_id = row["run_id"]

            if status == "done":
                self._write_results(conn, run_id)

        return run_id

    def _resolve_prompt_version_id(self, conn) -> Optional[int]:
        if self.prompt_version is None:
            return None
        row = conn.execute(
            "SELECT prompt_version_id FROM analysis.prompt_versions WHERE prompt_version = %s",
            (self.prompt_version,),
        ).fetchone()
        if row is None:
            raise ValueError(
                f"prompt_version {self.prompt_version!r} is not registered "
                "(call register_prompt_version() first)"
            )
        return row["prompt_version_id"]

    def _write_results(self, conn, run_id: int) -> None:
        if self._sentiment is not None:
            r = self._sentiment
            conn.execute(
                """
                INSERT INTO analysis.sentiment_results
                    (run_id, label, score, sarcasm_detected, evidence_spans)
                VALUES (%s, %s::analysis.sentiment_label, %s, %s, %s)
                """,
                (run_id, r.label, r.score, r.sarcasm_detected, r.evidence_spans),
            )
        for r in self._favorability_stances:
            conn.execute(
                """
                INSERT INTO analysis.favorability_stances
                    (run_id, entity_id, stance, score, evidence_spans)
                VALUES (%s, %s, %s::analysis.favorability_label, %s, %s)
                """,
                (run_id, r.entity_id, r.stance, r.score, r.evidence_spans),
            )
        for r in self._target_mentions:
            conn.execute(
                """
                INSERT INTO analysis.target_mentions
                    (run_id, doc_id, raw_target, entity_id, stance, topic,
                     confidence, evidence_spans)
                VALUES (%s, %s, %s, %s, %s::analysis.sentiment_label, %s, %s, %s)
                """,
                (run_id, self.doc_id, r.raw_target, r.entity_id, r.stance,
                 r.topic, r.confidence, r.evidence_spans),
            )
        if self._propaganda is not None:
            self._write_propaganda(conn, run_id)
        for r in self._claims:
            conn.execute(
                """
                INSERT INTO analysis.claims (run_id, doc_id, claim_text, topic, confidence)
                VALUES (%s, %s, %s, %s, %s)
                """,
                (run_id, self.doc_id, r.claim_text, r.topic, r.confidence),
            )
        if self._bot_signals is not None:
            r = self._bot_signals
            conn.execute(
                """
                INSERT INTO analysis.bot_signals
                    (run_id, doc_id, label, score, llm_text_likelihood, burstiness,
                     type_token_ratio, template_score)
                VALUES (%s, %s, %s::analysis.bot_label, %s, %s, %s, %s, %s)
                """,
                (run_id, self.doc_id, r.label, r.score, r.llm_text_likelihood,
                 r.burstiness, r.type_token_ratio, r.template_score),
            )
        for r in self._citations:
            conn.execute(
                """
                INSERT INTO analysis.citations
                    (run_id, source_doc_id, target_doc_id, target_url, link_type)
                VALUES (%s, %s, %s, %s, %s::analysis.link_type)
                """,
                (run_id, self.doc_id, r.target_doc_id, r.target_url, r.link_type),
            )

    def _write_propaganda(self, conn, run_id: int) -> None:
        result, techniques = self._propaganda
        conn.execute(
            """
            INSERT INTO analysis.propaganda_results
                (run_id, density, summary, techniques_validated, techniques_dropped)
            VALUES (%s, %s, %s, %s, %s)
            """,
            (run_id, result.density, result.summary,
             result.techniques_validated, result.techniques_dropped),
        )
        for t in techniques:
            conn.execute(
                """
                INSERT INTO analysis.propaganda_techniques
                    (run_id, technique, evidence_span, confidence)
                VALUES (%s, %s::analysis.propaganda_technique, %s, %s)
                """,
                (run_id, t.technique, t.evidence_span, t.confidence),
            )


def open_run(
    task: str,
    *,
    doc_id: Optional[int] = None,
    author_id: Optional[int] = None,
    model_id: str,
    prompt_version: Optional[str] = None,
    inference_method: str,
) -> RunHandle:
    """
    Open a new analysis run. Writes nothing yet — returns a `RunHandle` that
    accumulates results via its `save_*` methods until `finish()` commits.

    Raises ValueError if not exactly one of `doc_id`/`author_id` is given
    (mirrors the `runs_doc_xor_author` CHECK constraint, checked here so the
    caller gets an immediate, readable error instead of a DB constraint
    violation after doing all its analysis work) or if `model_id` is empty
    (`analysis.runs.model_id` is NOT NULL for every run, llm or deterministic).
    """
    if (doc_id is None) == (author_id is None):
        raise ValueError("open_run() requires exactly one of doc_id/author_id")
    if not model_id:
        raise ValueError("open_run() requires a non-empty model_id")
    return RunHandle(task, doc_id, author_id, model_id, prompt_version, inference_method)


def register_prompt_version(
    prompt_version: str,
    task: str,
    system_prompt: str,
    user_prompt_template: Optional[str] = None,
) -> None:
    """
    Idempotent upsert into analysis.prompt_versions. Safe to call every time
    an engine runs with a given prompt_version — the second-and-later calls
    are no-ops beyond filling in `user_prompt_template` if it was NULL.

    `task` is deliberately not part of the ON CONFLICT SET clause: sentiment
    and favorability share one prompt_version under the unified 'text' task
    but this function may be called for either first — rewriting `task` on
    every call would make the audit column flip-flop for no reason (mirrors
    the same convention in the old etl/loader.py::save_ai_output).
    """
    with db.connection() as conn:
        conn.execute(
            """
            INSERT INTO analysis.prompt_versions
                (prompt_version, task, system_prompt, user_prompt_template)
            VALUES (%s, %s::analysis.task, %s, %s)
            ON CONFLICT (prompt_version) DO UPDATE SET
                system_prompt = excluded.system_prompt,
                user_prompt_template = COALESCE(
                    excluded.user_prompt_template,
                    analysis.prompt_versions.user_prompt_template
                )
            """,
            (prompt_version, task, system_prompt, user_prompt_template),
        )
