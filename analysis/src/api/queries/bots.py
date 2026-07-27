"""
Live query module for GET /bot-activity. Aggregates
`analysis.bot_signals` + `analysis.author_bot_scores` +
`analysis.author_leans` + `analysis.narrative_docs` over an arbitrary
[start, end] range -- no precomputed rollup (Phase 9 strictly-live,
`docs/audit-trail/analysis/2026-07-24-phase9-prewave.md`).
"""

from __future__ import annotations

from collections import Counter
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Set, Tuple

from analysis.src.api.models.bots import (
    AccountAgeBucket,
    BehavioralSignalBucket,
    BotActivityResponse,
    BotPushedNarrative,
    EntityBotRate,
    FlaggedAccount,
    PostingCadenceBucket,
)
from analysis.src.api.models.common import LeanLabel, RangeMeta, SampleDocModel
from analysis.src.api.queries import base
from analysis.src.api.queries.constants import (
    BOT_FLAGGED_SHARE_EXCLUSION,
    MAX_DISTRIBUTION_SAMPLES_PER_BUCKET,
    MAX_SAMPLED_AUTHOR_CARDS,
    MAX_SAMPLES_PER_TARGET,
    MIN_SAMPLED_AUTHOR_FOLLOWERS,
    MIN_SAMPLED_AUTHOR_POSTS,
    MIN_TARGET_SAMPLE_N,
    SNIPPET_MAX_CHARS,
)
from analysis.src.common import db
from analysis.src.common.settings import get_settings

# Account-age buckets for bot-flagged authors. Bounds are day counts; the
# last label has no upper bound (fallback in _age_bucket_label).
_AGE_BUCKET_LABELS: Tuple[str, ...] = (
    "< 7 days", "7-30 days", "30-90 days", "90-365 days", "1-3 years", "3+ years",
)
_AGE_BUCKET_DAY_BOUNDS: Tuple[int, ...] = (7, 30, 90, 365, 365 * 3)
_UNKNOWN_AGE_LABEL = "unknown"

# Panel-specific caps not covered by a foundation constant.
_TOP_N_NARRATIVES = 5
_MAX_FLAGGED_DOCS = MAX_DISTRIBUTION_SAMPLES_PER_BUCKET
_MAX_SAMPLES_PER_ACCOUNT = MAX_SAMPLES_PER_TARGET
_MAX_SAMPLES_PER_NARRATIVE = MAX_SAMPLES_PER_TARGET


def _time_filter(
    start: Optional[datetime], end: Optional[datetime], params: Dict[str, Any],
    column: str = "d.published_at",
) -> str:
    """SQL fragment (leading ' AND ...' or '') applying an optional
    [start, end] bound on `column`, adding whichever bounds are present to
    `params`. Either bound may be None (unbounded) -- windows scope
    denominators, they never gate document existence. Matches
    queries/narratives.py's `_time_filter` convention."""
    clauses = []
    if start is not None:
        clauses.append(f"{column} >= %(_range_start)s")
        params["_range_start"] = start
    if end is not None:
        clauses.append(f"{column} <= %(_range_end)s")
        params["_range_end"] = end
    return (" AND " + " AND ".join(clauses)) if clauses else ""


