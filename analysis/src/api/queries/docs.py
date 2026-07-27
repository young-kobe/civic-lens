"""
Live query module for the universal document drill-down (Phase 9
strictly-live API): resolves one corpus.documents row by id with NO time
predicate whatsoever (owner decision 2026-07-24) -- age never gates
whether a document exists, only aggregate windows elsewhere do.
"""

from __future__ import annotations

from typing import Any, Dict, List, Optional

from analysis.src.common import db

_CORE_SQL = """
    SELECT d.doc_id, d.source_type::text AS source_type, d.domain_or_subreddit, d.author_id,
           d.published_at, d.title, d.body, d.source_url,
           d.admission_class::text AS admission_class,
           a.handle AS author_handle, a.display_name AS author_display_name,
           a.profile_image_url AS author_profile_image_url, a.verified AS author_verified
    FROM corpus.documents d
    LEFT JOIN corpus.authors a ON a.author_id = d.author_id
    WHERE d.doc_id = %(doc_id)s
"""

_CITATIONS_OUT_SQL = """
    SELECT c.link_type::text AS link_type, c.target_doc_id AS doc_id, c.target_url AS target_url,
           t.source_url AS source_url, t.published_at AS published_at,
           t.admission_class::text AS admission_class
    FROM analysis.citations c
    LEFT JOIN corpus.documents t ON t.doc_id = c.target_doc_id
    WHERE c.source_doc_id = %(doc_id)s
    ORDER BY c.citation_id
"""

# source_doc_id is NOT NULL on analysis.citations -- the citing side is
# always an ingested doc, never a bare URL, so this join is always an INNER
# join and always resolves.
_CITATIONS_IN_SQL = """
    SELECT c.link_type::text AS link_type, c.source_doc_id AS doc_id,
           s.source_url AS source_url, s.published_at AS published_at,
           s.admission_class::text AS admission_class
    FROM analysis.citations c
    JOIN corpus.documents s ON s.doc_id = c.source_doc_id
    WHERE c.target_doc_id = %(doc_id)s
    ORDER BY c.citation_id
"""

_RUNS_SQL = """
    SELECT r.run_id AS run_id, r.task::text AS task, r.confidence AS confidence,
           r.model_id AS model_id, pv.prompt_version AS prompt_version
    FROM analysis.runs r
    LEFT JOIN analysis.prompt_versions pv ON pv.prompt_version_id = r.prompt_version_id
    WHERE r.doc_id = %(doc_id)s AND r.is_current AND r.status = 'done'::analysis.run_status
    ORDER BY r.task
"""


def get_document(doc_id: int) -> Optional[Dict[str, Any]]:
    """One document's full drill-down payload, or None if `doc_id` doesn't
    exist (the router maps that to 404)."""
    with db.connection() as conn:
        core = conn.execute(_CORE_SQL, {"doc_id": doc_id}).fetchone()
        if core is None:
            return None
        extras = _fetch_subtype_extras(conn, doc_id, core["source_type"])
        results = [
            _analysis_result(conn, row) for row in conn.execute(_RUNS_SQL, {"doc_id": doc_id}).fetchall()
        ]
        citations_out = conn.execute(_CITATIONS_OUT_SQL, {"doc_id": doc_id}).fetchall()
        citations_in = conn.execute(_CITATIONS_IN_SQL, {"doc_id": doc_id}).fetchall()

    return {
        **core,
        **extras,
        "analysis_results": results,
        "citations_out": [dict(row) for row in citations_out],
        "citations_in": [dict(row) for row in citations_in],
    }


_EMPTY_EXTRAS = {"news_extras": None, "reddit_extras": None, "x_post_extras": None}


