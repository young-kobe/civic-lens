#!/usr/bin/env python3
"""
migrate_sqlite_to_pg.py — one-time/re-runnable migration of precious data
from the legacy SQLite database into the north-star Postgres schema
(`data/pg-migrations/0001_north_star.sql`). Phase 3 of the Postgres redesign
(plan `has-our-aggregate-method-async-frog`, checklist `docs/todos/pg-redesign.md`).

Standalone by design: this module imports only the standard library, sqlite3,
and psycopg. It does NOT import anything from analysis.src or ingest, so it
keeps working after both of those trees are deleted at Phase 11 decommission
— it is meant to outlive the code that wrote the data it migrates.

Three modes, mutually exclusive:

  --raw       raw.* + ops.x_api_budget. The load-bearing, re-runnable mode:
              X/Reddit capture history cannot be re-fetched, so this is the
              one truly irreversible-if-wrong step in the whole redesign (see
              the plan's Risks section). Idempotent via
              `INSERT ... ON CONFLICT (natural pk) DO UPDATE` keyed on the
              same natural keys the ingestor already upserts on, so running
              this again at cutover syncs only the delta.
  --archive   Verbatim, best-effort, one-time import of the old derived data
              (ai_outputs, narratives, ...) into the read-only `archive`
              schema, kept "just to have". Never hard-fails on a single bad
              row (invalid JSON, epoch-zero) — those are counted and
              reported, not fatal, per the plan ("archive import is
              best-effort insurance and never blocks cutover").
  --verify    Read-only both sides. Runs the full verification battery
              (row counts, NULL counts, aggregate checks, seeded random
              sample comparison, PK uniqueness, raw_hash -> file
              resolution) and prints a pass/fail report. Exits 1 on any
              failure — this is the gate the plan puts in front of cutover.

Design note — the ONE thing that must never drift:

  --raw and --archive write rows through a small set of mapping functions
  (epoch_to_datetime, epoch_to_datetime_not_null, is_epoch_missing,
  page_state_label, int_flag_to_bool, best_effort_json, raw_json_text) and a
  declarative ColumnSpec/TableSpec layer built on top of them. --verify reuses
  the *exact same* ColumnSpec.transform callables to compute what it expects
  the target side to look like before comparing against what is actually
  there. There is no second, hand-rolled copy of "how columns map" living in
  the verifier — if the writer's mapping ever changes, the verifier changes
  with it automatically, by construction. This is the core safety property
  the plan asks for ("share ONE mapping-function layer... so the verifier can
  never drift from the writer").

Epoch-zero rule (see docs/audit-trail/ingestion/2026-07-22-pg-ingestion-port.md
and ingest/internal/runner/pgtime.go for the Go-side precedent this mirrors):

  - Nullable target timestamp columns: source 0 or NULL -> SQL NULL. Never
    fabricate 1970-01-01 as a real timestamp.
  - NOT NULL target timestamp columns fall into two groups:
      * Sentinel columns (raw.pages.next_fetch_at / inflight_at): the DDL
        itself defaults these to `to_timestamp(0)` and the Go frontier writer
        uses epoch-zero as a genuine "ready now" / "never claimed" value, not
        an unset marker. 0 is expected here and is not counted as a problem.
      * Strict columns (raw.articles.fetched_at, raw.x_posts.created_at/
        fetched_at, raw.x_users.fetched_at, ops.x_api_budget.last_updated):
        these should always carry a real value. The row is still written
        (the column is NOT NULL; there is nowhere else to put it but
        epoch-zero) but every such occurrence is counted, and --raw fails
        loud at the end unless --tolerate-not-null-zeroes is passed.
  - --archive columns are all nullable in the archive schema, so they always
    use the lenient nullable mapping; zero/NULL occurrences are counted and
    reported but never fail the import.
"""

from __future__ import annotations

import argparse
import json
import random
import sqlite3
import sys
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Iterator, Optional, Sequence

import psycopg
from psycopg.types.json import Json

DEFAULT_BATCH_SIZE = 1000
DEFAULT_RAW_FILES_DIR = "data/raw/sha256"
DEFAULT_SAMPLE_SIZE = 500
DEFAULT_SEED = 42

# IEEE 754 single-precision (Postgres REAL / float4) machine epsilon: 2**-23,
# i.e. ~7 significant decimal digits of relative precision per stored value.
# Used only to widen the --verify SUM tolerance for REAL-typed columns (see
# ColumnSpec.real_type and _sum_tolerance) — never for MIN/MAX, which do not
# accumulate error across rows.
REAL_TYPE_RELATIVE_EPSILON = 1.1920929e-07

EPOCH_ZERO = datetime(1970, 1, 1, tzinfo=timezone.utc)

PAGE_STATE_LABELS = {0: "queued", 1: "inflight", 2: "done", 3: "failed"}


# =============================================================================
# Shared mapping-function layer (see module docstring "Design note").
# Both the --raw/--archive writers and --verify import these same functions
# through ColumnSpec.transform — nothing below is duplicated elsewhere.
# =============================================================================


def epoch_to_datetime(value: Optional[int]) -> Optional[datetime]:
    """Nullable-target epoch mapping: 0 or NULL -> SQL NULL, else the UTC
    datetime. Never fabricates 1970-01-01 as a real timestamp (media-analysis
    no-fabrication rule)."""
    if value is None or value == 0:
        return None
    return datetime.fromtimestamp(value, tz=timezone.utc)


def epoch_to_datetime_not_null(value: Optional[int]) -> datetime:
    """NOT-NULL-target epoch mapping, used unconditionally for both the
    sentinel columns (raw.pages.next_fetch_at/inflight_at, where 0 is a
    legitimate value) and the strict columns (paired with is_epoch_missing
    below for problem counting, where 0 is a data-quality signal). Either
    way the column cannot hold SQL NULL, so 0/NULL substitutes EPOCH_ZERO."""
    if value is None or value == 0:
        return EPOCH_ZERO
    return datetime.fromtimestamp(value, tz=timezone.utc)


def is_epoch_missing(value: Optional[int]) -> bool:
    """True when a source epoch value is 0 or NULL. Used to flag strict NOT
    NULL timestamp columns (a real problem) — never applied to the sentinel
    columns, where this same condition is expected and benign."""
    return value is None or value == 0


def page_state_label(value: int) -> str:
    """SQLite pages.state INTEGER (0/1/2/3) -> raw.page_state enum label."""
    try:
        return PAGE_STATE_LABELS[value]
    except KeyError as exc:
        raise ValueError(f"unknown page state integer: {value!r}") from exc


def int_flag_to_bool(value: Optional[int]) -> bool:
    """SQLite 0/1/NULL flag -> Postgres BOOLEAN NOT NULL. NULL maps to False,
    matching the NOT NULL DEFAULT false the target column carries."""
    return bool(value) if value is not None else False


def nullable_int_flag_to_bool(value: Optional[int]) -> Optional[bool]:
    """Same as int_flag_to_bool but preserves NULL (for nullable BOOLEAN
    target columns, e.g. archive.ai_output_evals.is_correct)."""
    return None if value is None else bool(value)


