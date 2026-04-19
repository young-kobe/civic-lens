"""
Narrative aggregator.

Surfaces the narrative-overlay data (``narratives`` + ``narrative_docs`` +
``narrative_citations``) to the API. For each narrative the aggregator
computes: supporting doc count in the selected time window, per-source-type
breakdown, a daily timeline of new supporting docs, net sentiment over
supporting docs, and an inbound citation count from the partial citation
overlay (edges only exist between docs we own).
"""

import json
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional

from analysis.src.common.logger import get_logger
from analysis.src.reporting.aggregators.base import (
    get_connection,
    get_time_cutoff,
)
from analysis.src.reporting.models import NarrativeSummary

_SOURCE_LABELS = {
    "news": "News",
    "reddit_post": "Reddit",
    "reddit_comment": "Reddit",
    "x_post": "X",
}

logger = get_logger(__name__)


# How many narratives to return per request. Deep exploration can be added
# later as its own endpoint; this list is the dashboard surface.
DEFAULT_LIMIT = 20


class NarrativeAggregator:
    """Top narratives by support count, with per-narrative propagation data."""

    def __init__(self, db_path: str):
        self.db_path = db_path

    def get_top_narratives(
        self,
        time_window: str = "7d",
        limit: int = DEFAULT_LIMIT,
    ) -> List[NarrativeSummary]:
        cutoff = get_time_cutoff(time_window)
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            ranked = self._rank_narratives(cursor, cutoff, limit)
            return [self._build_summary(cursor, row, cutoff) for row in ranked]

    def _rank_narratives(
        self,
        cursor,
        cutoff: Optional[int],
        limit: int,
    ) -> List[tuple]:
        """Return (narrative_id, name, first_seen_at, first_seen_doc_id, support_count)
        for the top narratives in the window, ordered by support_count desc."""
        sql = """
            SELECT n.narrative_id, n.name, n.first_seen_at, n.first_seen_doc_id,
                   COUNT(DISTINCT nd.doc_id) AS support_count
            FROM narratives n
            JOIN narrative_docs nd ON nd.narrative_id = n.narrative_id
            JOIN docs d ON d.doc_id = nd.doc_id
        """
        params: List[Any] = []
        if cutoff is not None:
            sql += " WHERE d.published_at >= ?"
            params.append(cutoff)
        sql += """
            GROUP BY n.narrative_id, n.name, n.first_seen_at, n.first_seen_doc_id
            ORDER BY support_count DESC, n.first_seen_at DESC
            LIMIT ?
        """
        params.append(limit)
        cursor.execute(sql, params)
        return cursor.fetchall()

    def _build_summary(
        self, cursor, row: tuple, cutoff: Optional[int],
    ) -> NarrativeSummary:
        narrative_id, name, first_seen_at, first_seen_doc_id, support_count = row

        (first_seen_source_type, first_seen_domain, first_seen_tier,
         first_seen_author) = self._first_seen_info(cursor, first_seen_doc_id)
        # For x_post docs we default the tier to 'general_public' when the
        # author is not in account_profiles. For news/reddit we leave it null.
        if first_seen_tier is None and first_seen_source_type == "x_post":
            first_seen_tier = "general_public"

        source_breakdown = self._source_breakdown(cursor, narrative_id, cutoff)
        timeline = self._timeline(cursor, narrative_id, cutoff)
        net_sentiment = self._net_sentiment(cursor, narrative_id, cutoff)
        inbound = self._inbound_citations(cursor, narrative_id, cutoff)

        return NarrativeSummary(
            narrative_id=narrative_id,
            name=name or "",
            first_seen_at=first_seen_at or 0,
            first_seen_doc_id=first_seen_doc_id,
            first_seen_source_type=first_seen_source_type,
            first_seen_domain=first_seen_domain,
            first_seen_tier=first_seen_tier,
            first_seen_author=first_seen_author,
            supporting_doc_count=support_count,
            source_breakdown=source_breakdown,
            timeline=timeline,
            net_sentiment=net_sentiment,
            inbound_citation_count=inbound,
        )

    def _first_seen_info(self, cursor, first_seen_doc_id: Optional[int]) -> tuple:
        """(source_type, domain, tier, author_profile) for the earliest doc
        we ingested carrying the claim.

        ``tier`` and ``author_profile`` are only populated for x_post
        first-seen docs, resolved via ``x_posts_raw.author_id`` →
        ``account_profiles.*``. ``author_profile`` is a dict (or None) with
        faction context (party/branch/chamber/state/office/etc.) used by the
        UI to render "Rep Adams (D, NC-12)"-style labels. Absence from
        account_profiles returns tier='general_public' and author_profile=None.
        Naming rationale: see walkthrough 035.
        """
        if first_seen_doc_id is None:
            return None, None, None, None
        cursor.execute(
            """
            SELECT d.source_type, d.domain_or_subreddit,
                   ap.tier, ap.full_name, ap.party, ap.branch, ap.chamber,
                   ap.state_or_district, ap.office_title, ap.account_type,
                   x.author_id, u.username
            FROM docs d
            LEFT JOIN x_posts_raw x
              ON d.source_type = 'x_post' AND x.tweet_id = d.ident
            LEFT JOIN x_users_raw u ON u.user_id = x.author_id
            LEFT JOIN account_profiles ap
              ON ap.platform = 'x' AND ap.author_id = x.author_id
            WHERE d.doc_id = ?
            """,
            (first_seen_doc_id,),
        )
        row = cursor.fetchone()
        if not row:
            return None, None, None, None

        (source_type, domain, tier, full_name, party, branch, chamber,
         state_or_district, office_title, account_type, author_id, username) = row

        # Build author_profile only when we have an X author (tier lookup
        # works on author_id) AND the doc is x_post. Everyone else stays None.
        author_profile = None
        if source_type == "x_post" and author_id:
            author_profile = {
                "handle": username,
                "full_name": full_name,
                "party": party,
                "branch": branch,
                "chamber": chamber,
                "state_or_district": state_or_district,
                "office_title": office_title,
                "account_type": account_type,
            }

        return source_type, domain, tier, author_profile

    def _source_breakdown(
        self, cursor, narrative_id: int, cutoff: Optional[int],
    ) -> List[Dict[str, Any]]:
        sql = """
            SELECT d.source_type, COUNT(DISTINCT nd.doc_id) AS count
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            WHERE nd.narrative_id = ?
        """
        params: List[Any] = [narrative_id]
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        sql += " GROUP BY d.source_type ORDER BY count DESC"
        cursor.execute(sql, params)
        return [
            {
                "source_type": st,
                "label": _SOURCE_LABELS.get(st, st),
                "count": c,
            }
            for st, c in cursor.fetchall()
        ]

    def _timeline(
        self, cursor, narrative_id: int, cutoff: Optional[int],
    ) -> List[Dict[str, Any]]:
        sql = """
            SELECT date(d.published_at, 'unixepoch') AS day,
                   COUNT(DISTINCT nd.doc_id) AS count
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            WHERE nd.narrative_id = ?
              AND d.published_at IS NOT NULL
        """
        params: List[Any] = [narrative_id]
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        sql += " GROUP BY day ORDER BY day ASC"
        cursor.execute(sql, params)
        return [{"date": day, "count": count} for day, count in cursor.fetchall()]

    def _net_sentiment(
        self, cursor, narrative_id: int, cutoff: Optional[int],
    ) -> float:
        """Net sentiment (−100..+100) over supporting docs in the window.

        Maps POSITIVE→+conf, NEGATIVE→−conf, NEUTRAL/MIXED→0, averages, scales
        to %. Sentiment rows below ``aggregation_min_confidence`` are dropped
        entirely (walkthrough 039) so a low-confidence half-guess doesn't
        move the narrative's headline sentiment.
        """
        from analysis.src.common.settings import get_settings
        min_conf = get_settings().aggregation_min_confidence

        sql = """
            SELECT a.output_json, a.confidence
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            JOIN ai_outputs a
                 ON a.doc_id = d.doc_id
                AND a.task_type = 'sentiment'
                AND a.confidence >= ?
            WHERE nd.narrative_id = ?
        """
        params: List[Any] = [min_conf, narrative_id]
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        cursor.execute(sql, params)

        total = 0.0
        count = 0
        for output_json, conf in cursor.fetchall():
            try:
                parsed = json.loads(output_json) if output_json else {}
            except json.JSONDecodeError:
                continue
            label = parsed.get("label")
            c = conf if conf is not None else 1.0
            if label == "POSITIVE":
                total += c
            elif label == "NEGATIVE":
                total -= c
            else:
                continue  # NEUTRAL / MIXED do not pull the net.
            count += 1

        if count == 0:
            return 0.0
        return round((total / count) * 100, 1)

    def _inbound_citations(
        self, cursor, narrative_id: int, cutoff: Optional[int],
    ) -> int:
        """Count citation edges pointing at any supporting doc of this narrative."""
        sql = """
            SELECT COUNT(*)
            FROM narrative_citations c
            JOIN narrative_docs nd ON nd.doc_id = c.target_doc_id
            WHERE nd.narrative_id = ?
              AND c.target_doc_id IS NOT NULL
        """
        params: List[Any] = [narrative_id]
        if cutoff is not None:
            sql += " AND c.discovered_at >= ?"
            params.append(cutoff)
        cursor.execute(sql, params)
        return cursor.fetchone()[0] or 0
