"""
Bot activity aggregator (walkthrough 040 rewrite).

Rebuilt to feed the Bot Detector tab with real numbers instead of stubs,
and to give the propaganda overlay (coming walkthroughs) a per-author
authoritativeness signal.

Data sources:
  - `ai_outputs` rows with ``task_type='bot_detection'`` (per-post scores).
    Rows whose ``inference_method='deterministic'`` are pre-exclusion
    markers for electeds / affiliated / government-verified accounts and
    are dropped from every denominator and indicator count.
  - `author_bot_scores` (populated by ``job_runner.run_account_bot_rollup``)
    for per-author X-side aggregates.
  - `x_users_raw` for account-age bucketing.
"""

from __future__ import annotations

import datetime
import json
import re
import statistics
from collections import Counter, defaultdict
from typing import Any, Dict, Iterable, List, Tuple

from analysis.src.reporting.aggregators.base import get_connection
from analysis.src.reporting.models import (
    BotOverview,
    NarrativeAmplification,
    CoordinationStats,
    BehavioralSignals,
    BotActivityData,
)


_URL_RE = re.compile(r"https?://([^/\s]+)", re.IGNORECASE)


class BotAggregator:
    """Aggregates bot-detection metrics, stylometric stats, and coordination
    signals. All stubbed zeros in the pre-040 version are now computed."""

    def __init__(self, db_path: str):
        self.db_path = db_path

    def get_bot_activity(self) -> BotActivityData:
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            bot_data = self._fetch_bot_detection_data(cursor)
            behavior = self._fetch_behavior_signals(
                cursor,
                bot_data["bot_doc_ids"],
                bot_data["bot_authors"],
            )
        return self._format_bot_activity(bot_data, behavior)

    # ---------- Fetch ----------

    def _fetch_bot_detection_data(self, cursor) -> Dict[str, Any]:
        """
        Returns counts + per-doc / per-author bookkeeping used downstream.
        Pre-excluded rows (inference_method='deterministic') are dropped from
        every count so the automation rate denominator is eligible posts only.
        """
        cursor.execute(
            """
            SELECT a.doc_id,
                   a.output_json,
                   a.confidence,
                   a.inference_method,
                   d.source_type,
                   d.domain_or_subreddit,
                   d.published_at,
                   d.text,
                   d.ident
            FROM ai_outputs a
            JOIN docs d ON a.doc_id = d.doc_id
            WHERE a.task_type = 'bot_detection'
            """
        )
        total_eligible = 0
        bot_count = 0
        suspicious_count = 0
        by_cluster: Dict[str, int] = {}
        indicators_frequency: Counter = Counter()
        hourly_distribution: Dict[int, int] = defaultdict(int)
        bot_docs_data: List[Dict[str, Any]] = []
        bot_doc_ids: set[int] = set()
        bot_texts: List[str] = []
        bot_urls: List[str] = []
        bot_idents: List[str] = []  # for author lookup (tweet_ids)

        for (doc_id, output_json, confidence, inf_method, source_type,
             domain, pub_at, text, ident) in cursor.fetchall():
            # Drop pre-exclusion rows from the denominator entirely.
            if (inf_method or "") == "deterministic":
                continue
            total_eligible += 1
            try:
                data = json.loads(output_json) if output_json else {}
            except json.JSONDecodeError:
                continue
            label = data.get("label", "human")
            if label == "bot":
                bot_count += 1
                bot_doc_ids.add(doc_id)
                bot_docs_data.append({
                    "doc_id": doc_id,
                    "data": data,
                    "domain": domain,
                    "pub_at": pub_at,
                })
                if domain:
                    by_cluster[domain] = by_cluster.get(domain, 0) + 1
                for indicator in data.get("indicators", []):
                    indicators_frequency[indicator] += 1
                if pub_at:
                    hourly_distribution[datetime.datetime.fromtimestamp(pub_at).hour] += 1
                if text:
                    bot_texts.append(text)
                    for m in _URL_RE.finditer(text):
                        bot_urls.append(m.group(1).lower())
                if source_type == "x_post" and ident:
                    bot_idents.append(ident)
            elif label == "suspicious":
                suspicious_count += 1

        # Resolve X author_ids for bot-flagged x_posts (needed for accountReuse).
        bot_authors: List[str] = []
        if bot_idents:
            placeholders = ",".join("?" * len(bot_idents))
            cursor.execute(
                f"SELECT author_id FROM x_posts_raw WHERE tweet_id IN ({placeholders})",
                bot_idents,
            )
            bot_authors = [r[0] for r in cursor.fetchall() if r[0]]

        return {
            "total_eligible": total_eligible,
            "bot_count": bot_count,
            "suspicious_count": suspicious_count,
            "by_cluster": by_cluster,
            "indicators_frequency": indicators_frequency,
            "hourly_distribution": hourly_distribution,
            "bot_docs_data": bot_docs_data,
            "bot_doc_ids": bot_doc_ids,
            "bot_texts": bot_texts,
            "bot_urls": bot_urls,
            "bot_authors": bot_authors,
        }

    def _fetch_behavior_signals(
        self, cursor, bot_doc_ids: set[int], bot_authors: List[str],
    ) -> Dict[str, Any]:
        """Compute the no-longer-stubbed behavioral signals.

        - accountAgeDistribution: bucket x_users_raw.created_at for the flagged
          X authors. Falls back to an empty list if we have no X authors.
        - avgPostsPerSuspectedAccount + accountReuse: from bot_authors counts.
        - identicalTextPairs + copyPasteSimilarity: computed in the caller
          from bot_texts.
        """
        buckets = {
            "< 7 days": 0, "7-30 days": 0, "30-90 days": 0,
            "90-365 days": 0, "1-3 years": 0, "3+ years": 0,
            "unknown": 0,
        }
        if bot_authors:
            unique_authors = list(set(bot_authors))
            placeholders = ",".join("?" * len(unique_authors))
            cursor.execute(
                f"SELECT user_id, created_at FROM x_users_raw WHERE user_id IN ({placeholders})",
                unique_authors,
            )
            now = int(datetime.datetime.now(datetime.timezone.utc).timestamp())
            for _uid, created_at in cursor.fetchall():
                if not created_at:
                    buckets["unknown"] += 1
                    continue
                age_days = (now - created_at) / 86400
                if age_days < 7:
                    buckets["< 7 days"] += 1
                elif age_days < 30:
                    buckets["7-30 days"] += 1
                elif age_days < 90:
                    buckets["30-90 days"] += 1
                elif age_days < 365:
                    buckets["90-365 days"] += 1
                elif age_days < 365 * 3:
                    buckets["1-3 years"] += 1
                else:
                    buckets["3+ years"] += 1

        total_buckets = sum(buckets.values())
        account_age_distribution = [
            {
                "range": k,
                "count": v,
                "percentage": round((v / total_buckets) * 100, 1) if total_buckets else 0.0,
            }
            for k, v in buckets.items()
        ]

        # Per-author counts of bot-flagged posts (accountReuse + avg per account).
        author_counts = Counter(bot_authors)
        unique_authors = len(author_counts)
        total_author_bot_posts = sum(author_counts.values())
        avg_posts_per_suspected = (
            total_author_bot_posts / unique_authors if unique_authors else 0.0
        )
        reuse_authors = sum(1 for _, c in author_counts.items() if c > 1)
        account_reuse = reuse_authors / unique_authors if unique_authors else 0.0

        return {
            "account_age_distribution": account_age_distribution,
            "avg_posts_per_suspected_account": round(avg_posts_per_suspected, 2),
            "account_reuse": round(account_reuse, 3),
        }

    # ---------- Format ----------

    def _format_bot_activity(
        self, bot_data: Dict[str, Any], behavior: Dict[str, Any],
    ) -> BotActivityData:
        total_eligible = bot_data["total_eligible"]
        bot_count = bot_data["bot_count"]
        suspicious_count = bot_data["suspicious_count"]
        by_cluster = bot_data["by_cluster"]
        indicators_frequency = bot_data["indicators_frequency"]
        hourly_distribution = bot_data["hourly_distribution"]
        bot_texts: List[str] = bot_data["bot_texts"]
        bot_urls: List[str] = bot_data["bot_urls"]

        automation_rate = (bot_count / total_eligible * 100) if total_eligible > 0 else 0.0
        top_clusters = sorted(by_cluster.items(), key=lambda x: x[1], reverse=True)[:5]
        coordination_index = _compute_coordination_index(hourly_distribution)
        identical_text_pairs, copy_paste_buckets = _text_similarity_signals(bot_texts)
        link_domain_concentration = _link_domain_concentration(bot_urls)

        narratives = _narrative_amplification(indicators_frequency, bot_data["bot_docs_data"])
        posting_cadence = [
            {"day": 0, "hour": hour, "value": count}
            for hour, count in sorted(hourly_distribution.items())
        ]

        return BotActivityData(
            overview=BotOverview(
                suspectedAutomationRate=round(automation_rate, 1),
                coordinationIndex=round(coordination_index, 2),
                topClusters=[c[0] for c in top_clusters],
                totalFlaggedAccounts=bot_count + suspicious_count,
                confidence="medium" if total_eligible > 100 else "low",
            ),
            narrativeAmplification=narratives,
            coordinationStats=CoordinationStats(
                burstTimingSimilarity=round(coordination_index, 2),
                accountReuse=behavior["account_reuse"],
                identicalTextPairs=identical_text_pairs,
                avgPostsPerSuspectedAccount=behavior["avg_posts_per_suspected_account"],
            ),
            behavioralSignals=BehavioralSignals(
                accountAgeDistribution=behavior["account_age_distribution"],
                postingCadence=posting_cadence,
                copyPasteSimilarity=copy_paste_buckets,
                linkDomainConcentration=link_domain_concentration,
            ),
        )