def raw_json_text(value: Optional[str]) -> Optional[str]:
    """--raw mode JSON mapping (x_posts_raw.context_annotations_json): ''
    or NULL -> SQL NULL; otherwise the text is passed through unvalidated for
    an explicit ::jsonb cast at INSERT time. Mirrors nullableJSON() in
    ingest/internal/runner/pgtime.go exactly — no validation is performed
    there either, since the X API client is trusted to have produced valid
    JSON at capture time. If that trust is ever violated the INSERT fails
    loud on the bad row, which is correct for the load-bearing raw path."""
    if value is None or value == "":
        return None
    return value


def best_effort_json(value: Optional[str]) -> tuple[Optional[Any], bool]:
    """--archive mode JSON mapping: '' or NULL -> (None, False); valid JSON
    text -> (parsed value, False); invalid JSON text -> (None, True). The
    bool flags a bad row for the archive import's summary report — archive
    is best-effort insurance and must never hard-fail over one bad row."""
    if value is None or value == "":
        return None, False
    try:
        return json.loads(value), False
    except (json.JSONDecodeError, TypeError):
        return None, True


# =============================================================================
# Declarative column/table specs
# =============================================================================


@dataclass(frozen=True)
class ColumnSpec:
    """One source column -> one target column, with the transform used by
    both the writer and --verify. `problem_check`, when set, flags source
    values that are data-quality problems for a strict NOT NULL epoch column
    (see is_epoch_missing) — the writer tallies these; --verify does not use
    it directly today but it documents which columns are "expected always
    populated" for a human reading the spec. `sql_cast` appends a Postgres
    cast to the VALUES placeholder (e.g. '::jsonb', '::raw.page_state') for
    columns whose target type psycopg/PG cannot infer from an untyped text
    parameter alone. `real_type` marks a column whose target Postgres type is
    REAL (float4) rather than DOUBLE PRECISION/INTEGER/BIGINT — every
    archive.* confidence/score column; --verify's SUM check widens its
    tolerance for these (see _sum_tolerance) to absorb single-precision
    storage rounding compounding across many summed rows, while MIN/MAX stay
    at the strict tolerance since they never accumulate error."""

    source: str
    target: str
    transform: Callable[[Any], Any] = field(default=lambda v: v)
    problem_check: Optional[Callable[[Any], bool]] = None
    sql_cast: str = ""
    aggregatable: bool = False
    bind_adapter: Optional[Callable[[Any], Any]] = None
    real_type: bool = False


@dataclass(frozen=True)
class TableSpec:
    source_table: str
    target_schema: str
    target_table: str
    columns: Sequence[ColumnSpec]
    pk_columns: Sequence[str]  # target column names
    conflict_action: str = "update"  # 'update' or 'nothing'

    @property
    def qualified_target(self) -> str:
        return f"{self.target_schema}.{self.target_table}"


def _json_best_effort_value(raw: Optional[str]) -> Optional[Any]:
    """Canonical value used by BOTH the writer and --verify: the parsed
    Python object (dict/list/str/...), or None for SQL NULL. Binding this
    for an actual INSERT needs an extra Json() wrap (see bind_json_value /
    ColumnSpec.bind_adapter) — kept separate so --verify compares against
    this exact canonical value, not a psycopg wire-format wrapper."""
    return best_effort_json(raw)[0]


def _json_best_effort_problem(raw: Optional[str]) -> bool:
    return best_effort_json(raw)[1]


def bind_json_value(value: Optional[Any]) -> Optional[Json]:
    """Writer-only binding step for a ::jsonb-cast column whose canonical
    value (from _json_best_effort_value) is a parsed Python object rather
    than raw text: None stays SQL NULL, everything else gets psycopg's Json
    adapter so executemany serializes it correctly."""
    return None if value is None else Json(value)


