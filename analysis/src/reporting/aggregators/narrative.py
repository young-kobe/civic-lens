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
    X_AUTHOR_JOIN_SQL,
    get_connection,
    get_time_cutoff,
)
from analysis.src.reporting.entity_registry import (
    catch_all_profile,
    CATCH_ALL_OUTLETS,
    CATCH_ALL_SUBREDDITS,
    CATCH_ALL_X_USERS,
    get_registry,
    resolve_entity,
)
from analysis.src.reporting.models import NarrativeSummary

_SOURCE_LABELS = {
    "news": "News",
    "reddit_post": "Reddit",
    "reddit_comment": "Reddit",
    "x_post": "X",
}

# How many supporting docs to surface per narrative in the modal drill-down
# table. Small enough that the table fits above the fold on desktop; large
# enough to show a mix of tones and sources.
TOP_SUPPORTING_DOCS_LIMIT = 6


_SNIPPET_MAX_CHARS = 120


def _text_snippet(text: Optional[str], limit: int = _SNIPPET_MAX_CHARS) -> Optional[str]:
    """One-line preview of a doc's body, used as the Headline-column fallback
    for social posts that have no title. Collapses whitespace and truncates."""
    if not text:
        return None
    collapsed = " ".join(text.split())
    if not collapsed:
        return None
    if len(collapsed) > limit:
        return collapsed[: limit - 3].rstrip() + "..."
    return collapsed


def _build_source_label(
    source_type: Optional[str],
    domain: Optional[str],
    x_handle: Optional[str],
) -> str:
    """Human-readable source label for the supporting-docs table row."""
    if source_type == "news":
        return f"News · {domain}" if domain else "News"
    if source_type == "x_post":
        return f"X · @{x_handle}" if x_handle else "X"
    if source_type in ("reddit_post", "reddit_comment") and domain:
        return f"Reddit · r/{domain}"
    if source_type in ("reddit_post", "reddit_comment"):
        return "Reddit"
    return source_type or "unknown"


def _build_doc_url(
    source_type: Optional[str],
    domain: Optional[str],
    ident: Optional[str],
    x_handle: Optional[str] = None,
) -> Optional[str]:
    """External URL for a supporting doc, mirroring the sentiment sampler's
    logic: news rows store URL in ``ident``; reddit rows synthesize from
    subreddit + post id; x_post rows synthesize from handle + tweet id."""
    if not ident:
        return None
    if ident.startswith(("http://", "https://")):
        return ident
    if source_type and source_type.startswith("reddit"):
        post_id = ident.replace("t3_", "").replace("t1_", "")
        return f"https://reddit.com/r/{domain or 'all'}/comments/{post_id}"
    if source_type == "x_post" and x_handle:
        return f"https://x.com/{x_handle}/status/{ident}"
    return None

logger = get_logger(__name__)


# How many narratives to return per request. Deep exploration can be added
# later as its own endpoint; this list is the dashboard surface.
DEFAULT_LIMIT = 20


