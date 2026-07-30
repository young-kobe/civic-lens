"""
Live query module for the propaganda panel (Phase 9 strictly-live API):
aggregates analysis.propaganda_results/propaganda_techniques/runs directly
per request. The denominator is analysis.runs (is_current, done, task=
'propaganda'), not propaganda_results rows -- deterministic pre-filter-clean
docs get a real run row (see engine/citations.py's convention) and belong in
the denominator, not just the flagged numerator.
"""

from __future__ import annotations

from collections import Counter, defaultdict
from datetime import datetime
from typing import Any, Dict, List, Mapping, Optional, Sequence

from analysis.src.api.queries.base import build_sample_doc, split_admission_counts
from analysis.src.api.queries.constants import BOT_FLAGGED_SHARE_EXCLUSION, OFFICIAL_AUTHOR_TIERS, SNIPPET_MAX_CHARS
from analysis.src.api.queries.profiles import (
    CATCH_ALL_OUTLETS,
    CATCH_ALL_SUBREDDITS,
    CATCH_ALL_X_USERS,
    catch_all_profile,
    fetch_entity_profiles,
)
from analysis.src.common import db

# The six analysis.propaganda_technique enum values -- rendered even at
# count 0 so the UI can show a complete bar chart, not just detected ones.
PROPAGANDA_TECHNIQUES = (
    "loaded_language", "name_calling", "ad_hominem",
    "appeal_to_fear", "whataboutism", "doubt_casting",
)

# Sample evidence spans kept per technique (bar-chart tooltip use, not a
# full drill-down) and flagged-doc examples surfaced at the panel level.
SAMPLE_EVIDENCE_PER_TECHNIQUE = 3
FLAGGED_EXAMPLE_LIMIT = 10

# Per-entity leaderboard cap -- tolerance-based territory (a ranked
# top-N), not a hard cap tied to any stored rollup size.
MAX_ENTITY_LEADERBOARD_ROWS = 20

# Per-entity flagged-example cap for the drill-down bucket (Wave 2 restoration,
# docs/todos/ui-feature-restoration.md) -- readers only need a handful of
# examples to audit the score, not an exhaustive list.
EXAMPLES_PER_ENTITY = 5
# Pool size for the examples-by-entity fetch: pulled once, ordered by density
# desc, then fanned into per-entity buckets in Python (capped at
# EXAMPLES_PER_ENTITY each) -- larger than any single entity needs so a long
# tail of low-volume outlets/officials still gets a few rows.
EXAMPLE_POOL_LIMIT = 500


def _time_filter(
    start: Optional[datetime], end: Optional[datetime], params: Dict[str, Any],
    column: str = "d.published_at",
) -> str:
    """SQL fragment (leading ' AND ...' or '') applying an optional
    [start, end] bound on `column` -- either half may be None (unbounded)."""
    clauses = []
    if start is not None:
        clauses.append(f"{column} >= %(_range_start)s")
        params["_range_start"] = start
    if end is not None:
        clauses.append(f"{column} <= %(_range_end)s")
        params["_range_end"] = end
    return (" AND " + " AND ".join(clauses)) if clauses else ""