RAW_TABLES: list[TableSpec] = [
    TableSpec(
        source_table="pages",
        target_schema="raw",
        target_table="pages",
        pk_columns=["url_canon"],
        columns=[
            ColumnSpec("url_canon", "url_canon"),
            ColumnSpec("url_raw", "url_raw"),
            ColumnSpec("domain", "domain"),
            ColumnSpec("state", "state", transform=page_state_label, sql_cast="::raw.page_state"),
            ColumnSpec("priority", "priority", aggregatable=True),
            ColumnSpec("retries", "retries", aggregatable=True),
            # Sentinel columns: 0 is a legitimate "ready now"/"never claimed"
            # value (see module docstring), so no problem_check here.
            ColumnSpec("next_fetch_at", "next_fetch_at", transform=epoch_to_datetime_not_null, aggregatable=True),
            ColumnSpec("inflight_at", "inflight_at", transform=epoch_to_datetime_not_null, aggregatable=True),
            ColumnSpec("http_status", "http_status"),
            ColumnSpec("content_sha256", "content_sha256"),
            ColumnSpec("etag", "etag"),
            ColumnSpec("last_modified", "last_modified"),
            ColumnSpec("last_error", "last_error"),
        ],
    ),
    TableSpec(
        source_table="articles_raw",
        target_schema="raw",
        target_table="articles",
        pk_columns=["url_canon"],
        columns=[
            ColumnSpec("url_canon", "url_canon"),
            ColumnSpec("domain", "domain"),
            ColumnSpec(
                "fetched_at", "fetched_at",
                transform=epoch_to_datetime_not_null, problem_check=is_epoch_missing, aggregatable=True,
            ),
            ColumnSpec("published_at", "published_at", transform=epoch_to_datetime, aggregatable=True),
            ColumnSpec("title", "title"),
            ColumnSpec("raw_hash", "raw_hash"),
            ColumnSpec("extraction_version", "extraction_version"),
        ],
    ),
    TableSpec(
        source_table="reddit_posts_raw",
        target_schema="raw",
        target_table="reddit_posts",
        pk_columns=["fullname"],
        columns=[
            ColumnSpec("fullname", "fullname"),
            ColumnSpec("subreddit", "subreddit"),
            ColumnSpec("created_utc", "created_utc", transform=epoch_to_datetime, aggregatable=True),
            ColumnSpec("fetched_at", "fetched_at", transform=epoch_to_datetime, aggregatable=True),
            ColumnSpec("title", "title"),
            ColumnSpec("body", "body"),
            ColumnSpec("score", "score", aggregatable=True),
            ColumnSpec("num_comments", "num_comments", aggregatable=True),
            ColumnSpec("raw_hash", "raw_hash"),
            ColumnSpec("extraction_version", "extraction_version"),
        ],
    ),
    TableSpec(
        source_table="x_posts_raw",
        target_schema="raw",
        target_table="x_posts",
        pk_columns=["tweet_id"],
        columns=[
            ColumnSpec("tweet_id", "tweet_id"),
            ColumnSpec("author_id", "author_id"),
            ColumnSpec("conversation_id", "conversation_id"),
            ColumnSpec(
                "created_at", "created_at",
                transform=epoch_to_datetime_not_null, problem_check=is_epoch_missing, aggregatable=True,
            ),
            ColumnSpec(
                "fetched_at", "fetched_at",
                transform=epoch_to_datetime_not_null, problem_check=is_epoch_missing, aggregatable=True,
            ),
            ColumnSpec("text", "text"),
            ColumnSpec("lang", "lang"),
            ColumnSpec("retweet_count", "retweet_count", aggregatable=True),
            ColumnSpec("reply_count", "reply_count", aggregatable=True),
            ColumnSpec("like_count", "like_count", aggregatable=True),
            ColumnSpec("quote_count", "quote_count", aggregatable=True),
            ColumnSpec("place_id", "place_id"),
            ColumnSpec("place_country_code", "place_country_code"),
            ColumnSpec("place_full_name", "place_full_name"),
            ColumnSpec(
                "context_annotations_json", "context_annotations",
                transform=raw_json_text, sql_cast="::jsonb",
            ),
            ColumnSpec("in_reply_to_user_id", "in_reply_to_user_id"),
            ColumnSpec("referenced_tweet_id", "referenced_tweet_id"),
            ColumnSpec("referenced_tweet_type", "referenced_tweet_type"),
            ColumnSpec("raw_hash", "raw_hash"),
            ColumnSpec("extraction_version", "extraction_version"),
            ColumnSpec("is_official_tier", "is_official_tier", transform=int_flag_to_bool),
        ],
    ),
    TableSpec(
        source_table="x_users_raw",
        target_schema="raw",
        target_table="x_users",
        pk_columns=["user_id"],
        columns=[
            ColumnSpec("user_id", "user_id"),
            ColumnSpec("username", "username"),
            ColumnSpec("name", "name"),
            ColumnSpec("location", "location"),
            ColumnSpec("description", "description"),
            ColumnSpec("created_at", "created_at", transform=epoch_to_datetime, aggregatable=True),
            ColumnSpec("followers_count", "followers_count", aggregatable=True),
            ColumnSpec("following_count", "following_count", aggregatable=True),
            ColumnSpec("tweet_count", "tweet_count", aggregatable=True),
            ColumnSpec("listed_count", "listed_count", aggregatable=True),
            ColumnSpec("verified", "verified", transform=int_flag_to_bool),
            ColumnSpec("verified_type", "verified_type"),
            ColumnSpec("profile_image_url", "profile_image_url"),
            ColumnSpec("protected", "protected", transform=int_flag_to_bool),
            ColumnSpec(
                "fetched_at", "fetched_at",
                transform=epoch_to_datetime_not_null, problem_check=is_epoch_missing, aggregatable=True,
            ),
            ColumnSpec("raw_hash", "raw_hash"),
        ],
    ),
    TableSpec(
        source_table="x_api_budget",
        target_schema="ops",
        target_table="x_api_budget",
        pk_columns=["month_key"],
        columns=[
            ColumnSpec("month_key", "month_key"),
            ColumnSpec("post_count", "post_count", aggregatable=True),
            ColumnSpec("user_count", "user_count", aggregatable=True),
            ColumnSpec("request_count", "request_count", aggregatable=True),
            ColumnSpec("estimated_cents", "estimated_cents", aggregatable=True),
            ColumnSpec(
                "last_updated", "last_updated",
                transform=epoch_to_datetime_not_null, problem_check=is_epoch_missing, aggregatable=True,
            ),
        ],
    ),
]