def _fetch_bot_docs(conn, start, end, min_conf: float) -> List[Dict[str, Any]]:
    params: Dict[str, Any] = {"min_conf": min_conf, "snip": SNIPPET_MAX_CHARS}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT bs.doc_id, bs.label::text AS bot_label, r.confidence, r.model_id,
               d.author_id, d.published_at, d.source_url, d.admission_class::text AS admission_class,
               LEFT(d.body, %(snip)s) AS snippet
        FROM analysis.bot_signals bs
        JOIN analysis.runs r ON r.run_id = bs.run_id AND r.is_current AND r.status = 'done'::analysis.run_status
        JOIN corpus.documents d ON d.doc_id = bs.doc_id
        WHERE r.confidence >= %(min_conf)s{time_clause}
    """
    return conn.execute(sql, params).fetchall()


def _fetch_behavioral_breakdown(conn, start, end, min_conf: float) -> List[Dict[str, Any]]:
    params: Dict[str, Any] = {"min_conf": min_conf}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT bs.label::text AS bot_label, COUNT(*) AS doc_count,
               AVG(bs.llm_text_likelihood) AS avg_llm_text_likelihood,
               AVG(bs.burstiness) AS avg_burstiness,
               AVG(bs.type_token_ratio) AS avg_type_token_ratio,
               AVG(bs.template_score) AS avg_template_score
        FROM analysis.bot_signals bs
        JOIN analysis.runs r ON r.run_id = bs.run_id AND r.is_current AND r.status = 'done'::analysis.run_status
        JOIN corpus.documents d ON d.doc_id = bs.doc_id
        WHERE r.confidence >= %(min_conf)s{time_clause}
        GROUP BY bs.label
    """
    return conn.execute(sql, params).fetchall()


def _fetch_author_bot_scores(conn, author_ids: Set[int]) -> Dict[int, Dict[str, Any]]:
    if not author_ids:
        return {}
    sql = """
        SELECT abs.author_id, abs.sample_count,
               (abs.bot_post_count + abs.suspicious_post_count)::float
                   / NULLIF(abs.sample_count, 0) AS flagged_share,
               a.platform::text AS platform, a.handle, a.display_name,
               a.followers_count, a.account_created_at
        FROM analysis.author_bot_scores abs
        JOIN corpus.authors a ON a.author_id = abs.author_id
        WHERE abs.author_id = ANY(%(author_ids)s)
    """
    rows = conn.execute(sql, {"author_ids": list(author_ids)}).fetchall()
    return {row["author_id"]: row for row in rows}


def _fetch_author_leans(conn, author_ids: Set[int]) -> Dict[int, Dict[str, Any]]:
    if not author_ids:
        return {}
    sql = """
        SELECT author_id, lean::text AS lean, lean_share, lean_confidence, stance_sample_count
        FROM analysis.author_leans
        WHERE author_id = ANY(%(author_ids)s) AND lean != 'unknown'::corpus.political_lean
    """
    rows = conn.execute(sql, {"author_ids": list(author_ids)}).fetchall()
    return {row["author_id"]: row for row in rows}


def _fetch_author_entities(conn, author_ids: Set[int]) -> Dict[int, Dict[str, Any]]:
    if not author_ids:
        return {}
    sql = """
        SELECT ap.author_id, e.entity_id, e.entity_key, e.kind::text AS kind, e.display_name
        FROM corpus.author_profiles ap
        JOIN corpus.entities e ON e.entity_id = ap.entity_id
        WHERE ap.author_id = ANY(%(author_ids)s) AND ap.entity_id IS NOT NULL
    """
    rows = conn.execute(sql, {"author_ids": list(author_ids)}).fetchall()
    return {row["author_id"]: row for row in rows}