def get_propaganda_overview(
    *, start: Optional[datetime], end: Optional[datetime], window_label: Optional[str],
) -> Dict[str, Any]:
    """Overall flagged rate/mean score, per-technique counts, news-vs-social
    and by-party splits, and flagged evidence samples for [start, end]."""
    with db.connection() as conn:
        doc_rows = _fetch_eligible_docs(conn, start, end)
        technique_rows = _fetch_technique_evidence(conn, start, end)
        sample_rows = _fetch_flagged_samples(conn, start, end)
        entity_rows = _fetch_entity_rows(conn, start, end)
        example_rows = _fetch_example_rows(conn, start, end)
        run_ids = [row["run_id"] for row in example_rows]
        example_technique_rows = _fetch_example_techniques(conn, run_ids)

        # One batched profile fetch covers every entity id referenced by
        # either the tier buckets (doc_rows) or the drill-down examples
        # (example_rows) -- no per-entity query loop.
        entity_ids = {
            row[col]
            for row in (*doc_rows, *example_rows)
            for col in ("outlet_entity_id", "subreddit_entity_id", "author_entity_id")
            if row[col] is not None
        }
        profiles = fetch_entity_profiles(conn, entity_ids)

    overview = _summarize_docs(doc_rows)
    overview["by_technique"] = _summarize_techniques(technique_rows, overview["flagged_docs"])
    overview["by_tier"] = _bucket_by_tier(doc_rows)
    overview["by_entity"] = _summarize_entity_rows(entity_rows)
    overview["examples"] = [
        build_sample_doc(row, admission_class=row["admission_class"]) for row in sample_rows
    ]
    tier_buckets = _bucket_by_tier_entity(doc_rows, profiles)
    overview["by_news_outlet"] = _finalize_tier_items(tier_buckets["news_outlet"])
    overview["by_official"] = _finalize_tier_items(tier_buckets["official"])
    overview["by_general_public"] = _finalize_tier_items(tier_buckets["general_public"])
    overview["examples_by_entity"] = _build_examples_by_entity(
        example_rows, example_technique_rows, profiles,
    )
    sampled, official_record = split_admission_counts(doc_rows) if doc_rows else (0, 0)
    overview["range"] = {
        "window": window_label,
        "start": start,
        "end": end,
        "sampled_doc_count": sampled,
        "official_record_doc_count": official_record,
        "model_ids": sorted({row["model_id"] for row in doc_rows}),
    }
    return overview


# =============================================================================
# Pure computation -- no DB, unit-testable directly.
# =============================================================================

def _summarize_docs(rows: Sequence[Mapping[str, Any]]) -> Dict[str, Any]:
    """Overall + news-vs-social + by-party rollups from one pass over the
    eligible-doc rows (already bot-exclusion-filtered in SQL)."""
    total = len(rows)
    flagged = sum(1 for row in rows if (row["techniques_validated"] or 0) > 0)
    mean_score = round(sum(row["density"] for row in rows) / total, 3) if total else 0.0
    return {
        "total_eligible_docs": total,
        "flagged_docs": flagged,
        "flagged_rate_pct": round(flagged / total * 100, 1) if total else 0.0,
        "mean_score": mean_score,
        "by_source": _bucket_by_source(rows),
        "by_party": _bucket_by_party(rows),
    }


def _bucket_by_source(rows: Sequence[Mapping[str, Any]]) -> List[Dict[str, Any]]:
    """news-vs-social split -- 'news' is corpus.source_type='news', every
    other source_type (reddit_post, x_post) buckets as 'social'."""
    buckets: Dict[str, Dict[str, Any]] = {
        "news": {"total": 0, "flagged": 0, "score_sum": 0.0},
        "social": {"total": 0, "flagged": 0, "score_sum": 0.0},
    }
    for row in rows:
        bucket = buckets["news"] if row["source_type"] == "news" else buckets["social"]
        bucket["total"] += 1
        bucket["score_sum"] += row["density"]
        if (row["techniques_validated"] or 0) > 0:
            bucket["flagged"] += 1
    out = []
    for source in ("news", "social"):
        b = buckets[source]
        total = b["total"]
        out.append({
            "source": source,
            "total_docs": total,
            "flagged_docs": b["flagged"],
            "flagged_rate_pct": round(b["flagged"] / total * 100, 1) if total else 0.0,
            "mean_score": round(b["score_sum"] / total, 3) if total else 0.0,
        })
    return out


def _bucket_by_party(rows: Sequence[Mapping[str, Any]]) -> List[Dict[str, Any]]:
    """By-party split keyed on corpus.entities.lean (the schema's only
    curated affiliation column -- there is no separate 'party' column).
    Authors with no resolved entity, or a registered entity with
    lean='unknown', both collapse into the 'unknown' bucket rather than
    being excluded -- every eligible doc is accounted for in exactly one
    party bucket."""
    buckets: Dict[str, Dict[str, Any]] = {}
    for row in rows:
        party = row["party"]
        bucket = buckets.setdefault(party, {"total": 0, "flagged": 0, "score_sum": 0.0})
        bucket["total"] += 1
        bucket["score_sum"] += row["density"]
        if (row["techniques_validated"] or 0) > 0:
            bucket["flagged"] += 1
    out = []
    for party in sorted(buckets):
        b = buckets[party]
        total = b["total"]
        out.append({
            "party": party,
            "total_docs": total,
            "flagged_docs": b["flagged"],
            "flagged_rate_pct": round(b["flagged"] / total * 100, 1) if total else 0.0,
            "mean_score": round(b["score_sum"] / total, 3) if total else 0.0,
        })
    return out