ARCHIVE_TABLES: list[TableSpec] = [
    TableSpec(
        source_table="docs",
        target_schema="archive",
        target_table="docs",
        conflict_action="nothing",
        pk_columns=["doc_id"],
        columns=[
            ColumnSpec("doc_id", "doc_id"),
            ColumnSpec("source_type", "source_type"),
            ColumnSpec("ident", "ident"),
            ColumnSpec("domain_or_subreddit", "domain_or_subreddit"),
            ColumnSpec("published_at", "published_at", transform=epoch_to_datetime, aggregatable=True),
            ColumnSpec("title", "title"),
            ColumnSpec("text", "text"),
            ColumnSpec("raw_hash", "raw_hash"),
            ColumnSpec(
                "metadata_json", "metadata_json",
                transform=_json_best_effort_value, problem_check=_json_best_effort_problem,
                sql_cast="::jsonb", bind_adapter=bind_json_value,
            ),
            ColumnSpec("etl_version", "etl_version"),
        ],
    ),
    TableSpec(
        source_table="prompt_versions",
        target_schema="archive",
        target_table="prompt_versions",
        conflict_action="nothing",
        pk_columns=["prompt_version"],
        columns=[
            ColumnSpec("prompt_version", "prompt_version"),
            ColumnSpec("task_type", "task_type"),
            ColumnSpec("system_prompt", "system_prompt"),
            ColumnSpec("user_prompt_template", "user_prompt_template"),
            ColumnSpec("created_at", "created_at", transform=epoch_to_datetime, aggregatable=True),
            ColumnSpec("notes", "notes"),
        ],
    ),
    TableSpec(
        source_table="ai_outputs",
        target_schema="archive",
        target_table="ai_outputs",
        conflict_action="nothing",
        pk_columns=["output_id"],
        columns=[
            ColumnSpec("output_id", "output_id"),
            ColumnSpec("doc_id", "doc_id"),
            ColumnSpec("model_id", "model_id"),
            ColumnSpec("prompt_version", "prompt_version"),
            ColumnSpec("task_type", "task_type"),
            ColumnSpec(
                "output_json", "output_json",
                transform=_json_best_effort_value, problem_check=_json_best_effort_problem,
                sql_cast="::jsonb", bind_adapter=bind_json_value,
            ),
            ColumnSpec("confidence", "confidence", aggregatable=True, real_type=True),
            ColumnSpec("created_at", "created_at", transform=epoch_to_datetime, aggregatable=True),
            ColumnSpec("inference_method", "inference_method"),
            ColumnSpec("label", "label"),
        ],
    ),
    TableSpec(
        source_table="target_mentions",
        target_schema="archive",
        target_table="target_mentions",
        conflict_action="nothing",
        pk_columns=["mention_id"],
        columns=[
            ColumnSpec("mention_id", "mention_id"),
            ColumnSpec("output_id", "output_id"),
            ColumnSpec("doc_id", "doc_id"),
            ColumnSpec("raw_target", "raw_target"),
            ColumnSpec("entity_key", "entity_key"),
            ColumnSpec("entity_kind", "entity_kind"),
            ColumnSpec("entity_party", "entity_party"),
            ColumnSpec("stance", "stance"),
            ColumnSpec("topic", "topic"),
            ColumnSpec("confidence", "confidence", aggregatable=True, real_type=True),
            ColumnSpec(
                "evidence_json", "evidence_json",
                transform=_json_best_effort_value, problem_check=_json_best_effort_problem,
                sql_cast="::jsonb", bind_adapter=bind_json_value,
            ),
            ColumnSpec("created_at", "created_at", transform=epoch_to_datetime, aggregatable=True),
        ],
    ),
    TableSpec(
        source_table="narratives",
        target_schema="archive",
        target_table="narratives",
        conflict_action="nothing",
        pk_columns=["narrative_id"],
        columns=[
            ColumnSpec("narrative_id", "narrative_id"),
            ColumnSpec("name", "name"),
            ColumnSpec("description", "description"),
            ColumnSpec("first_seen_at", "first_seen_at", transform=epoch_to_datetime, aggregatable=True),
            ColumnSpec("first_seen_doc_id", "first_seen_doc_id"),
            ColumnSpec("created_at", "created_at", transform=epoch_to_datetime, aggregatable=True),
            ColumnSpec("updated_at", "updated_at", transform=epoch_to_datetime, aggregatable=True),
            ColumnSpec(
                "anchor_embedding_json", "anchor_embedding_json",
                transform=_json_best_effort_value, problem_check=_json_best_effort_problem,
                sql_cast="::jsonb", bind_adapter=bind_json_value,
            ),
            ColumnSpec("clustering_mode", "clustering_mode"),
            ColumnSpec("clustering_threshold", "clustering_threshold", aggregatable=True, real_type=True),
            ColumnSpec("embedding_model", "embedding_model"),
        ],
    ),
    TableSpec(
        source_table="narrative_docs",
        target_schema="archive",
        target_table="narrative_docs",
        conflict_action="nothing",
        pk_columns=["assignment_id"],
        columns=[
            ColumnSpec("assignment_id", "assignment_id"),
            ColumnSpec("narrative_id", "narrative_id"),
            ColumnSpec("doc_id", "doc_id"),
            ColumnSpec("discovered_at", "discovered_at", transform=epoch_to_datetime, aggregatable=True),
            ColumnSpec("confidence", "confidence", aggregatable=True, real_type=True),
        ],
    ),
    TableSpec(
        source_table="narrative_citations",
        target_schema="archive",
        target_table="narrative_citations",
        conflict_action="nothing",
        pk_columns=["citation_id"],
        columns=[
            ColumnSpec("citation_id", "citation_id"),
            ColumnSpec("source_doc_id", "source_doc_id"),
            ColumnSpec("target_doc_id", "target_doc_id"),
            ColumnSpec("target_url", "target_url"),
            ColumnSpec("link_type", "link_type"),
            ColumnSpec("discovered_at", "discovered_at", transform=epoch_to_datetime, aggregatable=True),
        ],
    ),
    TableSpec(
        source_table="account_profiles",
        target_schema="archive",
        target_table="account_profiles",
        conflict_action="nothing",
        pk_columns=["profile_id"],
        columns=[
            ColumnSpec("profile_id", "profile_id"),
            ColumnSpec("platform", "platform"),
            ColumnSpec("author_id", "author_id"),
            ColumnSpec("display_name", "display_name"),
            ColumnSpec("tier", "tier"),
            ColumnSpec("classification_method", "classification_method"),
            ColumnSpec("classified_at", "classified_at", transform=epoch_to_datetime, aggregatable=True),
            ColumnSpec("notes", "notes"),
            ColumnSpec("full_name", "full_name"),
            ColumnSpec("party", "party"),
            ColumnSpec("branch", "branch"),
            ColumnSpec("chamber", "chamber"),
            ColumnSpec("state_or_district", "state_or_district"),
            ColumnSpec("office_title", "office_title"),
            ColumnSpec("account_type", "account_type"),
        ],
    ),
    TableSpec(
        source_table="author_bot_scores",
        target_schema="archive",
        target_table="author_bot_scores",
        conflict_action="nothing",
        pk_columns=["platform", "author_id"],
        columns=[
            ColumnSpec("platform", "platform"),
            ColumnSpec("author_id", "author_id"),
            ColumnSpec("score", "score", aggregatable=True, real_type=True),
            ColumnSpec("variance", "variance", aggregatable=True, real_type=True),
            ColumnSpec("sample_count", "sample_count", aggregatable=True),
            ColumnSpec("bot_post_count", "bot_post_count", aggregatable=True),
            ColumnSpec("suspicious_post_count", "suspicious_post_count", aggregatable=True),
            ColumnSpec("llm_text_likelihood_mean", "llm_text_likelihood_mean", aggregatable=True, real_type=True),
            ColumnSpec(
                "stylometric_features_json", "stylometric_features_json",
                transform=_json_best_effort_value, problem_check=_json_best_effort_problem,
                sql_cast="::jsonb", bind_adapter=bind_json_value,
            ),
            ColumnSpec("updated_at", "updated_at", transform=epoch_to_datetime, aggregatable=True),
        ],
    ),
    TableSpec(
        source_table="doc_task_state",
        target_schema="archive",
        target_table="doc_task_state",
        conflict_action="nothing",
        pk_columns=["doc_id", "task_type"],
        columns=[
            ColumnSpec("doc_id", "doc_id"),
            ColumnSpec("task_type", "task_type"),
            ColumnSpec("status", "status"),
            ColumnSpec("prompt_version", "prompt_version"),
            ColumnSpec("attempts", "attempts", aggregatable=True),
            ColumnSpec("last_attempt_at", "last_attempt_at", transform=epoch_to_datetime, aggregatable=True),
        ],
    ),
]

# ai_output_evals is imported only if the source table is nonzero (expected
# empty per the plan). Kept separate from ARCHIVE_TABLES so run_archive() can
# gate it explicitly and log the check either way.
AI_OUTPUT_EVALS_TABLE = TableSpec(
    source_table="ai_output_evals",
    target_schema="archive",
    target_table="ai_output_evals",
    conflict_action="nothing",
    pk_columns=["eval_id"],
    columns=[
        ColumnSpec("eval_id", "eval_id"),
        ColumnSpec("ai_output_id", "ai_output_id"),
        ColumnSpec("doc_id", "doc_id"),
        ColumnSpec("task_type", "task_type"),
        ColumnSpec("human_label", "human_label"),
        ColumnSpec("human_confidence", "human_confidence", aggregatable=True, real_type=True),
        ColumnSpec("is_correct", "is_correct", transform=nullable_int_flag_to_bool),
        ColumnSpec("is_golden", "is_golden", transform=int_flag_to_bool),
        ColumnSpec("reviewer_id", "reviewer_id"),
        ColumnSpec("reviewed_at", "reviewed_at", transform=epoch_to_datetime, aggregatable=True),
        ColumnSpec("notes", "notes"),
    ],
)


# =============================================================================
# SQLite reading helpers
# =============================================================================


def open_sqlite(path: str) -> sqlite3.Connection:
    """Open the source SQLite DB read-only where possible (immutable=0 since
    we only ever SELECT, but no write lock is taken either way)."""
    conn = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
    conn.row_factory = sqlite3.Row
    return conn


def table_exists(conn: sqlite3.Connection, table: str) -> bool:
    row = conn.execute(
        "SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", (table,)
    ).fetchone()
    return row is not None