def _fetch_subtype_extras(conn, doc_id: int, source_type: str) -> Dict[str, Any]:
    """All three `*_extras` keys are always present -- exactly one
    populated, matching `source_type`, the other two explicitly None
    (never simply absent) so a raw-dict consumer never has to distinguish
    "not this subtype" from "key missing"."""
    extras = dict(_EMPTY_EXTRAS)
    if source_type == "news":
        row = conn.execute(
            "SELECT domain, extraction_version, outlet_entity_id FROM corpus.news_articles "
            "WHERE doc_id = %(doc_id)s",
            {"doc_id": doc_id},
        ).fetchone()
        extras["news_extras"] = dict(row) if row else None
    elif source_type == "reddit_post":
        row = conn.execute(
            "SELECT subreddit, score, num_comments, subreddit_entity_id FROM corpus.reddit_posts "
            "WHERE doc_id = %(doc_id)s",
            {"doc_id": doc_id},
        ).fetchone()
        extras["reddit_extras"] = dict(row) if row else None
    elif source_type == "x_post":
        row = conn.execute(
            "SELECT tweet_id, conversation_id, lang, place_country_code, retweet_count, "
            "reply_count, like_count, quote_count, is_official_tier, referenced_tweet_id, "
            "referenced_tweet_type FROM corpus.x_posts WHERE doc_id = %(doc_id)s",
            {"doc_id": doc_id},
        ).fetchone()
        extras["x_post_extras"] = dict(row) if row else None
    else:
        raise ValueError(f"unknown source_type: {source_type!r}")
    return extras


def _analysis_result(conn, run_row: Dict[str, Any]) -> Dict[str, Any]:
    fetcher = _TASK_FIELD_FETCHERS.get(run_row["task"])
    fields = fetcher(conn, run_row["run_id"]) if fetcher is not None else {}
    return {
        "task": run_row["task"],
        "run_id": run_row["run_id"],
        "confidence": run_row["confidence"],
        "model_id": run_row["model_id"],
        "prompt_version": run_row["prompt_version"],
        "fields": fields,
    }


def _bot_fields(conn, run_id: int) -> Dict[str, Any]:
    row = conn.execute(
        "SELECT label::text AS label, llm_text_likelihood, burstiness, "
        "type_token_ratio, template_score FROM analysis.bot_signals WHERE run_id = %(run_id)s",
        {"run_id": run_id},
    ).fetchone()
    return dict(row) if row else {}


def _text_fields(conn, run_id: int) -> Dict[str, Any]:
    sentiment_row = conn.execute(
        "SELECT label::text AS label, score, sarcasm_detected, evidence_spans "
        "FROM analysis.sentiment_results WHERE run_id = %(run_id)s",
        {"run_id": run_id},
    ).fetchone()
    return {
        "sentiment": dict(sentiment_row) if sentiment_row else None,
    }


def _targets_fields(conn, run_id: int) -> Dict[str, Any]:
    rows = conn.execute(
        "SELECT raw_target, entity_id, stance::text AS stance, topic, confidence, evidence_spans "
        "FROM analysis.target_mentions WHERE run_id = %(run_id)s",
        {"run_id": run_id},
    ).fetchall()
    return {"target_mentions": [dict(row) for row in rows]}


def _propaganda_fields(conn, run_id: int) -> Dict[str, Any]:
    result_row = conn.execute(
        "SELECT density, summary, techniques_validated, techniques_dropped "
        "FROM analysis.propaganda_results WHERE run_id = %(run_id)s",
        {"run_id": run_id},
    ).fetchone()
    techniques = conn.execute(
        "SELECT technique::text AS technique, evidence_span, confidence "
        "FROM analysis.propaganda_techniques WHERE run_id = %(run_id)s",
        {"run_id": run_id},
    ).fetchall()
    fields = dict(result_row) if result_row else {}
    fields["techniques"] = [dict(row) for row in techniques]
    return fields


def _claims_fields(conn, run_id: int) -> Dict[str, Any]:
    rows = conn.execute(
        "SELECT claim_text, topic, confidence FROM analysis.claims WHERE run_id = %(run_id)s",
        {"run_id": run_id},
    ).fetchall()
    return {"claims": [dict(row) for row in rows]}


def _citations_fields(conn, run_id: int) -> Dict[str, Any]:
    row = conn.execute(
        "SELECT COUNT(*) AS citation_count FROM analysis.citations WHERE run_id = %(run_id)s",
        {"run_id": run_id},
    ).fetchone()
    return {"citation_count": row["citation_count"] if row else 0}


# account_tier is author-scoped (no doc_id), so it never appears in
# _RUNS_SQL's results for a doc and has no entry here.
_TASK_FIELD_FETCHERS = {
    "bot": _bot_fields,
    "text": _text_fields,
    "targets": _targets_fields,
    "propaganda": _propaganda_fields,
    "claims": _claims_fields,
    "citations": _citations_fields,
}