def _bucket_by_tier(rows: Sequence[Mapping[str, Any]]) -> List[Dict[str, Any]]:
    """News/Officials/Public three-way split (the ThreeWayGrid tiers):
    'news' is source_type='news'; 'officials' is an author whose
    author_profiles.tier is in OFFICIAL_AUTHOR_TIERS (elected_official or
    affiliated); every other doc (general_public authors, and docs with no
    resolved author profile) buckets as 'public' -- every eligible doc
    lands in exactly one of the three groups, matching by_party's
    'unknown' convention of accounting for every row rather than dropping
    unmatched ones."""
    buckets: Dict[str, Dict[str, Any]] = {
        group: {"total": 0, "flagged": 0, "score_sum": 0.0} for group in ("news", "officials", "public")
    }
    for row in rows:
        if row["source_type"] == "news":
            group = "news"
        elif row["tier"] in OFFICIAL_AUTHOR_TIERS:
            group = "officials"
        else:
            group = "public"
        bucket = buckets[group]
        bucket["total"] += 1
        bucket["score_sum"] += row["density"]
        if (row["techniques_validated"] or 0) > 0:
            bucket["flagged"] += 1
    out = []
    for group in ("news", "officials", "public"):
        b = buckets[group]
        total = b["total"]
        out.append({
            "group": group,
            "total_docs": total,
            "flagged_docs": b["flagged"],
            "flagged_rate_pct": round(b["flagged"] / total * 100, 1) if total else 0.0,
            "mean_score": round(b["score_sum"] / total, 3) if total else 0.0,
        })
    return out


def _resolve_tier_and_key(
    row: Mapping[str, Any], profiles: Mapping[int, Any],
) -> tuple:
    """Route one doc to (tier, bucket_key, profile) for the per-tier entity
    leaderboards (Wave 2 restoration). Mirrors the source_type resolution
    already used by _bucket_by_tier, but resolved to a specific registry
    entity (or a catch-all sentinel) rather than a bare group label:

      * source_type='news' -> corpus.news_articles.outlet_entity_id, else
        the 'other-outlets' catch-all.
      * source_type='reddit_post' -> corpus.reddit_posts.subreddit_entity_id
        (always general_public -- a subreddit is never an official), else
        the 'other-subreddits' catch-all.
      * source_type='x_post' -> corpus.author_profiles.entity_id. An
        official/collective (EntityProfileModel.kind == 'official', the
        ui-kind already resolved by profiles.py's _entity_ui_kind per its
        is_official_kind predicate) lands in the officials tier; any other
        resolved entity still gets its own general_public card rather than
        folding into the catch-all. An unresolved x_post falls to the
        'other-x-users' catch-all.

    Every row lands in exactly one (tier, key) -- there is no dropped/
    unclassified case, matching by_tier's accounting convention."""
    source_type = row["source_type"]
    if source_type == "news":
        entity_id = row["outlet_entity_id"]
        if entity_id is not None and entity_id in profiles:
            return "news_outlet", profiles[entity_id].key, profiles[entity_id]
        return "news_outlet", CATCH_ALL_OUTLETS, catch_all_profile(CATCH_ALL_OUTLETS)
    if source_type == "reddit_post":
        entity_id = row["subreddit_entity_id"]
        if entity_id is not None and entity_id in profiles:
            return "general_public", profiles[entity_id].key, profiles[entity_id]
        return "general_public", CATCH_ALL_SUBREDDITS, catch_all_profile(CATCH_ALL_SUBREDDITS)
    # x_post
    entity_id = row["author_entity_id"]
    if entity_id is not None and entity_id in profiles:
        profile = profiles[entity_id]
        tier = "official" if profile.kind == "official" else "general_public"
        return tier, profile.key, profile
    return "general_public", CATCH_ALL_X_USERS, catch_all_profile(CATCH_ALL_X_USERS)