def source_row_count(conn: sqlite3.Connection, table: str) -> int:
    return conn.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0]


def iter_source_batches(
    conn: sqlite3.Connection, spec: TableSpec, batch_size: int
) -> Iterator[list[sqlite3.Row]]:
    """Stream the source table in fixed-size batches via a single cursor
    (no OFFSET pagination, which would re-scan on every page)."""
    source_cols = ", ".join(c.source for c in spec.columns)
    cur = conn.execute(f"SELECT {source_cols} FROM {spec.source_table}")
    while True:
        batch = cur.fetchmany(batch_size)
        if not batch:
            return
        yield batch


# =============================================================================
# Postgres writing helpers
# =============================================================================


def build_upsert_sql(spec: TableSpec) -> str:
    """INSERT ... ON CONFLICT (pk) DO UPDATE|NOTHING, one placeholder per
    target column in ColumnSpec order, with each column's sql_cast applied."""
    target_cols = ", ".join(c.target for c in spec.columns)
    placeholders = ", ".join(f"%s{c.sql_cast}" for c in spec.columns)
    conflict_cols = ", ".join(spec.pk_columns)
    if spec.conflict_action == "nothing":
        conflict_clause = f"ON CONFLICT ({conflict_cols}) DO NOTHING"
    else:
        non_pk = [c.target for c in spec.columns if c.target not in spec.pk_columns]
        if not non_pk:
            conflict_clause = f"ON CONFLICT ({conflict_cols}) DO NOTHING"
        else:
            set_clause = ", ".join(f"{col} = EXCLUDED.{col}" for col in non_pk)
            conflict_clause = f"ON CONFLICT ({conflict_cols}) DO UPDATE SET {set_clause}"
    return (
        f"INSERT INTO {spec.qualified_target} ({target_cols}) "
        f"VALUES ({placeholders}) {conflict_clause}"
    )


def transform_row(spec: TableSpec, row: sqlite3.Row) -> tuple:
    """Apply every column's transform to one source row, in target-column
    order, producing the CANONICAL row: the single place a source row
    becomes a target row's logical values, shared by the writer and by
    --verify (expected values to compare against what Postgres actually
    stored). Does not apply bind_adapter — see bind_row for the writer-only
    wire-binding step."""
    return tuple(c.transform(row[c.source]) for c in spec.columns)


def bind_row(spec: TableSpec, canonical: tuple) -> tuple:
    """Writer-only step: apply each column's bind_adapter (if any) to a
    canonical row from transform_row, producing the tuple actually passed to
    psycopg's executemany. Kept separate from transform_row so --verify
    compares against canonical Python values, never a psycopg wire wrapper."""
    return tuple(
        c.bind_adapter(v) if c.bind_adapter is not None else v
        for c, v in zip(spec.columns, canonical)
    )


@dataclass
class ProblemCounter:
    """Tallies strict-NOT-NULL-epoch-column and best-effort-JSON problems
    across a whole --raw or --archive run, keyed (schema, table, column).
    --raw uses this to decide whether to fail loud at the end;
    --archive uses it purely for the summary report (never fails)."""

    counts: dict[tuple[str, str, str], int] = field(default_factory=dict)

    def record(self, schema: str, table: str, column: str) -> None:
        key = (schema, table, column)
        self.counts[key] = self.counts.get(key, 0) + 1

    def total(self) -> int:
        return sum(self.counts.values())

    def report_lines(self) -> list[str]:
        return [
            f"  {schema}.{table}.{column}: {n}"
            for (schema, table, column), n in sorted(self.counts.items())
        ]


def migrate_table(
    sqlite_conn: sqlite3.Connection,
    pg_conn: psycopg.Connection,
    spec: TableSpec,
    batch_size: int,
    problems: ProblemCounter,
) -> int:
    """Copy one table batch-by-batch. Transaction granularity: one COMMIT per
    batch, not one per table. Justification (see task report / audit-trail
    entry): raw.pages alone is 1.3M+ rows in production: a single
    table-wide transaction would hold a long-lived lock and, on failure
    midway, roll back everything the run had already done, defeating the
    idempotent-re-run design (ON CONFLICT means a restarted run just
    re-upserts already-migrated rows harmlessly). Per-batch commits bound
    transaction size and let a genuine failure stop the run with most of the
    table already durably migrated, ready for a corrected re-run to pick up
    where it left off."""
    if not table_exists(sqlite_conn, spec.source_table):
        print(f"  skip {spec.source_table}: no such table in source DB")
        return 0
    sql = build_upsert_sql(spec)
    total = 0
    with pg_conn.cursor() as cur:
        for batch in iter_source_batches(sqlite_conn, spec, batch_size):
            values = [bind_row(spec, transform_row(spec, row)) for row in batch]
            for col in spec.columns:
                if col.problem_check is None:
                    continue
                for row in batch:
                    if col.problem_check(row[col.source]):
                        problems.record(spec.target_schema, spec.target_table, col.target)
            cur.executemany(sql, values)
            pg_conn.commit()
            total += len(batch)
    print(f"  {spec.source_table} -> {spec.qualified_target}: {total} rows")
    return total


# =============================================================================
# --raw and --archive orchestration
# =============================================================================


def run_raw(
    sqlite_path: str, pg_dsn: str, batch_size: int, tolerate_not_null_zeroes: bool
) -> int:
    """Migrate raw.* + ops.x_api_budget. Tables are processed in FK-dependency
    order: raw.pages before raw.articles (articles.url_canon references
    pages.url_canon); the rest carry no FK to raw.pages or to each other.
    Returns the process exit code (0 success, 1 if strict NOT-NULL-epoch
    problems were found and not tolerated)."""
    print(f"--raw: {sqlite_path} -> {pg_dsn}")
    problems = ProblemCounter()
    with open_sqlite(sqlite_path) as sqlite_conn, psycopg.connect(pg_dsn) as pg_conn:
        for spec in RAW_TABLES:
            migrate_table(sqlite_conn, pg_conn, spec, batch_size, problems)

    if problems.total() == 0:
        print("No strict NOT-NULL-epoch problems found.")
        return 0

    print(f"\nSTRICT NOT-NULL EPOCH PROBLEMS: {problems.total()} row(s) had a "
          f"0/NULL source epoch for a column that should always be populated:")
    for line in problems.report_lines():
        print(line)
    if tolerate_not_null_zeroes:
        print("--tolerate-not-null-zeroes set: continuing despite the above.")
        return 0
    print(
        "\nFAILING LOUD: rerun with --tolerate-not-null-zeroes to accept this "
        "data quality issue, or fix it upstream and rerun --raw (idempotent "
        "ON CONFLICT means already-migrated rows are simply re-upserted)."
    )
    return 1


