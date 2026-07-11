"""
Narrative aggregator entry point.

Surfaces the narrative-overlay data (``narratives`` + ``narrative_docs`` +
``narrative_citations``) to the API. For each narrative it computes: supporting
doc count in the window, per-source-type breakdown, a daily timeline, net
sentiment, an inbound citation count from the partial citation overlay, plus
the propaganda / bot-pushed / cross-tier / drill-down overlays.

Query shape (the N+1 fix): the old code ran one ranking query then ~13 queries
PER narrative. This version runs the ranking query once, then calls each
batched ``NarrativeRepository`` fetch exactly once over the whole ranked id set,
and assembles every summary from the pre-fetched maps via the projector. Total
query count is now a small constant, independent of how many narratives are
returned.
"""

from typing import List, Optional

from analysis.src.common.logger import get_logger
from analysis.src.reporting.aggregators.base import (
    get_aggregation_min_confidence,
    get_connection,
    get_time_cutoff,
)
from analysis.src.reporting.aggregators.narrative import projector
from analysis.src.reporting.aggregators.narrative.repository import (
    NarrativeRepository,
    TOP_SUPPORTING_DOCS_LIMIT,
)
from analysis.src.reporting.models import NarrativeSummary

logger = get_logger(__name__)

# How many narratives to return per request. Deep exploration can be added
# later as its own endpoint; this list is the dashboard surface.
DEFAULT_LIMIT = 20

__all__ = ["NarrativeAggregator", "DEFAULT_LIMIT", "TOP_SUPPORTING_DOCS_LIMIT"]


class NarrativeAggregator:
    """Top narratives by support count, with per-narrative coverage data.

    'Coverage' (not 'propagation') — walkthrough 035 narrowed this from a
    causal propagation claim to a support / citation overlay over the docs
    we've ingested.
    """

    def __init__(self, db_path: str):
        self.db_path = db_path
        self.repo = NarrativeRepository()

    def get_top_narratives(
        self,
        time_window: str = "7d",
        limit: int = DEFAULT_LIMIT,
    ) -> List[NarrativeSummary]:
        cutoff = get_time_cutoff(time_window)
        min_conf = get_aggregation_min_confidence()
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            ranked = self.repo.get_ranked(cursor, cutoff, limit)
            if not ranked:
                return []

            ids = [row[0] for row in ranked]
            first_seen_doc_ids = [row[3] for row in ranked]

            # One batched fetch per fact — this is the whole N+1 fix.
            source_breakdowns = self.repo.get_source_breakdowns(cursor, ids, cutoff)
            timelines = self.repo.get_timelines(cursor, ids, cutoff)
            sentiment_stats = self.repo.get_sentiment_stats(cursor, ids, cutoff, min_conf)
            mean_confidences = self.repo.get_mean_confidence(cursor, ids, cutoff)
            propaganda_scores = self.repo.get_propaganda_scores(cursor, ids, cutoff)
            bot_pushed = self.repo.get_bot_pushed_fractions(cursor, ids, cutoff)
            inbound_counts = self.repo.get_inbound_citations(cursor, ids, cutoff)
            citation_details = self.repo.get_citation_details(cursor, ids, cutoff)
            cross_tier_rows = self.repo.get_cross_tier_rows(cursor, ids, cutoff)
            supporting_docs = self.repo.get_supporting_docs(cursor, ids, cutoff)
            first_seen_info = self.repo.get_first_seen_info(cursor, first_seen_doc_ids)

            return [
                projector.build_summary(
                    row,
                    first_seen_row=first_seen_info.get(row[3]),
                    source_breakdown_rows=source_breakdowns.get(row[0], []),
                    timeline_rows=timelines.get(row[0], []),
                    sentiment_rows=sentiment_stats.get(row[0], []),
                    inbound=inbound_counts.get(row[0], 0),
                    citation_detail=citation_details.get(row[0], ({}, 0, [])),
                    propaganda_score=propaganda_scores.get(row[0]),
                    bot_pushed_fraction=bot_pushed.get(row[0]),
                    cross_tier_rows=cross_tier_rows.get(row[0], []),
                    supporting_rows=supporting_docs.get(row[0], []),
                    mean_confidence=mean_confidences.get(row[0]),
                )
                for row in ranked
            ]

    def _citation_detail(self, cursor, narrative_id: int, cutoff: Optional[int]):
        """(inbound_by_link_type, external_count, cross_narrative) for one
        narrative. Retained as a thin per-narrative wrapper over the batched
        repository fetch: the citation-edge unit test drives this directly, and
        it keeps the pre-refactor signature intact."""
        return self.repo.get_citation_details(cursor, [narrative_id], cutoff).get(
            narrative_id, ({}, 0, []),
        )
