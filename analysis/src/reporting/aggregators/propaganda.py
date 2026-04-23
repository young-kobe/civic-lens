"""
Propaganda aggregator (walkthrough 043).

Reads ``ai_outputs`` rows with ``task_type='propaganda'`` and produces:
  - Overall propaganda rate + mean score across eligible docs in the window.
  - Per-technique counts (how often each of the six techniques is flagged).
  - News vs Social split — honest reporting of which side uses more
    propaganda, in both directions.
  - A short list of recent flagged examples with verbatim evidence spans so a
    reader can audit what the classifier is calling out.

Rows with ``inference_method='deterministic'`` (pre-excluded, from other tasks
that borrow the column) do not exist for propaganda today, but the aggregator
filters them out defensively so future pre-exclusion work doesn't silently
corrupt these numbers.
"""

from __future__ import annotations

import json
from collections import Counter, defaultdict
from dataclasses import asdict, dataclass, field
from typing import Any, Dict, List, Optional, Set

from analysis.src.common.logger import get_logger
from analysis.src.llm.schemas import PROPAGANDA_TECHNIQUE_ENUM
from analysis.src.reporting.aggregators.base import (
    get_connection,
    get_time_cutoff,
    source_filter_allowed,
)
from analysis.src.reporting.aggregators.constants import (
    SOCIAL_PLATFORMS,
    NEWS_PLATFORMS,
)
from analysis.src.reporting.aggregators.narrative import _build_doc_url
from analysis.src.reporting.entity_registry import (
    catch_all_profile,
    CATCH_ALL_OUTLETS,
    CATCH_ALL_SUBREDDITS,
    CATCH_ALL_X_USERS,
    get_registry,
    resolve_entity,
)

logger = get_logger(__name__)

# Docs with overall_propaganda_score at or above this threshold are counted
# as "flagged" for the headline rate. Anything lower is considered either
# un-flagged or low-signal.
FLAG_THRESHOLD = 0.3
EXAMPLE_LIMIT = 10
TEXT_PREVIEW_CHARS = 240


@dataclass
class TechniqueCount:
    technique: str
    count: int
    pct_of_flagged_docs: float  # % of flagged docs that used this technique


@dataclass
class SourceSplit:
    label: str             # "News" | "Social Media"
    total_docs: int        # denominator in the window
    flagged_docs: int
    flagged_rate_pct: float
    mean_score: float


@dataclass
class PropagandaExample:
    doc_id: int
    source_type: str
    domain: Optional[str]
    title: Optional[str]
    overall_score: float
    techniques: List[Dict[str, Any]]  # list of {technique, confidence, evidence_span}
    text_preview: str
    # X handle of the author for x_post examples (null for news/reddit). Used
    # by the UI to filter the Examples list down to an individual entity when
    # a PropagandaEntityCard drill-down opens.
    author_handle: Optional[str] = None
    # External URL of the source doc — news story URL, X tweet permalink, or
    # Reddit post link. Null when we can't synthesize one (rare — reddit
    # comments without a parent id).
    url: Optional[str] = None


@dataclass
class PropagandaEntityItem:
    """Per-entity propaganda rollup for the three-way dashboard frame
    (walkthrough 058). One instance per registry-matched entity +
    catch-alls. Sort the Propaganda page's "Top flagged entities" card
    by ``mean_score`` desc; ties broken by ``flagged_rate_pct``."""
    key: str                    # registry key or catch-all sentinel
    kind: str                   # outlet | official | subreddit | catch_all
    total_docs: int
    flagged_docs: int
    flagged_rate_pct: float
    mean_score: float
    entity_profile: Dict[str, Any] = field(default_factory=dict)