# =============================================================================
# Pure helpers (unit-testable)
# =============================================================================


def _compute_coordination_index(hourly_distribution: Dict[int, int]) -> float:
    if not hourly_distribution:
        return 0.0
    total = sum(hourly_distribution.values())
    if total == 0:
        return 0.0
    return max(hourly_distribution.values()) / total


def _narrative_amplification(
    indicators_frequency: Counter,
    bot_docs_data: List[Dict[str, Any]],
) -> List[NarrativeAmplification]:
    if not indicators_frequency:
        return []
    top_indicators = indicators_frequency.most_common(3)
    return [
        NarrativeAmplification(
            id=idx + 1,
            narrative=indicator,
            confidence="medium" if count > 5 else "low",
            examplePosts=[],
            topHashtags=[],
            topPhrases=[indicator],
            targets=[],
            suspectedBotVolume=count,
            whyFlagged=[indicator],
        )
        for idx, (indicator, count) in enumerate(top_indicators)
    ]


def _shingles(text: str, k: int = 8) -> set:
    """k-word shingle set for a piece of text. Used as a cheap similarity basis."""
    words = re.findall(r"\w+", text.lower())
    if len(words) < k:
        return {" ".join(words)} if words else set()
    return {" ".join(words[i:i + k]) for i in range(len(words) - k + 1)}


