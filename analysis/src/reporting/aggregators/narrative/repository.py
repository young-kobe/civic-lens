"""
Batched SQL access for the narrative aggregator.

Every fetch here takes the FULL set of ranked narrative ids and returns a
``{narrative_id -> facts}`` map with a fixed number of queries, so the total
query count is O(1) in the number of narratives rather than O(N). This is the
N+1 fix: the old per-narrative aggregator fired ~13 queries *per* narrative
(each ``WHERE nd.narrative_id = ?``); the batched form replaces that with one
``WHERE nd.narrative_id IN (...)`` per fact and fans the rows out in Python.

Where a per-narrative top-N cap or ordering must be preserved exactly (the
supporting-docs table, the cross-narrative citation edges), a
``ROW_NUMBER() OVER (PARTITION BY narrative_id ORDER BY ...)`` window reproduces
the old per-query ``ORDER BY ... LIMIT`` inside a single batched statement.

The repository returns raw facts; interpretation (label→tone mapping, registry
routing, snippet/label/enrichment presentation) lives in ``projector.py``.
"""

import json
from typing import Any, Dict, List, Optional, Sequence, Tuple

from analysis.src.reporting.aggregators.base import (
    REDDIT_ENGAGEMENT_JOIN_SQL,
    X_AUTHOR_JOIN_SQL,
)
from analysis.src.reporting.aggregators.evidence import SNIPPET_MAX_CHARS
from analysis.src.reporting.aggregators.rows import NarrativeSupportingDocRow

# How many supporting docs to surface per narrative in the modal drill-down
# table. Small enough that the table fits above the fold on desktop; large
# enough to show a mix of tones and sources. Used by the window function that
# caps each narrative's supporting-doc partition.
TOP_SUPPORTING_DOCS_LIMIT = 6

# Cap on cross-narrative citation edges surfaced per narrative (both directions
# combined). Applied per direction in SQL, then again after the two directions
# are merged in the projector — matching the pre-batch behaviour.
_MAX_CROSS_NARRATIVE_EDGES = 5


def _placeholders(ids: Sequence[int]) -> str:
    return ",".join("?" * len(ids))