@dataclass
class PropagandaOverview:
    """Complete /api/propaganda response."""
    window: str
    total_eligible_docs: int
    flagged_docs: int
    propaganda_rate_pct: float
    mean_score: float
    by_technique: List[TechniqueCount]
    by_source: List[SourceSplit]
    examples: List[PropagandaExample]
    disclaimer: str
    # Three-way entity rollups (walkthrough 058). Each list is sorted by
    # mean_score desc; catch-alls at the end. Empty when no eligible rows.
    by_news_outlet: List[PropagandaEntityItem] = field(default_factory=list)
    by_official: List[PropagandaEntityItem] = field(default_factory=list)
    by_general_public: List[PropagandaEntityItem] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "window": self.window,
            "total_eligible_docs": self.total_eligible_docs,
            "flagged_docs": self.flagged_docs,
            "propaganda_rate_pct": self.propaganda_rate_pct,
            "mean_score": self.mean_score,
            "by_technique": [asdict(t) for t in self.by_technique],
            "by_source": [asdict(s) for s in self.by_source],
            "examples": [asdict(e) for e in self.examples],
            "disclaimer": self.disclaimer,
            "by_news_outlet": [asdict(e) for e in self.by_news_outlet],
            "by_official": [asdict(e) for e in self.by_official],
            "by_general_public": [asdict(e) for e in self.by_general_public],
        }