def _fetch_narrative_member_docs(conn, start, end) -> List[Dict[str, Any]]:
    params: Dict[str, Any] = {"snip": SNIPPET_MAX_CHARS}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT nd.narrative_id, n.name, nd.doc_id, nd.confidence AS member_confidence,
               d.author_id, d.published_at, d.source_url, d.admission_class::text AS admission_class,
               LEFT(d.body, %(snip)s) AS snippet
        FROM analysis.narrative_docs nd
        JOIN analysis.narratives n ON n.narrative_id = nd.narrative_id
        JOIN corpus.documents d ON d.doc_id = nd.doc_id
        WHERE true{time_clause}
    """
    return conn.execute(sql, params).fetchall()


def _fetch_model_ids(conn, start, end) -> List[str]:
    """Distinct `analysis.runs.model_id` among current, succeeded 'bot'
    task runs in range -- RangeMeta's methodology-change signal."""
    params: Dict[str, Any] = {}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT DISTINCT r.model_id
        FROM analysis.runs r
        JOIN corpus.documents d ON d.doc_id = r.doc_id
        WHERE r.task = 'bot'::analysis.task AND r.is_current
          AND r.status = 'done'::analysis.run_status{time_clause}
    """
    rows = conn.execute(sql, params).fetchall()
    return sorted(row["model_id"] for row in rows if row["model_id"])


def _age_bucket_label(now: datetime, created_at: Optional[datetime]) -> str:
    if created_at is None:
        return _UNKNOWN_AGE_LABEL
    age_days = (now - created_at).days
    for bound, label in zip(_AGE_BUCKET_DAY_BOUNDS, _AGE_BUCKET_LABELS):
        if age_days < bound:
            return label
    return _AGE_BUCKET_LABELS[-1]


def _build_age_buckets(
    bot_rows: List[Dict[str, Any]], bot_scores: Dict[int, Dict[str, Any]], now: datetime,
) -> List[AccountAgeBucket]:
    """Buckets DISTINCT bot-flagged (label='bot') authors' account age --
    one author counted once even with several flagged docs in range."""
    counts: Dict[str, int] = {label: 0 for label in _AGE_BUCKET_LABELS}
    counts[_UNKNOWN_AGE_LABEL] = 0
    seen_authors: Set[int] = set()
    for row in bot_rows:
        if row["bot_label"] != "bot" or row["author_id"] is None:
            continue
        if row["author_id"] in seen_authors:
            continue
        seen_authors.add(row["author_id"])
        created_at = bot_scores.get(row["author_id"], {}).get("account_created_at")
        counts[_age_bucket_label(now, created_at)] += 1
    return [AccountAgeBucket(age_range=label, account_count=count) for label, count in counts.items()]


def _build_entity_bot_rates(
    bot_rows: List[Dict[str, Any]], entities: Dict[int, Dict[str, Any]],
) -> List[EntityBotRate]:
    accum: Dict[int, Dict[str, Any]] = {}
    for row in bot_rows:
        author_id = row["author_id"]
        if author_id is None:
            continue
        entity = entities.get(author_id)
        if entity is None:
            continue
        slot = accum.setdefault(entity["entity_id"], {
            "entity_id": entity["entity_id"], "entity_key": entity["entity_key"],
            "kind": entity["kind"],
            "display_name": entity["display_name"], "total": 0, "bot": 0,
        })
        slot["total"] += 1
        if row["bot_label"] == "bot":
            slot["bot"] += 1

    items = [
        EntityBotRate(
            entity_id=slot["entity_id"], entity_key=slot["entity_key"],
            kind=slot["kind"], display_name=slot["display_name"],
            total_docs=slot["total"], bot_docs=slot["bot"],
            bot_rate_pct=round(slot["bot"] / slot["total"] * 100, 1) if slot["total"] else 0.0,
        )
        for slot in accum.values()
    ]
    items.sort(key=lambda item: (-item.bot_rate_pct, -item.total_docs))
    return items[:MAX_SAMPLED_AUTHOR_CARDS]


def _build_flagged_accounts(
    bot_rows: List[Dict[str, Any]], bot_scores: Dict[int, Dict[str, Any]], leans: Dict[int, Dict[str, Any]],
) -> List[FlaggedAccount]:
    """Example bot-scored accounts, gated on the same footprint floor as
    the pre-redesign entity cards (MIN_SAMPLED_AUTHOR_POSTS/FOLLOWERS) so
    a one-post, no-follower account never gets its own card."""
    docs_by_author: Dict[int, List[Dict[str, Any]]] = {}
    for row in bot_rows:
        if row["bot_label"] != "bot" or row["author_id"] is None:
            continue
        docs_by_author.setdefault(row["author_id"], []).append(row)

    eligible = [
        (author_id, info) for author_id, info in bot_scores.items()
        if author_id in docs_by_author
        and (info["flagged_share"] or 0.0) >= BOT_FLAGGED_SHARE_EXCLUSION
        and info["sample_count"] >= MIN_SAMPLED_AUTHOR_POSTS
        and (info["followers_count"] or 0) >= MIN_SAMPLED_AUTHOR_FOLLOWERS
    ]
    eligible.sort(key=lambda pair: -(pair[1]["flagged_share"] or 0.0))

    accounts: List[FlaggedAccount] = []
    for author_id, info in eligible[:MAX_SAMPLED_AUTHOR_CARDS]:
        docs = sorted(docs_by_author[author_id], key=lambda r: -(r["confidence"] or 0.0))
        samples = [
            SampleDocModel(**base.build_sample_doc(row, admission_class=row["admission_class"]))
            for row in docs[:_MAX_SAMPLES_PER_ACCOUNT]
        ]
        lean_row = leans.get(author_id)
        lean = (
            LeanLabel(
                kind="derived", value=lean_row["lean"], lean_share=lean_row["lean_share"],
                confidence=lean_row["lean_confidence"], sample_count=lean_row["stance_sample_count"],
            )
            if lean_row is not None else None
        )
        accounts.append(FlaggedAccount(
            author_id=author_id, platform=info["platform"], handle=info["handle"],
            display_name=info["display_name"],
            flagged_post_share=round(info["flagged_share"], 3),
            sample_count=info["sample_count"], followers_count=info["followers_count"],
            lean=lean, samples=samples,
        ))
    return accounts


def _build_flagged_docs(bot_rows: List[Dict[str, Any]]) -> List[SampleDocModel]:
    flagged = [row for row in bot_rows if row["bot_label"] == "bot"]
    flagged.sort(key=lambda row: -(row["confidence"] or 0.0))
    return [
        SampleDocModel(**base.build_sample_doc(row, admission_class=row["admission_class"]))
        for row in flagged[:_MAX_FLAGGED_DOCS]
    ]


def _build_bot_pushed_narratives(
    narrative_rows: List[Dict[str, Any]], bot_scores: Dict[int, Dict[str, Any]],
) -> List[BotPushedNarrative]:
    """Top narratives ranked by the fraction of in-range member docs
    authored by a bot-scored account. A narrative with fewer than
    MIN_TARGET_SAMPLE_N in-range member docs is dropped -- a 2-doc
    narrative reading '100% bot-pushed' is noise, not signal."""
    groups: Dict[int, Dict[str, Any]] = {}
    for row in narrative_rows:
        group = groups.setdefault(row["narrative_id"], {"name": row["name"], "docs": []})
        group["docs"].append(row)

    narratives: List[BotPushedNarrative] = []
    for narrative_id, group in groups.items():
        docs = group["docs"]
        total = len(docs)
        if total < MIN_TARGET_SAMPLE_N:
            continue
        bot_authored = [
            row for row in docs
            if row["author_id"] is not None
            and (bot_scores.get(row["author_id"], {}).get("flagged_share") or 0.0)
                >= BOT_FLAGGED_SHARE_EXCLUSION
        ]
        samples_source = [row for row in bot_authored if row["member_confidence"] is not None]
        samples_source.sort(key=lambda row: -row["member_confidence"])
        samples = [
            SampleDocModel(**base.build_sample_doc(
                {**row, "confidence": row["member_confidence"]},
                admission_class=row["admission_class"],
            ))
            for row in samples_source[:_MAX_SAMPLES_PER_NARRATIVE]
        ]
        narratives.append(BotPushedNarrative(
            narrative_id=narrative_id, name=group["name"] or "",
            member_doc_count=total, bot_authored_doc_count=len(bot_authored),
            bot_fraction_pct=round(len(bot_authored) / total * 100, 1), samples=samples,
        ))
    narratives.sort(key=lambda n: (-n.bot_fraction_pct, -n.member_doc_count))
    return narratives[:_TOP_N_NARRATIVES]


def _build_posting_cadence(bot_rows: List[Dict[str, Any]]) -> List[PostingCadenceBucket]:
    """24-bucket UTC hour-of-day histogram over bot-flagged (label='bot')
    docs in range -- pure post-processing over the same `bot_rows` already
    fetched for the rest of the panel (no extra query), matching the
    retired aggregator's hourly_distribution (a single combined histogram,
    not one per author)."""
    counts = Counter(
        row["published_at"].hour for row in bot_rows
        if row["bot_label"] == "bot" and row["published_at"] is not None
    )
    return [PostingCadenceBucket(hour=hour, doc_count=counts.get(hour, 0)) for hour in range(24)]


def _compute_coordination_index(cadence: List[PostingCadenceBucket]) -> float:
    """Reimplements the retired
    reporting/aggregators/bot/metrics.py::_compute_coordination_index():
    the busiest hour's share of all bot-flagged posts in range. 0.0 when
    there is no bot-flagged posting activity at all (empty histogram)."""
    total = sum(bucket.doc_count for bucket in cadence)
    if total == 0:
        return 0.0
    return round(max(bucket.doc_count for bucket in cadence) / total, 3)


def get_bot_activity(
    *, start: Optional[datetime], end: Optional[datetime], window_label: Optional[str],
) -> BotActivityResponse:
    """Aggregates the Bot Detector panel live over `[start, end]` (either
    bound may be None for an unbounded/'all' range). `window_label` only
    feeds `RangeMeta.window` (None for a custom from/to range) -- it does
    not affect the SQL bounds, which are always the resolved `start`/`end`."""
    min_conf = get_settings().aggregation_min_confidence
    now = datetime.now(timezone.utc)

    with db.connection() as conn:
        bot_rows = _fetch_bot_docs(conn, start, end, min_conf)
        breakdown_rows = _fetch_behavioral_breakdown(conn, start, end, min_conf)
        narrative_rows = _fetch_narrative_member_docs(conn, start, end)
        # Union of both scans' authors: bot_pushed_narratives needs a
        # score for every narrative-member author, not just the subset
        # that also has its own bot_signals row in range (a doc can be a
        # narrative member without itself being bot-detection-scored).
        author_ids = {
            row["author_id"] for row in (*bot_rows, *narrative_rows) if row["author_id"] is not None
        }
        bot_scores = _fetch_author_bot_scores(conn, author_ids)
        leans = _fetch_author_leans(conn, author_ids)
        entities = _fetch_author_entities(conn, author_ids)
        model_ids = _fetch_model_ids(conn, start, end)

    analyzed_doc_count = len(bot_rows)
    bot_scored_doc_count = sum(
        1 for row in bot_rows
        if row["author_id"] is not None
        and (bot_scores.get(row["author_id"], {}).get("flagged_share") or 0.0)
            >= BOT_FLAGGED_SHARE_EXCLUSION
    )
    automation_rate_pct = (
        round(bot_scored_doc_count / analyzed_doc_count * 100, 1) if analyzed_doc_count else 0.0
    )

    behavioral_signals = [
        BehavioralSignalBucket(
            label=row["bot_label"], doc_count=row["doc_count"],
            avg_llm_text_likelihood=row["avg_llm_text_likelihood"],
            avg_burstiness=row["avg_burstiness"],
            avg_type_token_ratio=row["avg_type_token_ratio"],
            avg_template_score=row["avg_template_score"],
        )
        for row in breakdown_rows
    ]

    sampled_count, official_count = base.split_admission_counts(bot_rows)
    range_meta = RangeMeta(
        window=window_label, start=start, end=end,
        sampled_doc_count=sampled_count, official_record_doc_count=official_count,
        model_ids=model_ids,
    )
    posting_cadence = _build_posting_cadence(bot_rows)

    return BotActivityResponse(
        range=range_meta,
        analyzed_doc_count=analyzed_doc_count,
        bot_scored_doc_count=bot_scored_doc_count,
        automation_rate_pct=automation_rate_pct,
        behavioral_signals=behavioral_signals,
        account_age_buckets=_build_age_buckets(bot_rows, bot_scores, now),
        coordination_index=_compute_coordination_index(posting_cadence),
        posting_cadence=posting_cadence,
        by_entity=_build_entity_bot_rates(bot_rows, entities),
        bot_pushed_narratives=_build_bot_pushed_narratives(narrative_rows, bot_scores),
        flagged_accounts=_build_flagged_accounts(bot_rows, bot_scores, leans),
        flagged_docs=_build_flagged_docs(bot_rows),
    )