class NarrativeAggregator:
    """Top narratives by support count, with per-narrative coverage data.

    'Coverage' (not 'propagation') — walkthrough 035 narrowed this from a
    causal propagation claim to a support / citation overlay over the docs
    we've ingested.
    """

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
                   n.clustering_mode, n.clustering_threshold, n.embedding_model,
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
            GROUP BY n.narrative_id, n.name, n.first_seen_at, n.first_seen_doc_id,
                     n.clustering_mode, n.clustering_threshold, n.embedding_model
            ORDER BY support_count DESC, n.first_seen_at DESC
            LIMIT ?
        """
        params.append(limit)
        cursor.execute(sql, params)
        return cursor.fetchall()

    def _build_summary(
        self, cursor, row: tuple, cutoff: Optional[int],
    ) -> NarrativeSummary:
        (narrative_id, name, first_seen_at, first_seen_doc_id,
         clustering_mode, clustering_threshold, embedding_model,
         support_count) = row

        # Clustering audit provenance (migration 015): how this cluster was
        # formed. embedding_model is null for jaccard-mode narratives.
        clustering = None
        if clustering_mode is not None:
            clustering = {
                "mode": clustering_mode,
                "threshold": clustering_threshold,
                "embedding_model": embedding_model,
            }

        (first_seen_source_type, first_seen_domain, first_seen_tier,
         first_seen_author, first_seen_handle) = self._first_seen_info(
            cursor, first_seen_doc_id,
        )
        # For x_post docs we default the tier to 'general_public' when the
        # author is not in account_profiles. For news/reddit we leave it null.
        if first_seen_tier is None and first_seen_source_type == "x_post":
            first_seen_tier = "general_public"

        # Registry lookup for the UI's three-way frame (walkthrough 058).
        # Independent of first_seen_tier, which comes from account_profiles
        # and can say 'elected_official' for X accounts that aren't in our
        # 16-seat verified_officials.yaml (e.g. individual Congress members).
        tier_group, entity_profile = self._registry_lookup(
            first_seen_source_type, first_seen_domain, first_seen_handle,
        )

        source_breakdown = self._source_breakdown(cursor, narrative_id, cutoff)
        timeline = self._timeline(cursor, narrative_id, cutoff)
        net_sentiment = self._net_sentiment(cursor, narrative_id, cutoff)
        inbound = self._inbound_citations(cursor, narrative_id, cutoff)
        inbound_by_link_type, external_citations, cross_narrative = (
            self._citation_detail(cursor, narrative_id, cutoff)
        )
        propaganda_score = self._propaganda_score(cursor, narrative_id, cutoff)
        bot_pushed_fraction = self._bot_pushed_fraction(cursor, narrative_id, cutoff)
        cross_tier = self._is_cross_tier(cursor, narrative_id, cutoff)
        top_supporting = self._top_supporting_docs(cursor, narrative_id, cutoff)
        mean_confidence = self._mean_confidence(cursor, narrative_id, cutoff)

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
            inbound_by_link_type=inbound_by_link_type,
            external_citation_count=external_citations,
            cross_narrative_citations=cross_narrative,
            propaganda_score=propaganda_score,
            bot_pushed_fraction=bot_pushed_fraction,
            first_seen_entity_profile=entity_profile,
            first_seen_tier_group=tier_group,
            cross_tier=cross_tier,
            top_supporting_docs=top_supporting,
            mean_confidence=mean_confidence,
            clustering=clustering,
        )

    @staticmethod
    def _registry_lookup(
        source_type: Optional[str],
        domain: Optional[str],
        x_handle: Optional[str],
    ) -> tuple:
        """(tier_group, entity_profile_dict) for the first-seen doc.
        Catch-all profile when source is classifiable but no registry
        entry matched; (None, None) when source_type is unknown."""
        tier, entity = resolve_entity(get_registry(), source_type, domain, x_handle)
        if tier is None:
            return None, None
        if entity is not None:
            return tier, entity.profile_dict()
        # Matched tier but no registry entity — catch-all.
        if tier == "news":
            return tier, catch_all_profile(
                CATCH_ALL_OUTLETS, "Other news outlets",
                "News doc whose domain is not in the tracked outlet registry.",
            )
        if tier == "public" and source_type == "x_post":
            return tier, catch_all_profile(
                CATCH_ALL_X_USERS, "Other X users",
                "X post whose author is not in the tracked officials registry.",
            )
        if tier == "public":
            return tier, catch_all_profile(
                CATCH_ALL_SUBREDDITS, "Other subreddits",
                "Reddit post whose subreddit is not in the tracked subreddit registry.",
            )
        return tier, None

    def _is_cross_tier(
        self, cursor, narrative_id: int, cutoff: Optional[int],
    ) -> bool:
        """True when supporting docs span >1 of {news, officials, public}.

        Tier assignment here is coarse and matches resolve_entity:
          * news → 'news'
          * x_post authored by a verified official → 'officials'
          * x_post by anyone else → 'public'
          * reddit_post / reddit_comment → 'public'
        """
        officials_handles = {h for h in get_registry().officials.keys()}
        sql = f"""
            SELECT DISTINCT d.source_type, LOWER(u.username)
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            {X_AUTHOR_JOIN_SQL}
            WHERE nd.narrative_id = ?
        """
        params: List[Any] = [narrative_id]
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        cursor.execute(sql, params)
        tiers: set = set()
        for source_type, handle in cursor.fetchall():
            if source_type == "news":
                tiers.add("news")
            elif source_type == "x_post":
                tiers.add("officials" if handle in officials_handles else "public")
            elif source_type in ("reddit_post", "reddit_comment"):
                tiers.add("public")
            if len(tiers) >= 2:
                return True
        return False

    def _propaganda_score(
        self, cursor, narrative_id: int, cutoff: Optional[int],
    ) -> Optional[float]:
        """Mean overall_propaganda_score across supporting docs with a
        propaganda row, in the window. None when no supporting doc has been
        through propaganda detection (walkthrough 043).

        Deterministic (pre-filter-clean) rows are included: they are real
        score-0 "no propaganda" verdicts and belong in the mean, matching the
        headline denominator fix (audit A-2)."""
        sql = """
            SELECT a.output_json, a.confidence
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            JOIN ai_outputs_latest a
                 ON a.doc_id = d.doc_id
                AND a.task_type = 'propaganda'
            WHERE nd.narrative_id = ?
        """
        params: List[Any] = [narrative_id]
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        cursor.execute(sql, params)
        scores: List[float] = []
        for output_json, _conf in cursor.fetchall():
            try:
                payload = json.loads(output_json) if output_json else {}
            except json.JSONDecodeError:
                continue
            score = payload.get("overall_propaganda_score")
            if isinstance(score, (int, float)):
                scores.append(float(score))
        if not scores:
            return None
        return round(sum(scores) / len(scores), 3)

    def _bot_pushed_fraction(
        self, cursor, narrative_id: int, cutoff: Optional[int],
    ) -> Optional[float]:
        """Fraction of supporting X authors with a high bot score.

        A supporting doc's X author counts as "bot-pushed" when
        ``author_bot_scores.score >= 0.5`` or they have at least one
        bot-labeled post (``bot_post_count > 0``). Returns None when no
        supporting doc is an X post with a resolved author (walkthrough 043).
        """
        sql = """
            SELECT x.author_id, ab.score, ab.bot_post_count
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            JOIN x_posts_raw x ON x.tweet_id = d.ident
            LEFT JOIN author_bot_scores ab
                 ON ab.platform = 'x' AND ab.author_id = x.author_id
            WHERE nd.narrative_id = ?
              AND d.source_type = 'x_post'
        """
        params: List[Any] = [narrative_id]
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        cursor.execute(sql, params)
        rows = cursor.fetchall()
        if not rows:
            return None
        seen_authors: set[str] = set()
        bot_authors = 0
        for author_id, score, bot_post_count in rows:
            if not author_id or author_id in seen_authors:
                continue
            seen_authors.add(author_id)
            is_bot_pushed = (
                (isinstance(score, (int, float)) and score >= 0.5)
                or (isinstance(bot_post_count, int) and bot_post_count > 0)
            )
            if is_bot_pushed:
                bot_authors += 1
        if not seen_authors:
            return None
        return round(bot_authors / len(seen_authors), 3)

    def _first_seen_info(self, cursor, first_seen_doc_id: Optional[int]) -> tuple:
        """(source_type, domain, tier, author_profile, handle) for the
        earliest doc we ingested carrying the claim.

        ``tier`` and ``author_profile`` are only populated for x_post
        first-seen docs, resolved via ``x_posts_raw.author_id`` →
        ``account_profiles.*``. ``author_profile`` is a dict (or None) with
        faction context (party/branch/chamber/state/office/etc.) used by the
        UI to render "Rep Adams (D, NC-12)"-style labels. ``handle`` is the
        post author's X username (raw-case) for x_post rows, None otherwise
        — the caller passes it into the entity_registry resolve_entity for
        the three-way frame lookup. Naming rationale: see walkthrough 035.
        """
        if first_seen_doc_id is None:
            return None, None, None, None, None
        cursor.execute(
            f"""
            SELECT d.source_type, d.domain_or_subreddit,
                   ap.tier, ap.full_name, ap.party, ap.branch, ap.chamber,
                   ap.state_or_district, ap.office_title, ap.account_type,
                   x.author_id, u.username
            FROM docs d
            {X_AUTHOR_JOIN_SQL}
            LEFT JOIN account_profiles ap
              ON ap.platform = 'x' AND ap.author_id = x.author_id
            WHERE d.doc_id = ?
            """,
            (first_seen_doc_id,),
        )
        row = cursor.fetchone()
        if not row:
            return None, None, None, None, None

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

        return source_type, domain, tier, author_profile, username

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
            JOIN ai_outputs_latest a
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
            # NEUTRAL / MIXED contribute 0 to the numerator but DO count in
            # the denominator (audit A-6) — the docstring's "NEUTRAL=0, average
            # over supporting docs" contract, matching the sentiment page. A
            # narrative that is 49 NEUTRAL + 1 POSITIVE reports ~+2, not +90.
            count += 1

        if count == 0:
            return 0.0
        return round((total / count) * 100, 1)

    def _mean_confidence(
        self, cursor, narrative_id: int, cutoff: Optional[int],
    ) -> Optional[float]:
        """Mean sentiment-row confidence across the narrative's supporting docs
        in the window (audit R-3).

        Averages over ALL supporting docs that have a sentiment row — not just
        the top-N drill-down slice — so the chip reflects the whole cluster.
        Reads ai_outputs_latest, which guarantees one sentiment row per doc;
        the best-per-doc pass below is retained as a cheap belt-and-braces
        dedup. Returns None when no supporting doc has a sentiment row.
        """
        sql = """
            SELECT d.doc_id, a.confidence
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            JOIN ai_outputs_latest a
                 ON a.doc_id = d.doc_id
                AND a.task_type = 'sentiment'
            WHERE nd.narrative_id = ?
        """
        params: List[Any] = [narrative_id]
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        sql += " ORDER BY COALESCE(a.confidence, 0) DESC"
        cursor.execute(sql, params)

        best_per_doc: Dict[int, float] = {}
        for doc_id, conf in cursor.fetchall():
            if doc_id in best_per_doc:
                continue
            best_per_doc[doc_id] = float(conf) if conf is not None else 0.0

        if not best_per_doc:
            return None
        return round(sum(best_per_doc.values()) / len(best_per_doc), 3)

    def _top_supporting_docs(
        self, cursor, narrative_id: int, cutoff: Optional[int],
    ) -> List[Dict[str, Any]]:
        """Top N supporting docs for the modal drill-down table.

        Orders by sentiment AI-output confidence desc so the table surfaces the
        highest-signal classifications first. Rows without a sentiment row
        still appear (with null label/confidence/reasoning) so the table
        doesn't drop a doc purely for missing analysis. Source-label is
        synthesized here — "News · nytimes.com" / "X · @Schumer" /
        "Reddit · r/politics" — so the UI doesn't have to re-derive it.
        """
        sql = f"""
            SELECT d.doc_id, d.title,
                   substr(d.text, 1, {_SNIPPET_MAX_CHARS * 4}) AS text_head,
                   d.source_type, d.domain_or_subreddit,
                   d.ident, d.published_at, u.username,
                   a.output_json, a.confidence
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            {X_AUTHOR_JOIN_SQL}
            LEFT JOIN ai_outputs_latest a
                   ON a.doc_id = d.doc_id
                  AND a.task_type = 'sentiment'
            WHERE nd.narrative_id = ?
        """
        params: List[Any] = [narrative_id]
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        sql += """
            ORDER BY COALESCE(a.confidence, 0) DESC, d.published_at DESC
            LIMIT ?
        """
        params.append(TOP_SUPPORTING_DOCS_LIMIT)
        cursor.execute(sql, params)

        rows = []
        seen_doc_ids: set = set()
        for (
            doc_id, title, text, source_type, domain, ident, published_at,
            x_handle, output_json, confidence,
        ) in cursor.fetchall():
            # Dedupe by doc_id: ai_outputs has no UNIQUE(doc_id, task_type),
            # so concurrent cron + admin runs can leave >1 sentiment row per
            # doc and the LEFT JOIN would render the doc twice (audit A-11,
            # same guard propaganda.py documents). Keep the first (highest
            # confidence, per the ORDER BY).
            if doc_id in seen_doc_ids:
                continue
            seen_doc_ids.add(doc_id)
            sentiment_label = None
            reasoning = None
            if output_json:
                try:
                    parsed = json.loads(output_json)
                    raw_label = parsed.get("label")
                    if raw_label == "POSITIVE":
                        sentiment_label = "positive"
                    elif raw_label == "NEGATIVE":
                        sentiment_label = "negative"
                    elif raw_label in ("NEUTRAL", "MIXED"):
                        sentiment_label = "neutral"
                    reasoning = parsed.get("reasoning") or None
                    if reasoning and len(reasoning) > 240:
                        reasoning = reasoning[:237].rstrip() + "..."
                except json.JSONDecodeError:
                    pass

            source_label = _build_source_label(source_type, domain, x_handle)
            url = _build_doc_url(source_type, domain, ident, x_handle)

            rows.append({
                "doc_id": doc_id,
                "title": title or None,
                # Social posts (X, Reddit) have no headline; give the UI a text
                # snippet to show in the Headline column instead of "(untitled)".
                "snippet": _text_snippet(text) if not title else None,
                "source_type": source_type or "unknown",
                "source_label": source_label,
                "url": url,
                "published_at": published_at,
                "sentiment_label": sentiment_label,
                "confidence": float(confidence) if confidence is not None else None,
                "reasoning": reasoning,
            })
        return rows

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

    _MAX_CROSS_NARRATIVE_EDGES = 5

    def _citation_detail(
        self, cursor, narrative_id: int, cutoff: Optional[int],
    ) -> tuple:
        """(inbound_by_link_type, external_count, cross_narrative) — the
        citation-edge semantics the flat inbound count collapses away.

        * ``inbound_by_link_type``: the inbound count split by edge kind
          (url_citation / quote / reply / retweet) — a quote-heavy narrative
          spreads differently than a link-dropped one.
        * ``external_count``: outbound edges from supporting docs to URLs we
          never ingested (target_url set, target_doc_id null) — what the
          narrative points AT outside our sample.
        * ``cross_narrative``: top narratives this one cites into / is cited
          by, each ``{narrative_id, name, direction, edge_count}``. These are
          edges between sampled docs only — never a claim about origin or
          causal propagation (walkthrough 035 scoping).
        """
        time_filter = " AND c.discovered_at >= ?" if cutoff is not None else ""
        time_params: List[Any] = [cutoff] if cutoff is not None else []

        cursor.execute(
            f"""
            SELECT c.link_type, COUNT(*)
            FROM narrative_citations c
            JOIN narrative_docs nd ON nd.doc_id = c.target_doc_id
            WHERE nd.narrative_id = ? AND c.target_doc_id IS NOT NULL{time_filter}
            GROUP BY c.link_type
            """,
            [narrative_id, *time_params],
        )
        by_link_type = {lt or "unknown": n for lt, n in cursor.fetchall()}

        cursor.execute(
            f"""
            SELECT COUNT(*)
            FROM narrative_citations c
            JOIN narrative_docs nd ON nd.doc_id = c.source_doc_id
            WHERE nd.narrative_id = ? AND c.target_doc_id IS NULL
              AND c.target_url IS NOT NULL{time_filter}
            """,
            [narrative_id, *time_params],
        )
        external_count = cursor.fetchone()[0] or 0

        cross: List[Dict[str, Any]] = []
        for direction, src_col, dst_col in (
            ("cites", "source_doc_id", "target_doc_id"),
            ("cited_by", "target_doc_id", "source_doc_id"),
        ):
            cursor.execute(
                f"""
                SELECT other.narrative_id, n2.name, COUNT(*) AS edge_count
                FROM narrative_citations c
                JOIN narrative_docs mine ON mine.doc_id = c.{src_col}
                     AND mine.narrative_id = ?
                JOIN narrative_docs other ON other.doc_id = c.{dst_col}
                     AND other.narrative_id != ?
                JOIN narratives n2 ON n2.narrative_id = other.narrative_id
                WHERE c.target_doc_id IS NOT NULL{time_filter}
                GROUP BY other.narrative_id, n2.name
                ORDER BY edge_count DESC
                LIMIT ?
                """,
                [narrative_id, narrative_id, *time_params, self._MAX_CROSS_NARRATIVE_EDGES],
            )
            cross.extend(
                {
                    "narrative_id": other_id,
                    "name": other_name or "",
                    "direction": direction,
                    "edge_count": edge_count,
                }
                for other_id, other_name, edge_count in cursor.fetchall()
            )
        cross.sort(key=lambda e: -e["edge_count"])
        return by_link_type, external_count, cross[: self._MAX_CROSS_NARRATIVE_EDGES]