class PropagandaAggregator:
    """Aggregates propaganda-technique classifications."""

    DISCLAIMER = (
        "Sampled political discourse. Propaganda scoring is a per-doc LLM "
        "classification with verbatim evidence spans; a high rate means the "
        "sample leaned heavily on these techniques, not that every flagged "
        "doc is propaganda-by-intent."
    )

    def __init__(self, db_path: str):
        self.db_path = db_path

    def get_propaganda_overview(
        self, time_window: str = "7d", source_filter: str = "all",
    ) -> PropagandaOverview:
        cutoff = get_time_cutoff(time_window)
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            rows = self._fetch_rows(cursor, cutoff)
            examples = self._fetch_examples(cursor, cutoff)
        allowed = source_filter_allowed(source_filter)
        if allowed is not None:
            rows = [r for r in rows if (r[1] or "") in allowed]
            examples = [e for e in examples if (e[1] or "") in allowed]
        return self._build_overview(rows, examples, window=time_window)

    def _fetch_rows(self, cursor, cutoff) -> List[tuple]:
        """Return (doc_id, source_type, domain, confidence, output_json,
        inference_method, x_handle). X handle is None for non-X rows;
        used for the three-way entity rollup (walkthrough 058)."""
        sql = """
            SELECT a.doc_id,
                   d.source_type,
                   d.domain_or_subreddit,
                   a.confidence,
                   a.output_json,
                   a.inference_method,
                   u.username
            FROM ai_outputs a
            JOIN docs d ON d.doc_id = a.doc_id
            LEFT JOIN x_posts_raw x
              ON d.source_type = 'x_post' AND x.tweet_id = d.ident
            LEFT JOIN x_users_raw u ON u.user_id = x.author_id
            WHERE a.task_type = 'propaganda'
              AND COALESCE(a.inference_method, '') != 'deterministic'
        """
        params: list = []
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        cursor.execute(sql, params)
        return cursor.fetchall()

    def _fetch_examples(self, cursor, cutoff) -> List[tuple]:
        """Most-recent flagged docs for the Examples card."""
        sql = """
            SELECT a.doc_id,
                   d.source_type,
                   d.domain_or_subreddit,
                   d.title,
                   d.text,
                   a.confidence,
                   a.output_json,
                   d.published_at,
                   d.ident,
                   u.username
            FROM ai_outputs a
            JOIN docs d ON d.doc_id = a.doc_id
            LEFT JOIN x_posts_raw x
                   ON d.source_type = 'x_post' AND x.tweet_id = d.ident
            LEFT JOIN x_users_raw u ON u.user_id = x.author_id
            WHERE a.task_type = 'propaganda'
              AND COALESCE(a.inference_method, '') != 'deterministic'
              AND a.confidence >= ?
        """
        params: list = [FLAG_THRESHOLD]
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        sql += " ORDER BY d.published_at DESC LIMIT ?"
        params.append(EXAMPLE_LIMIT)
        cursor.execute(sql, params)
        return cursor.fetchall()

    def _build_overview(
        self, rows: List[tuple], example_rows: List[tuple], window: str,
    ) -> PropagandaOverview:
        total_eligible = len(rows)
        flagged = 0
        score_sum = 0.0
        score_samples = 0
        technique_counts: Counter = Counter()
        # For source split
        by_src: Dict[str, Dict[str, float]] = defaultdict(
            lambda: {"total": 0, "flagged": 0, "score_sum": 0.0, "score_samples": 0}
        )
        # Three-way entity rollups (walkthrough 058). Keyed by registry
        # primary key (or catch-all sentinel). Bucket dict holds counts +
        # score accumulator + the entity profile for UI rendering.
        registry = get_registry()
        by_outlet: Dict[str, Dict[str, Any]] = {}
        by_official: Dict[str, Dict[str, Any]] = {}
        by_public: Dict[str, Dict[str, Any]] = {}

        for doc_id, source_type, domain, conf, output_json, _inf, x_handle in rows:
            try:
                payload = json.loads(output_json) if output_json else {}
            except json.JSONDecodeError:
                continue
            score = float(payload.get("overall_propaganda_score", 0.0))
            techs = payload.get("techniques") or []
            is_flagged = score >= FLAG_THRESHOLD and len(techs) > 0

            # Source bucket classification
            bucket = None
            if source_type in SOCIAL_PLATFORMS:
                bucket = "social"
            elif source_type in NEWS_PLATFORMS:
                bucket = "news"
            if bucket is not None:
                by_src[bucket]["total"] += 1
                if is_flagged:
                    by_src[bucket]["flagged"] += 1
                by_src[bucket]["score_sum"] += score
                by_src[bucket]["score_samples"] += 1

            # Three-way entity bucketing
            _accumulate_entity(
                registry, source_type, domain, x_handle, score, is_flagged,
                by_outlet, by_official, by_public,
            )

            if is_flagged:
                flagged += 1
                # De-dup per-doc: a single doc can carry multiple evidence
                # spans for the same technique (e.g. two loaded-language
                # phrases). Counting each would double-count — with
                # pct_of_flagged_docs = count / flagged * 100, values can
                # exceed 100% and read as "Loaded language: 197% of flagged
                # posts." Track (technique -> seen-in-this-doc) with a set.
                doc_techs: Set[str] = set()
                for t in techs:
                    if not isinstance(t, dict):
                        continue
                    name = t.get("technique")
                    if name in PROPAGANDA_TECHNIQUE_ENUM:
                        doc_techs.add(name)
                for name in doc_techs:
                    technique_counts[name] += 1
            score_sum += score
            score_samples += 1

        rate_pct = (flagged / total_eligible * 100) if total_eligible else 0.0
        mean_score = (score_sum / score_samples) if score_samples else 0.0

        by_technique = [
            TechniqueCount(
                technique=t,
                count=technique_counts.get(t, 0),
                pct_of_flagged_docs=round(
                    (technique_counts.get(t, 0) / flagged * 100) if flagged else 0.0,
                    1,
                ),
            )
            for t in PROPAGANDA_TECHNIQUE_ENUM
        ]
        by_technique.sort(key=lambda tc: tc.count, reverse=True)

        by_source = []
        for label, key in (("News", "news"), ("Social Media", "social")):
            d = by_src.get(key, {"total": 0, "flagged": 0, "score_sum": 0.0, "score_samples": 0})
            src_rate = (d["flagged"] / d["total"] * 100) if d["total"] else 0.0
            src_mean = (d["score_sum"] / d["score_samples"]) if d["score_samples"] else 0.0
            by_source.append(SourceSplit(
                label=label,
                total_docs=int(d["total"]),
                flagged_docs=int(d["flagged"]),
                flagged_rate_pct=round(src_rate, 1),
                mean_score=round(src_mean, 3),
            ))

        examples: List[PropagandaExample] = []
        for (doc_id, source_type, domain, title, text, _conf, output_json,
             _pub, ident, author_handle) in example_rows:
            try:
                payload = json.loads(output_json) if output_json else {}
            except json.JSONDecodeError:
                continue
            techs_raw = payload.get("techniques") or []
            techs_clean: List[Dict[str, Any]] = []
            for t in techs_raw[:5]:
                if not isinstance(t, dict):
                    continue
                techs_clean.append({
                    "technique": t.get("technique"),
                    "confidence": float(t.get("confidence", 0.0) or 0.0),
                    "evidence_span": t.get("evidence_span"),
                })
            preview = (text or "")[:TEXT_PREVIEW_CHARS]
            examples.append(PropagandaExample(
                doc_id=doc_id,
                source_type=source_type or "unknown",
                domain=domain,
                title=title,
                overall_score=float(payload.get("overall_propaganda_score", 0.0)),
                techniques=techs_clean,
                text_preview=preview,
                author_handle=author_handle,
                url=_build_doc_url(source_type, domain, ident, author_handle),
            ))

        return PropagandaOverview(
            window=window,
            total_eligible_docs=total_eligible,
            flagged_docs=flagged,
            propaganda_rate_pct=round(rate_pct, 1),
            mean_score=round(mean_score, 3),
            by_technique=by_technique,
            by_source=by_source,
            examples=examples,
            disclaimer=self.DISCLAIMER,
            by_news_outlet=_finalize_entity_items(by_outlet),
            by_official=_finalize_entity_items(by_official),
            by_general_public=_finalize_entity_items(by_public),
        )