def run_archive(sqlite_path: str, pg_dsn: str, batch_size: int) -> int:
    """Verbatim best-effort import of the old derived data into archive.*.
    Never fails on a bad row (invalid JSON / epoch-zero); those are counted
    and reported. ai_output_evals is imported only if the source table is
    nonzero — expected zero per the plan; logs the check either way.
    Always returns 0 (archive import is insurance, not a gate)."""
    print(f"--archive: {sqlite_path} -> {pg_dsn}")
    problems = ProblemCounter()
    with open_sqlite(sqlite_path) as sqlite_conn, psycopg.connect(pg_dsn) as pg_conn:
        for spec in ARCHIVE_TABLES:
            migrate_table(sqlite_conn, pg_conn, spec, batch_size, problems)
        _run_archive_evals(sqlite_conn, pg_conn, batch_size, problems)

    if problems.total():
        print(f"\nArchive best-effort issues (informational, not fatal): "
              f"{problems.total()} row(s):")
        for line in problems.report_lines():
            print(line)
    else:
        print("\nNo epoch-zero / invalid-JSON issues in the archive import.")
    return 0


def _run_archive_evals(
    sqlite_conn: sqlite3.Connection,
    pg_conn: psycopg.Connection,
    batch_size: int,
    problems: ProblemCounter,
) -> None:
    """ai_output_evals is imported only if SELECT count(*) is nonzero.
    Production was confirmed 0 rows as of the 2026-07-22 plan pull; this
    logs the check either way so a future nonzero count is never silently
    skipped."""
    spec = AI_OUTPUT_EVALS_TABLE
    if not table_exists(sqlite_conn, spec.source_table):
        print(f"  {spec.source_table}: no such table in source DB, skipping")
        return
    count = source_row_count(sqlite_conn, spec.source_table)
    print(f"  {spec.source_table} source row count check: {count}")
    if count == 0:
        print(f"  {spec.source_table}: 0 rows, skipping import (expected).")
        return
    print(f"  {spec.source_table}: {count} rows found (unexpected!) — importing.")
    migrate_table(sqlite_conn, pg_conn, spec, batch_size, problems)


# =============================================================================
# --verify battery
#
# Every check below is built from the exact same ColumnSpec.transform
# callables the writers use (transform_row / TIMESTAMP_TRANSFORMS) — see the
# module docstring. There is no independently maintained "expected shape" of
# the target data; if the writer's mapping changes, these checks change with
# it automatically.
# =============================================================================

# The two transform functions that mean "this column is a TIMESTAMPTZ";
# aggregate checks need to know to compare via EXTRACT(EPOCH FROM ...) on the
# Postgres side rather than a plain numeric MIN/MAX/SUM.
TIMESTAMP_TRANSFORMS = {epoch_to_datetime, epoch_to_datetime_not_null}


@dataclass
class VerifyReport:
    """Accumulates every check's outcome. --verify exits 1 if failures is
    non-empty; passes are printed too so a clean run is visibly thorough,
    not just silent."""

    passes: list[str] = field(default_factory=list)
    failures: list[str] = field(default_factory=list)

    def ok(self, message: str) -> None:
        self.passes.append(message)

    def fail(self, message: str) -> None:
        self.failures.append(message)

    def print_summary(self) -> int:
        print(f"\n{len(self.passes)} check(s) passed.")
        if not self.failures:
            print("VERIFY: PASS")
            return 0
        print(f"\n{len(self.failures)} check(s) FAILED:")
        for line in self.failures:
            print(f"  FAIL: {line}")
        print("\nVERIFY: FAIL")
        return 1


@dataclass
class ColumnStats:
    aggregatable: bool = False
    is_timestamp: bool = False
    null_count: int = 0
    minimum: Optional[float] = None
    maximum: Optional[float] = None
    total: float = 0.0


def _numeric_or_epoch(value: Any) -> Optional[float]:
    """Reduce a canonical (post-transform) value to the number an aggregate
    check compares: a datetime becomes its epoch-seconds float, an
    int/float passes through, anything else (text, bool, dict, list) is not
    aggregatable and returns None."""
    if isinstance(value, datetime):
        return value.timestamp()
    if isinstance(value, bool):
        return None
    if isinstance(value, (int, float)):
        return float(value)
    return None


def compute_source_stats(
    sqlite_conn: sqlite3.Connection, spec: TableSpec
) -> dict[str, ColumnStats]:
    """Single streaming pass over the source table computing, per target
    column: how many rows the shared transform maps to NULL, and (for
    aggregatable columns) min/max/sum over the transformed values."""
    stats = {
        c.target: ColumnStats(aggregatable=c.aggregatable, is_timestamp=c.transform in TIMESTAMP_TRANSFORMS)
        for c in spec.columns
    }
    if not table_exists(sqlite_conn, spec.source_table):
        return stats
    for batch in iter_source_batches(sqlite_conn, spec, 5000):
        for row in batch:
            canonical = transform_row(spec, row)
            for col, value in zip(spec.columns, canonical):
                st = stats[col.target]
                if value is None:
                    st.null_count += 1
                    continue
                if not col.aggregatable:
                    continue
                num = _numeric_or_epoch(value)
                if num is None:
                    continue
                st.minimum = num if st.minimum is None else min(st.minimum, num)
                st.maximum = num if st.maximum is None else max(st.maximum, num)
                st.total += num
    return stats


def compute_target_stats(pg_conn: psycopg.Connection, spec: TableSpec) -> dict[str, dict]:
    """One SQL query per table: NULL count for every column, plus MIN/MAX/SUM
    for aggregatable ones (via EXTRACT(EPOCH FROM ...) for timestamp columns,
    plain arithmetic otherwise). SUM casts its operand to double precision:
    Postgres's sum(real) aggregate accumulates in single precision, which
    drifts from the source side's float64 running total by more than the
    comparison tolerance once thousands of rows are summed (archive.*
    confidence/score columns are REAL) — MIN/MAX need no such cast since
    they do not accumulate error."""
    parts = []
    for c in spec.columns:
        parts.append(f"COUNT(*) FILTER (WHERE {c.target} IS NULL) AS \"{c.target}__null\"")
        if c.aggregatable:
            expr = f"EXTRACT(EPOCH FROM {c.target})" if c.transform in TIMESTAMP_TRANSFORMS else c.target
            parts.append(f"MIN({expr}) AS \"{c.target}__min\"")
            parts.append(f"MAX({expr}) AS \"{c.target}__max\"")
            parts.append(f"SUM({expr}::double precision) AS \"{c.target}__sum\"")
    with pg_conn.cursor(row_factory=psycopg.rows.dict_row) as cur:
        cur.execute(f"SELECT {', '.join(parts)} FROM {spec.qualified_target}")
        return dict(cur.fetchone())