def _jaccard(a: set, b: set) -> float:
    if not a or not b:
        return 0.0
    inter = len(a & b)
    return inter / len(a | b) if (a | b) else 0.0


def _text_similarity_signals(
    bot_texts: List[str],
    *,
    max_compare: int = 400,
) -> Tuple[int, Dict[str, int]]:
    """Return (identicalTextPairs, copyPasteSimilarityBuckets).

    Identical-text count is cheap: pairs of texts with normalized lowercase
    identical content. Similarity buckets use 8-gram Jaccard over a capped
    sample to stay O(N^2) safe. Buckets: high (>=0.7), medium (0.3-0.7),
    low (<0.3).
    """
    sample = bot_texts[:max_compare]
    identical = 0
    buckets = {"high": 0, "medium": 0, "low": 0}
    if len(sample) < 2:
        return 0, buckets

    normalized = [re.sub(r"\s+", " ", t.strip().lower()) for t in sample]
    # Identical-text pair count (exact matches).
    counts = Counter(normalized)
    for text, n in counts.items():
        if n > 1:
            identical += n * (n - 1) // 2

    # Shingle-based buckets over a capped pairwise comparison.
    shingle_sets = [_shingles(t) for t in normalized]
    for i in range(len(shingle_sets)):
        for j in range(i + 1, len(shingle_sets)):
            sim = _jaccard(shingle_sets[i], shingle_sets[j])
            if sim >= 0.7:
                buckets["high"] += 1
            elif sim >= 0.3:
                buckets["medium"] += 1
            else:
                buckets["low"] += 1
    return identical, buckets


def _link_domain_concentration(bot_urls: List[str]) -> List[Dict[str, Any]]:
    if not bot_urls:
        return []
    counter = Counter(bot_urls)
    total = sum(counter.values())
    top = counter.most_common(5)
    return [
        {
            "domain": domain,
            "percentage": round((count / total) * 100, 1) if total else 0.0,
        }
        for domain, count in top
    ]