def _accumulate_entity(
    registry,
    source_type: Optional[str],
    domain: Optional[str],
    x_handle: Optional[str],
    score: float,
    is_flagged: bool,
    by_outlet: Dict[str, Dict[str, Any]],
    by_official: Dict[str, Dict[str, Any]],
    by_public: Dict[str, Dict[str, Any]],
) -> None:
    """Fan a propaganda row into the right per-entity bucket.
    Catch-all sentinels catch unmatched docs so every tier has full
    denominator coverage."""
    tier, entity = resolve_entity(registry, source_type, domain, x_handle)
    if tier is None:
        return

    if tier == "news":
        target = by_outlet
        if entity is not None:
            key, kind, profile = entity.domain, "outlet", entity.profile_dict()
        else:
            key, kind = CATCH_ALL_OUTLETS, "catch_all"
            profile = catch_all_profile(
                CATCH_ALL_OUTLETS, "Other news outlets",
                "News docs whose domain is not in the tracked outlet registry.",
            )
    elif tier == "officials":
        target = by_official
        key, kind, profile = entity.handle, "official", entity.profile_dict()
    else:  # public
        target = by_public
        if entity is not None:
            key, kind, profile = entity.subreddit, "subreddit", entity.profile_dict()
        elif source_type == "x_post":
            key, kind = CATCH_ALL_X_USERS, "catch_all"
            profile = catch_all_profile(
                CATCH_ALL_X_USERS, "Other X users",
                "X posts whose author is not in the tracked officials registry.",
            )
        else:
            key, kind = CATCH_ALL_SUBREDDITS, "catch_all"
            profile = catch_all_profile(
                CATCH_ALL_SUBREDDITS, "Other subreddits",
                "Reddit posts whose subreddit is not in the tracked subreddit registry.",
            )

    bucket = target.setdefault(key, {
        "kind": kind, "profile": profile,
        "total": 0, "flagged": 0, "score_sum": 0.0,
    })
    bucket["total"] += 1
    bucket["score_sum"] += score
    if is_flagged:
        bucket["flagged"] += 1


def _finalize_entity_items(
    bucket: Dict[str, Dict[str, Any]],
) -> List[PropagandaEntityItem]:
    """Turn per-entity accumulator dicts into PropagandaEntityItems,
    sorted by mean_score desc with catch-alls last."""
    items: List[PropagandaEntityItem] = []
    for key, stats in bucket.items():
        total = stats["total"]
        if total == 0:
            continue
        mean = stats["score_sum"] / total
        rate = (stats["flagged"] / total) * 100
        items.append(PropagandaEntityItem(
            key=key,
            kind=stats["kind"],
            total_docs=total,
            flagged_docs=stats["flagged"],
            flagged_rate_pct=round(rate, 1),
            mean_score=round(mean, 3),
            entity_profile=stats["profile"],
        ))
    items.sort(key=lambda it: (it.kind == "catch_all", -it.mean_score))
    return items