def _values_match(expected: Any, actual: Any, tolerance: float = 1e-6) -> bool:
    """Field-level equality for the sample comparison. Two narrow, documented
    exceptions to plain ==: (1) floats compare with a small tolerance for FP
    rounding — the default 1e-6 suits double-precision columns; callers
    comparing a REAL-typed SUM pass a wider `tolerance` (see
    _sum_tolerance), everything else uses the default; (2) a canonical JSON
    value that is still raw text (only raw_json_text's context_annotations
    column produces this — see its docstring for why it is not pre-parsed)
    is compared against Postgres's already-decoded jsonb object by parsing
    it first. Both sides come from the same shared transform; this only
    accounts for the fact that Postgres itself re-normalizes jsonb storage
    and never hands back the exact text that was cast into it."""
    if isinstance(expected, float) or isinstance(actual, float):
        if expected is None or actual is None:
            return expected == actual
        return abs(float(expected) - float(actual)) < tolerance
    if isinstance(expected, str) and isinstance(actual, (dict, list)):
        try:
            return json.loads(expected) == actual
        except (json.JSONDecodeError, TypeError):
            return False
    return expected == actual


def _sum_tolerance(col: ColumnSpec, expected: Optional[float]) -> float:
    """Tolerance for the SUM half of _compare_aggregate: the strict 1e-6
    default for every double-precision/integer column, widened for
    REAL-typed (float4) columns to absorb single-precision storage rounding
    compounding across many summed rows. REAL storage guarantees ~7
    significant decimal digits per value (IEEE 754 single-precision
    epsilon, REAL_TYPE_RELATIVE_EPSILON); the worst-case total rounding
    error summing N same-signed stored values is bounded by
    epsilon * |sum| — so the tolerance scales with the aggregate's own
    magnitude, not with a fixed row-count factor. A genuine data error (a
    wrong or missing row) shifts the sum by orders of magnitude more than
    this and still fails. MIN/MAX never call this — they do not accumulate
    error across rows, so the strict default already suits REAL there."""
    if not col.real_type or expected is None:
        return 1e-6
    return max(1e-6, REAL_TYPE_RELATIVE_EPSILON * abs(expected))


def _source_pk_columns(spec: TableSpec) -> list[str]:
    lookup = {c.target: c.source for c in spec.columns}
    return [lookup[pk] for pk in spec.pk_columns]


def target_row_count(pg_conn: psycopg.Connection, spec: TableSpec) -> int:
    with pg_conn.cursor() as cur:
        cur.execute(f"SELECT COUNT(*) FROM {spec.qualified_target}")
        return cur.fetchone()[0]


def check_row_count(
    sqlite_conn: sqlite3.Connection, pg_conn: psycopg.Connection, spec: TableSpec, report: VerifyReport
) -> None:
    src = source_row_count(sqlite_conn, spec.source_table) if table_exists(sqlite_conn, spec.source_table) else 0
    tgt = target_row_count(pg_conn, spec)
    if src != tgt:
        report.fail(f"{spec.qualified_target}: row count mismatch source={src} target={tgt}")
    else:
        report.ok(f"{spec.qualified_target}: row count matches ({tgt})")


def check_null_and_aggregates(
    sqlite_conn: sqlite3.Connection, pg_conn: psycopg.Connection, spec: TableSpec, report: VerifyReport
) -> None:
    """NULL-count parity for every mapped column, plus min/max/sum parity
    (as epochs for timestamp columns) for columns marked aggregatable."""
    source_stats = compute_source_stats(sqlite_conn, spec)
    target_stats = compute_target_stats(pg_conn, spec)
    for col in spec.columns:
        st = source_stats[col.target]
        target_null = target_stats[f"{col.target}__null"]
        if st.null_count != target_null:
            report.fail(
                f"{spec.qualified_target}.{col.target}: NULL count mismatch "
                f"source={st.null_count} target={target_null}"
            )
        else:
            report.ok(f"{spec.qualified_target}.{col.target}: NULL count matches ({st.null_count})")
        if col.aggregatable:
            _compare_aggregate(spec, col, st, target_stats, report)


def _compare_aggregate(
    spec: TableSpec, col: ColumnSpec, st: ColumnStats, target_stats: dict, report: VerifyReport
) -> None:
    has_data = st.minimum is not None
    pairs = [
        ("min", st.minimum, target_stats[f"{col.target}__min"]),
        ("max", st.maximum, target_stats[f"{col.target}__max"]),
        ("sum", st.total if has_data else None, target_stats[f"{col.target}__sum"]),
    ]
    for label, expected, actual in pairs:
        if expected is None and actual is None:
            continue
        tolerance = _sum_tolerance(col, expected) if label == "sum" else 1e-6
        if expected is None or actual is None or not _values_match(float(expected), float(actual), tolerance):
            report.fail(
                f"{spec.qualified_target}.{col.target}: {label} mismatch "
                f"source={expected} target={actual}"
            )
        else:
            report.ok(f"{spec.qualified_target}.{col.target}: {label} matches ({expected})")


def check_pk_uniqueness(
    sqlite_conn: sqlite3.Connection, pg_conn: psycopg.Connection, spec: TableSpec, report: VerifyReport
) -> None:
    """PK uniqueness proven on both sides independently via GROUP BY ...
    HAVING COUNT(*) > 1, rather than trusting the Postgres PK constraint
    alone — the plan asks for both-sides verification."""
    if table_exists(sqlite_conn, spec.source_table):
        cols = ", ".join(_source_pk_columns(spec))
        dupes = sqlite_conn.execute(
            f"SELECT {cols} FROM {spec.source_table} GROUP BY {cols} HAVING COUNT(*) > 1"
        ).fetchall()
        if dupes:
            report.fail(f"{spec.source_table}: {len(dupes)} duplicate PK value(s) in SOURCE")
        else:
            report.ok(f"{spec.source_table}: source PK uniqueness holds")
    cols = ", ".join(spec.pk_columns)
    with pg_conn.cursor() as cur:
        cur.execute(f"SELECT {cols} FROM {spec.qualified_target} GROUP BY {cols} HAVING COUNT(*) > 1")
        dupes = cur.fetchall()
    if dupes:
        report.fail(f"{spec.qualified_target}: {len(dupes)} duplicate PK value(s) in TARGET")
    else:
        report.ok(f"{spec.qualified_target}: target PK uniqueness holds")


def check_random_sample(
    sqlite_conn: sqlite3.Connection,
    pg_conn: psycopg.Connection,
    spec: TableSpec,
    sample_size: int,
    seed: int,
    report: VerifyReport,
) -> None:
    """Seeded random sample of up to sample_size rows, compared field by
    field through the shared transform (see _values_match for the two
    narrow representation exceptions)."""
    if not table_exists(sqlite_conn, spec.source_table):
        return
    src_pk_cols = _source_pk_columns(spec)
    all_pks = [tuple(r) for r in sqlite_conn.execute(
        f"SELECT {', '.join(src_pk_cols)} FROM {spec.source_table}"
    ).fetchall()]
    if not all_pks:
        return
    chosen = random.Random(seed).sample(all_pks, min(sample_size, len(all_pks)))
    mismatches = _compare_sample_rows(sqlite_conn, pg_conn, spec, src_pk_cols, chosen, report)
    if mismatches == 0:
        report.ok(f"{spec.qualified_target}: {len(chosen)}-row random sample (seed={seed}) matches field-by-field")