class NarrativeRepository:
    """Batched narrative fact fetchers. Stateless — every method takes an open
    cursor so the aggregator can drive them all off one connection."""

    def get_ranked(
        self, cursor, cutoff: Optional[int], limit: int,
    ) -> List[tuple]:
        """Return (narrative_id, name, first_seen_at, first_seen_doc_id,
        clustering_mode, clustering_threshold, embedding_model, support_count)
        for the top narratives in the window, ordered by support_count desc.

        Unchanged from the pre-refactor ``_rank_narratives`` query."""
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

    # ------------------------------------------------------------------ #
    #  Per-source-type breakdown + daily timeline                        #
    # ------------------------------------------------------------------ #

    def get_source_breakdowns(
        self, cursor, ids: Sequence[int], cutoff: Optional[int],
    ) -> Dict[int, List[Tuple[Optional[str], int]]]:
        """{narrative_id -> [(source_type, count)]}, count desc within each
        narrative (the global ORDER BY count preserves per-narrative order)."""
        if not ids:
            return {}
        sql = f"""
            SELECT nd.narrative_id, d.source_type, COUNT(DISTINCT nd.doc_id) AS count
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            WHERE nd.narrative_id IN ({_placeholders(ids)})
        """
        params: List[Any] = list(ids)
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        sql += " GROUP BY nd.narrative_id, d.source_type ORDER BY count DESC"
        cursor.execute(sql, params)
        out: Dict[int, List[Tuple[Optional[str], int]]] = {}
        for narrative_id, source_type, count in cursor.fetchall():
            out.setdefault(narrative_id, []).append((source_type, count))
        return out

    def get_timelines(
        self, cursor, ids: Sequence[int], cutoff: Optional[int],
    ) -> Dict[int, List[Tuple[str, int]]]:
        """{narrative_id -> [(day, count)]}, days ascending within each."""
        if not ids:
            return {}
        sql = f"""
            SELECT nd.narrative_id, date(d.published_at, 'unixepoch') AS day,
                   COUNT(DISTINCT nd.doc_id) AS count
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            WHERE nd.narrative_id IN ({_placeholders(ids)})
              AND d.published_at IS NOT NULL
        """
        params: List[Any] = list(ids)
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        sql += " GROUP BY nd.narrative_id, day ORDER BY day ASC"
        cursor.execute(sql, params)
        out: Dict[int, List[Tuple[str, int]]] = {}
        for narrative_id, day, count in cursor.fetchall():
            out.setdefault(narrative_id, []).append((day, count))
        return out

    # ------------------------------------------------------------------ #
    #  Sentiment: net-tone rows (min-conf filtered) + mean confidence    #
    # ------------------------------------------------------------------ #

    def get_sentiment_stats(
        self, cursor, ids: Sequence[int], cutoff: Optional[int], min_conf: float,
    ) -> Dict[int, List[Tuple[Optional[str], Optional[float]]]]:
        """{narrative_id -> [(output_json, confidence)]} for sentiment rows at
        or above ``min_conf``. The label→tone mapping happens in the projector;
        the confidence floor (walkthrough 039) is applied here in SQL so a
        low-confidence half-guess never reaches the average."""
        if not ids:
            return {}
        sql = f"""
            SELECT nd.narrative_id, a.output_json, a.confidence
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            JOIN ai_outputs_latest a
                 ON a.doc_id = d.doc_id
                AND a.task_type = 'sentiment'
                AND a.confidence >= ?
            WHERE nd.narrative_id IN ({_placeholders(ids)})
        """
        params: List[Any] = [min_conf, *ids]
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        cursor.execute(sql, params)
        out: Dict[int, List[Tuple[Optional[str], Optional[float]]]] = {}
        for narrative_id, output_json, conf in cursor.fetchall():
            out.setdefault(narrative_id, []).append((output_json, conf))
        return out

    def get_mean_confidence(
        self, cursor, ids: Sequence[int], cutoff: Optional[int],
    ) -> Dict[int, float]:
        """{narrative_id -> mean sentiment-row confidence} over ALL supporting
        docs with a sentiment row (audit R-3), not just the drill-down slice.
        Reads ai_outputs_latest (one sentiment row per doc); the best-per-doc
        pass is retained as a cheap belt-and-braces dedup. Narratives with no
        sentiment row are absent from the map (caller reads None)."""
        if not ids:
            return {}
        sql = f"""
            SELECT nd.narrative_id, d.doc_id, a.confidence
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            JOIN ai_outputs_latest a
                 ON a.doc_id = d.doc_id
                AND a.task_type = 'sentiment'
            WHERE nd.narrative_id IN ({_placeholders(ids)})
        """
        params: List[Any] = list(ids)
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        sql += " ORDER BY nd.narrative_id, COALESCE(a.confidence, 0) DESC"
        cursor.execute(sql, params)

        best_per_doc: Dict[int, Dict[int, float]] = {}
        for narrative_id, doc_id, conf in cursor.fetchall():
            per_narrative = best_per_doc.setdefault(narrative_id, {})
            if doc_id in per_narrative:
                continue
            per_narrative[doc_id] = float(conf) if conf is not None else 0.0

        out: Dict[int, float] = {}
        for narrative_id, docs in best_per_doc.items():
            if docs:
                out[narrative_id] = round(sum(docs.values()) / len(docs), 3)
        return out

    # ------------------------------------------------------------------ #
    #  Propaganda density + bot-pushed fraction                          #
    # ------------------------------------------------------------------ #

    def get_propaganda_scores(
        self, cursor, ids: Sequence[int], cutoff: Optional[int],
    ) -> Dict[int, float]:
        """{narrative_id -> mean overall_propaganda_score} across supporting
        docs with a propaganda row (walkthrough 043). Deterministic score-0
        rows are included (audit A-2). Narratives with no propaganda row are
        absent (caller reads None)."""
        if not ids:
            return {}
        sql = f"""
            SELECT nd.narrative_id, a.output_json
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            JOIN ai_outputs_latest a
                 ON a.doc_id = d.doc_id
                AND a.task_type = 'propaganda'
            WHERE nd.narrative_id IN ({_placeholders(ids)})
        """
        params: List[Any] = list(ids)
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        cursor.execute(sql, params)
        scores: Dict[int, List[float]] = {}
        for narrative_id, output_json in cursor.fetchall():
            try:
                payload = json.loads(output_json) if output_json else {}
            except json.JSONDecodeError:
                continue
            score = payload.get("overall_propaganda_score")
            if isinstance(score, (int, float)):
                scores.setdefault(narrative_id, []).append(float(score))
        return {
            narrative_id: round(sum(vals) / len(vals), 3)
            for narrative_id, vals in scores.items()
            if vals
        }

    def get_bot_pushed_fractions(
        self, cursor, ids: Sequence[int], cutoff: Optional[int],
    ) -> Dict[int, float]:
        """{narrative_id -> fraction of unique X supporting authors flagged
        bot-pushed}. An author counts when ``author_bot_scores.score >= 0.5`` or
        they have any bot-labeled post (``bot_post_count > 0``). Narratives with
        no X supporting doc that has a resolved author are absent (caller reads
        None) — matching walkthrough 043."""
        if not ids:
            return {}
        sql = f"""
            SELECT nd.narrative_id, x.author_id, ab.score, ab.bot_post_count
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            JOIN x_posts_raw x ON x.tweet_id = d.ident
            LEFT JOIN author_bot_scores ab
                 ON ab.platform = 'x' AND ab.author_id = x.author_id
            WHERE nd.narrative_id IN ({_placeholders(ids)})
              AND d.source_type = 'x_post'
        """
        params: List[Any] = list(ids)
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        cursor.execute(sql, params)

        # narrative_id -> {author_id -> is_bot_pushed}. Presence of the inner
        # dict (even empty of resolved authors) marks "this narrative had X
        # rows"; None-return semantics require distinguishing that from a
        # narrative with no X rows at all.
        seen: Dict[int, Dict[str, bool]] = {}
        for narrative_id, author_id, score, bot_post_count in cursor.fetchall():
            authors = seen.setdefault(narrative_id, {})
            if not author_id or author_id in authors:
                continue
            authors[author_id] = (
                (isinstance(score, (int, float)) and score >= 0.5)
                or (isinstance(bot_post_count, int) and bot_post_count > 0)
            )
        out: Dict[int, float] = {}
        for narrative_id, authors in seen.items():
            if not authors:
                continue
            bot_authors = sum(1 for flagged in authors.values() if flagged)
            out[narrative_id] = round(bot_authors / len(authors), 3)
        return out

    # ------------------------------------------------------------------ #
    #  Citation overlay                                                  #
    # ------------------------------------------------------------------ #

    def get_inbound_citations(
        self, cursor, ids: Sequence[int], cutoff: Optional[int],
    ) -> Dict[int, int]:
        """{narrative_id -> count of citation edges pointing at any supporting
        doc}. Narratives with none are absent (caller reads 0)."""
        if not ids:
            return {}
        sql = f"""
            SELECT nd.narrative_id, COUNT(*)
            FROM narrative_citations c
            JOIN narrative_docs nd ON nd.doc_id = c.target_doc_id
            WHERE nd.narrative_id IN ({_placeholders(ids)})
              AND c.target_doc_id IS NOT NULL
        """
        params: List[Any] = list(ids)
        if cutoff is not None:
            sql += " AND c.discovered_at >= ?"
            params.append(cutoff)
        sql += " GROUP BY nd.narrative_id"
        cursor.execute(sql, params)
        return {narrative_id: count or 0 for narrative_id, count in cursor.fetchall()}

    def get_citation_details(
        self, cursor, ids: Sequence[int], cutoff: Optional[int],
    ) -> Dict[int, Tuple[Dict[str, int], int, List[Dict[str, Any]]]]:
        """{narrative_id -> (inbound_by_link_type, external_count,
        cross_narrative)} — the citation-edge semantics the flat inbound count
        collapses away. Four queries total regardless of narrative count.

        * inbound_by_link_type: inbound count split by edge kind.
        * external_count: outbound edges to URLs we never ingested.
        * cross_narrative: top narratives this one cites / is cited by, each
          ``{narrative_id, name, direction, edge_count}``. The per-direction
          top-N is enforced by a window function; the two directions are merged
          and re-capped in Python to reproduce the pre-batch ordering exactly.
        """
        if not ids:
            return {}
        time_filter = " AND c.discovered_at >= ?" if cutoff is not None else ""
        time_params: List[Any] = [cutoff] if cutoff is not None else []
        ph = _placeholders(ids)

        # Inbound, split by link_type.
        cursor.execute(
            f"""
            SELECT nd.narrative_id, c.link_type, COUNT(*)
            FROM narrative_citations c
            JOIN narrative_docs nd ON nd.doc_id = c.target_doc_id
            WHERE nd.narrative_id IN ({ph})
              AND c.target_doc_id IS NOT NULL{time_filter}
            GROUP BY nd.narrative_id, c.link_type
            """,
            [*ids, *time_params],
        )
        by_link_type: Dict[int, Dict[str, int]] = {}
        for narrative_id, link_type, n in cursor.fetchall():
            by_link_type.setdefault(narrative_id, {})[link_type or "unknown"] = n

        # External (outbound-to-un-ingested-URL) count.
        cursor.execute(
            f"""
            SELECT nd.narrative_id, COUNT(*)
            FROM narrative_citations c
            JOIN narrative_docs nd ON nd.doc_id = c.source_doc_id
            WHERE nd.narrative_id IN ({ph})
              AND c.target_doc_id IS NULL
              AND c.target_url IS NOT NULL{time_filter}
            GROUP BY nd.narrative_id
            """,
            [*ids, *time_params],
        )
        external: Dict[int, int] = {
            narrative_id: count or 0 for narrative_id, count in cursor.fetchall()
        }

        # Cross-narrative edges, both directions. Insertion order per narrative
        # is cites-first then cited_by, each descending by edge_count — the
        # projector merges + re-caps to _MAX_CROSS_NARRATIVE_EDGES.
        cross: Dict[int, List[Dict[str, Any]]] = {}
        for direction, src_col, dst_col in (
            ("cites", "source_doc_id", "target_doc_id"),
            ("cited_by", "target_doc_id", "source_doc_id"),
        ):
            cursor.execute(
                f"""
                SELECT mine_id, other_id, name, edge_count FROM (
                    SELECT mine.narrative_id AS mine_id,
                           other.narrative_id AS other_id,
                           n2.name AS name,
                           COUNT(*) AS edge_count,
                           ROW_NUMBER() OVER (
                               PARTITION BY mine.narrative_id
                               ORDER BY COUNT(*) DESC
                           ) AS rn
                    FROM narrative_citations c
                    JOIN narrative_docs mine
                         ON mine.doc_id = c.{src_col}
                        AND mine.narrative_id IN ({ph})
                    JOIN narrative_docs other
                         ON other.doc_id = c.{dst_col}
                        AND other.narrative_id != mine.narrative_id
                    JOIN narratives n2 ON n2.narrative_id = other.narrative_id
                    WHERE c.target_doc_id IS NOT NULL{time_filter}
                    GROUP BY mine.narrative_id, other.narrative_id, n2.name
                )
                WHERE rn <= ?
                ORDER BY mine_id, rn
                """,
                [*ids, *time_params, _MAX_CROSS_NARRATIVE_EDGES],
            )
            for mine_id, other_id, other_name, edge_count in cursor.fetchall():
                cross.setdefault(mine_id, []).append({
                    "narrative_id": other_id,
                    "name": other_name or "",
                    "direction": direction,
                    "edge_count": edge_count,
                })

        out: Dict[int, Tuple[Dict[str, int], int, List[Dict[str, Any]]]] = {}
        for narrative_id in ids:
            edges = sorted(
                cross.get(narrative_id, []), key=lambda e: -e["edge_count"],
            )[:_MAX_CROSS_NARRATIVE_EDGES]
            out[narrative_id] = (
                by_link_type.get(narrative_id, {}),
                external.get(narrative_id, 0),
                edges,
            )
        return out

    # ------------------------------------------------------------------ #
    #  Cross-tier rows + first-seen provenance                           #
    # ------------------------------------------------------------------ #

    def get_cross_tier_rows(
        self, cursor, ids: Sequence[int], cutoff: Optional[int],
    ) -> Dict[int, List[Tuple[Optional[str], Optional[str]]]]:
        """{narrative_id -> [(source_type, lower_x_handle)]} of the DISTINCT
        source-type/handle pairs among supporting docs. The projector folds
        these into the {news, officials, public} tier count."""
        if not ids:
            return {}
        sql = f"""
            SELECT DISTINCT nd.narrative_id, d.source_type, LOWER(u.username)
            FROM narrative_docs nd
            JOIN docs d ON d.doc_id = nd.doc_id
            {X_AUTHOR_JOIN_SQL}
            WHERE nd.narrative_id IN ({_placeholders(ids)})
        """
        params: List[Any] = list(ids)
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        cursor.execute(sql, params)
        out: Dict[int, List[Tuple[Optional[str], Optional[str]]]] = {}
        for narrative_id, source_type, handle in cursor.fetchall():
            out.setdefault(narrative_id, []).append((source_type, handle))
        return out

    def get_first_seen_info(
        self, cursor, first_seen_doc_ids: Sequence[Optional[int]],
    ) -> Dict[int, tuple]:
        """{doc_id -> (source_type, domain, ap_tier, full_name, party, branch,
        chamber, state_or_district, office_title, account_type, author_id,
        username)} for the given first-seen docs. Batches on doc_id; the
        projector builds the author-faction payload from each row."""
        doc_ids = sorted({d for d in first_seen_doc_ids if d is not None})
        if not doc_ids:
            return {}
        cursor.execute(
            f"""
            SELECT d.doc_id, d.source_type, d.domain_or_subreddit,
                   ap.tier, ap.full_name, ap.party, ap.branch, ap.chamber,
                   ap.state_or_district, ap.office_title, ap.account_type,
                   x.author_id, u.username
            FROM docs d
            {X_AUTHOR_JOIN_SQL}
            LEFT JOIN account_profiles ap
              ON ap.platform = 'x' AND ap.author_id = x.author_id
            WHERE d.doc_id IN ({_placeholders(doc_ids)})
            """,
            doc_ids,
        )
        return {row[0]: row[1:] for row in cursor.fetchall()}

    # ------------------------------------------------------------------ #
    #  Supporting docs (modal drill-down)                                #
    # ------------------------------------------------------------------ #

    def get_supporting_docs(
        self, cursor, ids: Sequence[int], cutoff: Optional[int],
    ) -> Dict[int, List[NarrativeSupportingDocRow]]:
        """{narrative_id -> up to TOP_SUPPORTING_DOCS_LIMIT supporting-doc rows}
        ordered by sentiment confidence desc then recency, exactly as the old
        per-narrative ``ORDER BY ... LIMIT`` did. The per-narrative cap is a
        ``ROW_NUMBER() OVER (PARTITION BY narrative_id ...)`` window so one
        batched query replaces N. Rows with no sentiment row still appear (null
        label/confidence). ai_outputs_latest yields one sentiment row per doc
        and narrative_docs is UNIQUE(narrative_id, doc_id), so each doc occupies
        exactly one partition slot; the projector keeps a dedup guard anyway."""
        if not ids:
            return {}
        sql = f"""
            SELECT narrative_id, doc_id, title, text_head, source_type,
                   domain_or_subreddit, ident, published_at, username,
                   output_json, confidence,
                   x_retweets, x_replies, x_likes, x_quotes,
                   u_name, u_avatar, u_verified_type, u_followers, u_created_at,
                   u_bio, reddit_score, reddit_comments
            FROM (
                SELECT nd.narrative_id AS narrative_id, d.doc_id AS doc_id,
                       d.title AS title,
                       substr(d.text, 1, {SNIPPET_MAX_CHARS * 4}) AS text_head,
                       d.source_type AS source_type,
                       d.domain_or_subreddit AS domain_or_subreddit,
                       d.ident AS ident, d.published_at AS published_at,
                       u.username AS username,
                       a.output_json AS output_json, a.confidence AS confidence,
                       x.retweet_count AS x_retweets, x.reply_count AS x_replies,
                       x.like_count AS x_likes, x.quote_count AS x_quotes,
                       u.name AS u_name, u.profile_image_url AS u_avatar,
                       u.verified_type AS u_verified_type,
                       u.followers_count AS u_followers,
                       u.created_at AS u_created_at, u.description AS u_bio,
                       rp.score AS reddit_score, rp.num_comments AS reddit_comments,
                       ROW_NUMBER() OVER (
                           PARTITION BY nd.narrative_id
                           ORDER BY COALESCE(a.confidence, 0) DESC,
                                    d.published_at DESC
                       ) AS rn
                FROM narrative_docs nd
                JOIN docs d ON d.doc_id = nd.doc_id
                {X_AUTHOR_JOIN_SQL}
                {REDDIT_ENGAGEMENT_JOIN_SQL}
                LEFT JOIN ai_outputs_latest a
                       ON a.doc_id = d.doc_id
                      AND a.task_type = 'sentiment'
                WHERE nd.narrative_id IN ({_placeholders(ids)})
        """
        params: List[Any] = list(ids)
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params.append(cutoff)
        sql += """
            )
            WHERE rn <= ?
            ORDER BY narrative_id, rn
        """
        params.append(TOP_SUPPORTING_DOCS_LIMIT)
        cursor.execute(sql, params)

        out: Dict[int, List[NarrativeSupportingDocRow]] = {}
        for row in cursor.fetchall():
            narrative_id = row[0]
            out.setdefault(narrative_id, []).append(
                NarrativeSupportingDocRow.from_row(row[1:])
            )
        return out
