"""
Bot activity aggregator (walkthrough 040 rewrite).

Rebuilt to feed the Bot Detector tab with real numbers instead of stubs,
and to give the propaganda overlay a per-author authoritativeness signal.

Data sources:
  - `ai_outputs` rows with ``task_type='bot_detection'`` (per-post scores).
    Rows whose ``inference_method='deterministic'`` are pre-exclusion
    markers for electeds / affiliated / government-verified accounts and
    are dropped from every denominator and indicator count.
  - `author_bot_scores` (populated by ``job_runner.run_account_bot_rollup``)
    for per-author X-side aggregates.
  - `x_users_raw` for account-age bucketing.

Orchestration only: the DB scans live in ``repository``, the pure
calculations in ``metrics``, and the evidence/narrative/entity assembly in
``narratives`` / ``entities``.
"""

from __future__ import annotations

from typing import Any, Dict, List

from analysis.src.reporting.aggregators.base import (
    get_connection,
    get_time_cutoff,
)
from analysis.src.reporting.aggregators.bot.metrics import (
    _compute_coordination_index,
    _link_domain_concentration,
    _text_similarity_signals,
)
from analysis.src.reporting.aggregators.bot.narratives import (
    _fetch_narrative_amplification,
)
from analysis.src.reporting.aggregators.bot.repository import (
    _fetch_behavior_signals,
    _fetch_bot_detection_data,
    _fetch_entity_rollups,
)
from analysis.src.reporting.aggregators.bot.types import BotDetectionAggregate
from analysis.src.reporting.models import (
    BehavioralSignals,
    BotActivityData,
    BotEntityItem,
    BotOverview,
    CoordinationStats,
    NarrativeAmplification,
)


class BotAggregator:
    """Aggregates bot-detection metrics, stylometric stats, and coordination
    signals. All stubbed zeros in the pre-040 version are now computed."""

    def __init__(self, db_path: str):
        self.db_path = db_path

    def get_bot_activity(self, time_window: str = "24h") -> BotActivityData:
        """Aggregate bot metrics for the given window (24h|7d|30d|90d|all).

        The window applies a ``published_at`` cutoff to every doc-joined query
        so the Bot Detector's numbers actually match the selected pill
        (audit U-1a). ``all`` (cutoff None) is the full sample.
        """
        cutoff = get_time_cutoff(time_window)
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            bot_data = _fetch_bot_detection_data(cursor, cutoff)
            behavior = _fetch_behavior_signals(
                cursor,
                bot_data.bot_doc_ids,
                bot_data.bot_authors,
            )
            rollups = _fetch_entity_rollups(cursor, cutoff)
            narratives = _fetch_narrative_amplification(
                cursor, bot_data.bot_docs_data,
            )
        return self._format_bot_activity(bot_data, behavior, rollups, narratives)

    # ---------- Format ----------

    def _format_bot_activity(
        self, bot_data: BotDetectionAggregate, behavior: Dict[str, Any],
        rollups: Dict[str, List[BotEntityItem]],
        narratives: List[NarrativeAmplification],
    ) -> BotActivityData:
        total_eligible = bot_data.total_eligible
        bot_count = bot_data.bot_count
        suspicious_count = bot_data.suspicious_count
        by_cluster = bot_data.by_cluster
        hourly_distribution = bot_data.hourly_distribution
        bot_texts: List[str] = bot_data.bot_texts
        bot_urls: List[str] = bot_data.bot_urls

        automation_rate = (bot_count / total_eligible * 100) if total_eligible > 0 else 0.0
        top_clusters = sorted(by_cluster.items(), key=lambda x: x[1], reverse=True)[:5]
        coordination_index = _compute_coordination_index(hourly_distribution)
        identical_text_pairs, copy_paste_buckets = _text_similarity_signals(bot_texts)
        link_domain_concentration = _link_domain_concentration(bot_urls)

        return BotActivityData(
            overview=BotOverview(
                suspectedAutomationRate=round(automation_rate, 1),
                coordinationIndex=round(coordination_index, 2),
                topClusters=[c[0] for c in top_clusters],
                totalFlaggedPosts=bot_count + suspicious_count,
                confidence="medium" if total_eligible > 100 else "low",
                by_news_outlet=rollups.get("news", []),
                by_official=rollups.get("officials", []),
                by_general_public=rollups.get("public", []),
            ),
            narrativeAmplification=narratives,
            coordinationStats=CoordinationStats(
                accountReuse=behavior["account_reuse"],
                identicalTextPairs=identical_text_pairs,
                avgPostsPerSuspectedAccount=behavior["avg_posts_per_suspected_account"],
            ),
            behavioralSignals=BehavioralSignals(
                accountAgeDistribution=behavior["account_age_distribution"],
                copyPasteSimilarity=copy_paste_buckets,
                linkDomainConcentration=link_domain_concentration,
            ),
        )