def _compare_sample_rows(
    sqlite_conn: sqlite3.Connection,
    pg_conn: psycopg.Connection,
    spec: TableSpec,
    src_pk_cols: list[str],
    chosen: list[tuple],
    report: VerifyReport,
) -> int:
    source_cols = ", ".join(c.source for c in spec.columns)
    target_cols = ", ".join(c.target for c in spec.columns)
    where_src = " AND ".join(f"{col} = ?" for col in src_pk_cols)
    where_tgt = " AND ".join(f"{col} = %s" for col in spec.pk_columns)
    mismatches = 0
    for pk_values in chosen:
        src_row = sqlite_conn.execute(
            f"SELECT {source_cols} FROM {spec.source_table} WHERE {where_src}", pk_values
        ).fetchone()
        if src_row is None:
            continue
        expected = transform_row(spec, src_row)
        with pg_conn.cursor() as cur:
            cur.execute(f"SELECT {target_cols} FROM {spec.qualified_target} WHERE {where_tgt}", pk_values)
            tgt_row = cur.fetchone()
        if tgt_row is None:
            mismatches += 1
            report.fail(f"{spec.qualified_target}: sample pk {pk_values} missing in target")
            continue
        for col, exp_val, act_val in zip(spec.columns, expected, tgt_row):
            if not _values_match(exp_val, act_val):
                mismatches += 1
                report.fail(
                    f"{spec.qualified_target}: sample pk {pk_values} column {col.target} "
                    f"expected={exp_val!r} actual={act_val!r}"
                )
    return mismatches


def _hash_file_exists(raw_files_dir: str, file_hash: str) -> bool:
    if len(file_hash) < 2:
        return False
    subdir = Path(raw_files_dir) / file_hash[:2]
    return subdir.is_dir() and any(subdir.glob(f"{file_hash}.*"))


def check_raw_hash_files(
    pg_conn: psycopg.Connection, spec: TableSpec, raw_files_dir: str, report: VerifyReport
) -> None:
    """Every distinct raw_hash on this table must resolve to a file under
    raw_files_dir/<hash[:2]>/<hash>.<ext> (content-addressed storage layout,
    see ingest/internal/storage/rawstore/rawstore.go). Only applies to
    tables that carry a raw_hash column."""
    if not any(c.target == "raw_hash" for c in spec.columns):
        return
    with pg_conn.cursor() as cur:
        cur.execute(f"SELECT DISTINCT raw_hash FROM {spec.qualified_target} WHERE raw_hash IS NOT NULL")
        hashes = [r[0] for r in cur.fetchall()]
    missing = [h for h in hashes if not _hash_file_exists(raw_files_dir, h)]
    if missing:
        sample = ", ".join(missing[:10])
        report.fail(
            f"{spec.qualified_target}: {len(missing)}/{len(hashes)} raw_hash value(s) resolve to no "
            f"file under {raw_files_dir} (first 10: {sample})"
        )
    else:
        report.ok(
            f"{spec.qualified_target}: all {len(hashes)} distinct raw_hash value(s) "
            f"resolve to a file under {raw_files_dir}"
        )


def verify_table(
    sqlite_conn: sqlite3.Connection,
    pg_conn: psycopg.Connection,
    spec: TableSpec,
    sample_size: int,
    seed: int,
    report: VerifyReport,
) -> None:
    check_row_count(sqlite_conn, pg_conn, spec, report)
    check_null_and_aggregates(sqlite_conn, pg_conn, spec, report)
    check_pk_uniqueness(sqlite_conn, pg_conn, spec, report)
    check_random_sample(sqlite_conn, pg_conn, spec, sample_size, seed, report)


def verify_all(sqlite_path: str, pg_dsn: str, raw_files_dir: str, sample_size: int, seed: int) -> int:
    """Runs the full battery: raw+ops tables always; archive tables only
    when populated ("archive when present" per the plan) — an empty archive
    table is treated as "not imported yet", not a failure."""
    report = VerifyReport()
    with open_sqlite(sqlite_path) as sqlite_conn, psycopg.connect(pg_dsn) as pg_conn:
        for spec in RAW_TABLES:
            print(f"Verifying {spec.qualified_target} ...")
            verify_table(sqlite_conn, pg_conn, spec, sample_size, seed, report)
            check_raw_hash_files(pg_conn, spec, raw_files_dir, report)
        for spec in list(ARCHIVE_TABLES) + [AI_OUTPUT_EVALS_TABLE]:
            if target_row_count(pg_conn, spec) == 0:
                print(f"{spec.qualified_target}: not populated, skipping (run --archive first if unexpected)")
                continue
            print(f"Verifying {spec.qualified_target} ...")
            verify_table(sqlite_conn, pg_conn, spec, sample_size, seed, report)
    return report.print_summary()


# =============================================================================
# CLI
# =============================================================================


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Migrate precious data from the legacy SQLite DB into the north-star Postgres schema."
    )
    parser.add_argument("--sqlite", required=True, help="Path to the source SQLite database file")
    parser.add_argument("--pg", required=True, dest="pg_dsn", help="Postgres DSN, e.g. postgresql://user:pass@host:5432/db")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--raw", action="store_true", help="Migrate raw.* + ops.x_api_budget (re-runnable)")
    mode.add_argument("--archive", action="store_true", help="Verbatim best-effort import into archive.*")
    mode.add_argument("--verify", action="store_true", help="Read-only verification battery; exits 1 on any failure")
    parser.add_argument("--batch-size", type=int, default=DEFAULT_BATCH_SIZE, help=f"Rows per INSERT batch (default {DEFAULT_BATCH_SIZE})")
    parser.add_argument(
        "--tolerate-not-null-zeroes", action="store_true",
        help="--raw only: do not fail loud when a NOT NULL timestamp column had a 0/NULL source epoch",
    )
    parser.add_argument("--raw-files-dir", default=DEFAULT_RAW_FILES_DIR, help=f"--verify only: content-addressed raw file root (default {DEFAULT_RAW_FILES_DIR})")
    parser.add_argument("--sample-size", type=int, default=DEFAULT_SAMPLE_SIZE, help=f"--verify only: rows sampled per table (default {DEFAULT_SAMPLE_SIZE})")
    parser.add_argument("--seed", type=int, default=DEFAULT_SEED, help=f"--verify only: RNG seed for the sample (default {DEFAULT_SEED})")
    return parser


def main(argv: Optional[list[str]] = None) -> int:
    args = build_arg_parser().parse_args(argv)
    if not Path(args.sqlite).exists():
        print(f"error: sqlite DB not found at {args.sqlite}", file=sys.stderr)
        return 2
    if args.raw:
        return run_raw(args.sqlite, args.pg_dsn, args.batch_size, args.tolerate_not_null_zeroes)
    if args.archive:
        return run_archive(args.sqlite, args.pg_dsn, args.batch_size)
    return verify_all(args.sqlite, args.pg_dsn, args.raw_files_dir, args.sample_size, args.seed)


if __name__ == "__main__":
    sys.exit(main())