def _bucket_by_tier_entity(
    rows: Sequence[Mapping[str, Any]], profiles: Mapping[int, Any],
) -> Dict[str, Dict[str, Dict[str, Any]]]:
    """Accumulate per-(tier, entity-key) doc/flagged/score stats across the
    eligible-doc rows -- the pure core behind by_news_outlet/by_official/
    by_general_public, unit-testable without a database."""
    buckets: Dict[str, Dict[str, Dict[str, Any]]] = {
        "news_outlet": {}, "official": {}, "general_public": {},
    }
    for row in rows:
        tier, key, profile = _resolve_tier_and_key(row, profiles)
        slot = buckets[tier].setdefault(
            key, {"profile": profile, "total": 0, "flagged": 0, "score_sum": 0.0},
        )
        slot["total"] += 1
        slot["score_sum"] += row["density"]
        if (row["techniques_validated"] or 0) > 0:
            slot["flagged"] += 1
    return buckets


def _finalize_tier_items(bucket: Dict[str, Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Turn one tier's per-key accumulator dict into ranked PropagandaEntityItem
    dicts -- sorted by flagged_rate_pct desc, catch-alls last."""
    items = []
    for key, slot in bucket.items():
        total = slot["total"]
        if total == 0:
            continue
        flagged = slot["flagged"]
        items.append({
            "key": key,
            "kind": slot["profile"].kind,
            "entity_profile": slot["profile"],
            "total_docs": total,
            "flagged_docs": flagged,
            "flagged_rate_pct": round(flagged / total * 100, 1),
            "mean_score": round(slot["score_sum"] / total, 3),
        })
    items.sort(key=lambda item: (item["kind"] == "catch_all", -item["flagged_rate_pct"]))
    return items


def _build_examples_by_entity(
    example_rows: Sequence[Mapping[str, Any]],
    technique_rows: Sequence[Mapping[str, Any]],
    profiles: Mapping[int, Any],
) -> Dict[str, List[Dict[str, Any]]]:
    """Fan the flagged-example pool (already ordered by density desc in SQL)
    into per-entity buckets keyed by the same key _resolve_tier_and_key
    produces for the tier leaderboards -- capped at EXAMPLES_PER_ENTITY each,
    so iterating the pre-sorted pool keeps the highest-density examples per
    entity without a separate per-entity query."""
    techniques_by_run: Dict[int, List[Dict[str, Any]]] = defaultdict(list)
    for row in technique_rows:
        techniques_by_run[row["run_id"]].append({
            "technique": row["technique"],
            "evidence_span": row["evidence_span"],
            "confidence": row["confidence"],
        })

    result: Dict[str, List[Dict[str, Any]]] = {}
    for row in example_rows:
        _tier, key, profile = _resolve_tier_and_key(row, profiles)
        bucket = result.setdefault(key, [])
        if len(bucket) >= EXAMPLES_PER_ENTITY:
            continue
        party = None
        if row["author_entity_id"] is not None and row["author_entity_id"] in profiles:
            author_profile = profiles[row["author_entity_id"]]
            if author_profile.kind == "official":
                party = author_profile.party
        bucket.append({
            "doc_id": row["doc_id"],
            "source_type": row["source_type"],
            "domain": row["domain_or_subreddit"],
            "title": row["title"],
            "overall_score": row["density"],
            "text_preview": (row["body"] or "")[:SNIPPET_MAX_CHARS],
            "url": row["source_url"],
            "techniques": techniques_by_run.get(row["run_id"], []),
            "author_handle": row["author_handle"],
            "party": party,
        })
    return result


def _summarize_entity_rows(rows: Sequence[Mapping[str, Any]]) -> List[Dict[str, Any]]:
    """Per-entity propaganda leaderboard: groups already-resolved-entity
    rows (entity_id IS NOT NULL is enforced in SQL) by entity_id, ranks by
    doc_count, and caps at MAX_ENTITY_LEADERBOARD_ROWS."""
    accum: Dict[int, Dict[str, Any]] = {}
    for row in rows:
        slot = accum.setdefault(row["entity_id"], {
            "entity_id": row["entity_id"], "entity_key": row["entity_key"],
            "display_name": row["display_name"], "group": row["group"],
            "doc_count": 0, "score_sum": 0.0, "flagged": 0,
        })
        slot["doc_count"] += 1
        slot["score_sum"] += row["density"]
        if (row["techniques_validated"] or 0) > 0:
            slot["flagged"] += 1

    items = [
        {
            "entity_id": slot["entity_id"], "entity_key": slot["entity_key"],
            "display_name": slot["display_name"], "group": slot["group"],
            "doc_count": slot["doc_count"],
            "mean_density": round(slot["score_sum"] / slot["doc_count"], 3),
            "flagged_share": round(slot["flagged"] / slot["doc_count"], 3),
        }
        for slot in accum.values()
    ]
    items.sort(key=lambda item: item["doc_count"], reverse=True)
    return items[:MAX_ENTITY_LEADERBOARD_ROWS]


def _summarize_techniques(
    rows: Sequence[Mapping[str, Any]], flagged_docs: int,
) -> List[Dict[str, Any]]:
    """Per-technique counts against every enum value (0 for undetected
    ones) plus a handful of verbatim evidence spans, truncated to
    SNIPPET_MAX_CHARS -- counts come from analysis.propaganda_techniques
    rows, never re-derived from a doc-level score."""
    counts: Counter = Counter()
    evidence: Dict[str, List[str]] = defaultdict(list)
    for row in rows:
        technique = row["technique"]
        counts[technique] += 1
        if len(evidence[technique]) < SAMPLE_EVIDENCE_PER_TECHNIQUE:
            evidence[technique].append((row["evidence_span"] or "")[:SNIPPET_MAX_CHARS])

    out = []
    for technique in PROPAGANDA_TECHNIQUES:
        count = counts.get(technique, 0)
        out.append({
            "technique": technique,
            "count": count,
            "pct_of_flagged_docs": round(count / flagged_docs * 100, 1) if flagged_docs else 0.0,
            "sample_evidence": evidence.get(technique, []),
        })
    out.sort(key=lambda t: t["count"], reverse=True)
    return out


# =============================================================================
# SQL fetchers.
# =============================================================================

def _fetch_eligible_docs(
    conn, start: Optional[datetime], end: Optional[datetime],
) -> List[Mapping[str, Any]]:
    """One row per (doc, current done propaganda run) in [start, end],
    excluding docs whose author is bot-flagged (>= BOT_FLAGGED_SHARE_EXCLUSION)
    -- the rate-denominator exclusion binding rule. Carries source_type,
    density, techniques_validated, party (entities.lean or 'unknown'), tier
    (author_profiles.tier or NULL), admission_class, and model_id --
    everything the overview + RangeMeta + by_tier need from a single pass.
    Also carries the three doc->entity resolution columns (outlet_entity_id,
    subreddit_entity_id, author_entity_id) the by_news_outlet/by_official/
    by_general_public leaderboards resolve against -- same batched-fetch
    convention as _fetch_entity_rows, added here rather than a second query
    since this row set already covers every eligible doc."""
    params: Dict[str, Any] = {"bot_exclusion": BOT_FLAGGED_SHARE_EXCLUSION}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT d.doc_id AS doc_id, d.source_type::text AS source_type,
               d.admission_class::text AS admission_class,
               COALESCE(pr.density, 0.0) AS density,
               pr.techniques_validated AS techniques_validated,
               r.model_id AS model_id,
               COALESCE(e.lean::text, 'unknown') AS party,
               ap.tier::text AS tier,
               na.outlet_entity_id AS outlet_entity_id,
               rp.subreddit_entity_id AS subreddit_entity_id,
               ap.entity_id AS author_entity_id
        FROM analysis.runs r
        JOIN analysis.propaganda_results pr ON pr.run_id = r.run_id
        JOIN corpus.documents d ON d.doc_id = r.doc_id
        LEFT JOIN corpus.news_articles na ON na.doc_id = d.doc_id
        LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id
        LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
        LEFT JOIN corpus.entities e ON e.entity_id = ap.entity_id
        WHERE r.task = 'propaganda'::analysis.task
          AND r.is_current AND r.status = 'done'::analysis.run_status
          AND (d.author_id IS NULL OR NOT EXISTS (
                SELECT 1 FROM analysis.author_bot_scores ab
                WHERE ab.author_id = d.author_id
                  AND (ab.bot_post_count + ab.suspicious_post_count)::float
                      / NULLIF(ab.sample_count, 0) >= %(bot_exclusion)s
              )){time_clause}
    """
    return conn.execute(sql, params).fetchall()


def _fetch_technique_evidence(
    conn, start: Optional[datetime], end: Optional[datetime],
) -> List[Mapping[str, Any]]:
    params: Dict[str, Any] = {"bot_exclusion": BOT_FLAGGED_SHARE_EXCLUSION}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT pt.technique::text AS technique, pt.evidence_span AS evidence_span
        FROM analysis.propaganda_techniques pt
        JOIN analysis.runs r ON r.run_id = pt.run_id
        JOIN corpus.documents d ON d.doc_id = r.doc_id
        WHERE r.task = 'propaganda'::analysis.task
          AND r.is_current AND r.status = 'done'::analysis.run_status
          AND (d.author_id IS NULL OR NOT EXISTS (
                SELECT 1 FROM analysis.author_bot_scores ab
                WHERE ab.author_id = d.author_id
                  AND (ab.bot_post_count + ab.suspicious_post_count)::float
                      / NULLIF(ab.sample_count, 0) >= %(bot_exclusion)s
              )){time_clause}
        ORDER BY pt.technique_id
    """
    return conn.execute(sql, params).fetchall()


def _fetch_entity_rows(
    conn, start: Optional[datetime], end: Optional[datetime],
) -> List[Mapping[str, Any]]:
    """One row per (doc, current done propaganda run) in [start, end] that
    resolves to a registry entity, excluding bot-flagged authors (same
    predicate as _fetch_eligible_docs). Resolution priority:
    corpus.news_articles.outlet_entity_id, then corpus.reddit_posts
    .subreddit_entity_id, then corpus.authors -> corpus.author_profiles
    .entity_id -- docs resolving through none of the three are dropped
    (entity_id IS NOT NULL), since there is no entity to attribute them to
    on the leaderboard."""
    params: Dict[str, Any] = {"bot_exclusion": BOT_FLAGGED_SHARE_EXCLUSION}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT COALESCE(na.outlet_entity_id, rp.subreddit_entity_id, ap.entity_id) AS entity_id,
               e.entity_key AS entity_key, e.display_name AS display_name,
               CASE
                   WHEN na.outlet_entity_id IS NOT NULL THEN 'news'
                   WHEN rp.subreddit_entity_id IS NOT NULL THEN 'subreddit'
                   ELSE ap.tier::text
               END AS "group",
               COALESCE(pr.density, 0.0) AS density,
               pr.techniques_validated AS techniques_validated
        FROM analysis.runs r
        JOIN analysis.propaganda_results pr ON pr.run_id = r.run_id
        JOIN corpus.documents d ON d.doc_id = r.doc_id
        LEFT JOIN corpus.news_articles na ON na.doc_id = d.doc_id
        LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id
        LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
        LEFT JOIN corpus.entities e
               ON e.entity_id = COALESCE(na.outlet_entity_id, rp.subreddit_entity_id, ap.entity_id)
        WHERE r.task = 'propaganda'::analysis.task
          AND r.is_current AND r.status = 'done'::analysis.run_status
          AND COALESCE(na.outlet_entity_id, rp.subreddit_entity_id, ap.entity_id) IS NOT NULL
          AND (d.author_id IS NULL OR NOT EXISTS (
                SELECT 1 FROM analysis.author_bot_scores ab
                WHERE ab.author_id = d.author_id
                  AND (ab.bot_post_count + ab.suspicious_post_count)::float
                      / NULLIF(ab.sample_count, 0) >= %(bot_exclusion)s
              )){time_clause}
    """
    return conn.execute(sql, params).fetchall()


def _fetch_flagged_samples(
    conn, start: Optional[datetime], end: Optional[datetime],
) -> List[Mapping[str, Any]]:
    """Recent flagged docs with a resolvable run confidence -- filtered in
    SQL (not fabricated) so every row satisfies build_sample_doc's
    contract. Snippet is the run's own summary text, truncated."""
    params: Dict[str, Any] = {
        "bot_exclusion": BOT_FLAGGED_SHARE_EXCLUSION,
        "limit": FLAGGED_EXAMPLE_LIMIT,
        "snippet_max": SNIPPET_MAX_CHARS,
    }
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT d.doc_id AS doc_id, d.source_url AS source_url,
               d.published_at AS published_at, d.admission_class::text AS admission_class,
               r.confidence AS confidence, LEFT(COALESCE(pr.summary, ''), %(snippet_max)s) AS snippet
        FROM analysis.runs r
        JOIN analysis.propaganda_results pr ON pr.run_id = r.run_id
        JOIN corpus.documents d ON d.doc_id = r.doc_id
        WHERE r.task = 'propaganda'::analysis.task
          AND r.is_current AND r.status = 'done'::analysis.run_status
          AND pr.techniques_validated > 0
          AND r.confidence IS NOT NULL
          AND (d.author_id IS NULL OR NOT EXISTS (
                SELECT 1 FROM analysis.author_bot_scores ab
                WHERE ab.author_id = d.author_id
                  AND (ab.bot_post_count + ab.suspicious_post_count)::float
                      / NULLIF(ab.sample_count, 0) >= %(bot_exclusion)s
              )){time_clause}
        ORDER BY pr.density DESC NULLS LAST, d.published_at DESC
        LIMIT %(limit)s
    """
    return conn.execute(sql, params).fetchall()


def _fetch_example_rows(
    conn, start: Optional[datetime], end: Optional[datetime],
) -> List[Mapping[str, Any]]:
    """Flagged-doc pool backing examples_by_entity (Wave 2 restoration),
    ordered by density desc so fanning the pool into per-entity buckets in
    Python keeps each entity's highest-density examples first. Carries the
    full PropagandaExample field set (title/body/domain/source_url/
    author_handle) plus run_id (to join propaganda_techniques) and the same
    three doc->entity resolution columns as _fetch_eligible_docs, so
    _resolve_tier_and_key buckets examples under the exact same keys as the
    tier leaderboards."""
    params: Dict[str, Any] = {
        "bot_exclusion": BOT_FLAGGED_SHARE_EXCLUSION,
        "limit": EXAMPLE_POOL_LIMIT,
    }
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT d.doc_id AS doc_id, r.run_id AS run_id,
               d.source_type::text AS source_type,
               d.domain_or_subreddit AS domain_or_subreddit,
               d.title AS title, d.body AS body, d.source_url AS source_url,
               COALESCE(pr.density, 0.0) AS density,
               a.handle AS author_handle,
               na.outlet_entity_id AS outlet_entity_id,
               rp.subreddit_entity_id AS subreddit_entity_id,
               ap.entity_id AS author_entity_id
        FROM analysis.runs r
        JOIN analysis.propaganda_results pr ON pr.run_id = r.run_id
        JOIN corpus.documents d ON d.doc_id = r.doc_id
        LEFT JOIN corpus.authors a ON a.author_id = d.author_id
        LEFT JOIN corpus.news_articles na ON na.doc_id = d.doc_id
        LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id
        LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
        WHERE r.task = 'propaganda'::analysis.task
          AND r.is_current AND r.status = 'done'::analysis.run_status
          AND pr.techniques_validated > 0
          AND (d.author_id IS NULL OR NOT EXISTS (
                SELECT 1 FROM analysis.author_bot_scores ab
                WHERE ab.author_id = d.author_id
                  AND (ab.bot_post_count + ab.suspicious_post_count)::float
                      / NULLIF(ab.sample_count, 0) >= %(bot_exclusion)s
              )){time_clause}
        ORDER BY pr.density DESC NULLS LAST, d.published_at DESC
        LIMIT %(limit)s
    """
    return conn.execute(sql, params).fetchall()


def _fetch_example_techniques(conn, run_ids: List[int]) -> List[Mapping[str, Any]]:
    """Evidence spans for exactly the runs behind example_rows -- one
    batched `run_id = ANY(...)` query, never a per-doc loop."""
    if not run_ids:
        return []
    sql = """
        SELECT run_id, technique::text AS technique,
               evidence_span AS evidence_span, confidence AS confidence
        FROM analysis.propaganda_techniques
        WHERE run_id = ANY(%(run_ids)s)
        ORDER BY technique_id
    """
    return conn.execute(sql, {"run_ids": run_ids}).fetchall()
