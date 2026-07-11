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

from analysis.src.reporting.aggregators.base import (
    X_AUTHOR_JOIN_SQL,
    get_connection,
    get_time_cutoff,
)
from analysis.src.reporting.aggregators.narrative import (
    _build_doc_url,
    _build_source_label,
)
from analysis.src.reporting.aggregators.sentiment import CATCH_ALL_VERIFIED_OFFICIALS
from analysis.src.reporting.entity_registry import (
    catch_all_profile,
    CATCH_ALL_SUBREDDITS,
    CATCH_ALL_X_USERS,
    get_registry,
    resolve_entity,
)
from analysis.src.reporting.models import (
    BotActivityData,
    BotEntityItem,
    BotOverview,
    CoordinationStats,
    BehavioralSignals,
    FlaggedExample,
    NarrativeAmplification,
)


_URL_RE = re.compile(r"https?://([^/\s]+)", re.IGNORECASE)


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
            bot_data = self._fetch_bot_detection_data(cursor, cutoff)
            behavior = self._fetch_behavior_signals(
                cursor,
                bot_data["bot_doc_ids"],
                bot_data["bot_authors"],
            )
            rollups = self._fetch_entity_rollups(cursor, cutoff)
            narratives = _fetch_narrative_amplification(
                cursor, bot_data["bot_docs_data"],
            )
        return self._format_bot_activity(bot_data, behavior, rollups, narratives)

    # ---------- Fetch ----------

    def _fetch_bot_detection_data(self, cursor, cutoff=None) -> Dict[str, Any]:
        """
        Returns counts + per-doc / per-author bookkeeping used downstream.
        Pre-excluded rows (inference_method='deterministic') are dropped from
        every count so the automation rate denominator is eligible posts only.
        ``cutoff`` (unix seconds, or None) windows the scan by published_at.
        """
        # LEFT JOIN x_posts_raw + x_users_raw so bot-flagged x_posts
        # carry the author handle we need to synthesize an X permalink in
        # _narrative_amplification (C1 invariant: every evidence surface
        # links back to the original). This join is a flagged duplication
        # hotspot; see docs/todos/backend-aggregator-audit.md §1.
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
                   d.ident,
                   u.username
            FROM ai_outputs_latest a
            JOIN docs d ON a.doc_id = d.doc_id
            """
            + X_AUTHOR_JOIN_SQL
            + """
            WHERE a.task_type = 'bot_detection'
              AND d.source_type != 'news'
            """
            + ("" if cutoff is None else " AND d.published_at >= ?"),
            () if cutoff is None else (cutoff,),
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
             domain, pub_at, text, ident, x_handle) in cursor.fetchall():
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
                    "confidence": confidence,
                    "source_type": source_type,
                    "domain": domain,
                    "ident": ident,
                    "x_handle": x_handle,
                    "text": text,
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

    # ---------- Entity rollups (three-way Bot Detector grid) ----------

    def _fetch_entity_rollups(self, cursor, cutoff=None) -> Dict[str, List[BotEntityItem]]:
        """Per-entity bot-classification rates for the two-way grid.

        Runs one joined scan of ai_outputs.task='bot_detection' rows together
        with x author-handle lookup, then buckets each row into officials /
        public via ``resolve_entity`` — including the ingestor's
        ``is_official_tier`` provenance flag, so posts pulled via the
        verified-officials timeline land in the officials column even when
        the stored handle doesn't match the editorial registry (audit D-4;
        without this the Politicians & Officials tier stayed empty). Each
        entity also collects up to ``_SAMPLES_PER_ENTITY`` of its
        bot-flagged posts, confidence-ranked, so the card can open an
        evidence modal instead of dead-ending at an external link.

        News is excluded by contract (2026-07-11): articles are not
        accounts, so "automation rate of an outlet's articles" is not a
        real metric. ``by_news_outlet`` stays in the payload, permanently
        empty, so stale caches and older UI builds keep parsing.
        """
        cursor.execute(
            """
            SELECT a.doc_id, a.output_json, a.confidence, a.inference_method,
                   d.source_type, d.domain_or_subreddit, d.ident, d.text,
                   u.username, x.is_official_tier
            FROM ai_outputs_latest a
            JOIN docs d ON d.doc_id = a.doc_id
            """
            + X_AUTHOR_JOIN_SQL
            + """
            WHERE a.task_type = 'bot_detection'
              AND d.source_type != 'news'
            """
            + ("" if cutoff is None else " AND d.published_at >= ?"),
            () if cutoff is None else (cutoff,),
        )
        # (tier, key) -> accumulator
        accum: Dict[Tuple[str, str], Dict[str, Any]] = {}
        registry = get_registry()
        for (doc_id, output_json, confidence, inf_method, source_type,
             domain, ident, text, handle, is_official_tier) in cursor.fetchall():
            if (inf_method or "") == "deterministic":
                continue
            try:
                data = json.loads(output_json) if output_json else {}
            except json.JSONDecodeError:
                continue
            label = data.get("label", "human")

            tier, entity = resolve_entity(
                registry, source_type, domain, handle,
                is_official_tier=bool(is_official_tier),
            )
            if tier is None:
                continue
            if entity is not None:
                profile = entity.profile_dict()
            elif tier == "officials":
                # Provenance-flagged officials post with no editorial
                # registry entity — same bucket sentiment uses.
                profile = catch_all_profile(
                    CATCH_ALL_VERIFIED_OFFICIALS, "Verified officials",
                    "X posts pulled via the verified-officials timeline whose "
                    "handle is not individually in the editorial officials registry.",
                )
            elif source_type == "x_post":
                profile = catch_all_profile(
                    CATCH_ALL_X_USERS, "Other X users",
                    "X post whose author is not in the tracked officials registry.",
                )
            else:
                profile = catch_all_profile(
                    CATCH_ALL_SUBREDDITS, "Other subreddits",
                    "Reddit post whose subreddit is not in the tracked subreddit registry.",
                )
            key = profile["key"]

            slot = accum.setdefault((tier, key), {
                "kind": profile["kind"], "profile": profile,
                "total": 0, "bot": 0, "flagged": [],
            })
            slot["total"] += 1
            if label == "bot":
                slot["bot"] += 1
                slot["flagged"].append((
                    float(confidence or 0.0),
                    _flagged_example(
                        doc_id, text, source_type, domain, handle, ident,
                        data=data, confidence=confidence,
                    ),
                ))

        rollups: Dict[str, List[BotEntityItem]] = {
            "news": [], "officials": [], "public": [],
        }
        for (tier, key), s in accum.items():
            rate = (s["bot"] / s["total"] * 100) if s["total"] else 0.0
            samples = [
                ex for _conf, ex in
                sorted(s["flagged"], key=lambda pair: -pair[0])[:_SAMPLES_PER_ENTITY]
                if ex is not None
            ]
            rollups[tier].append(BotEntityItem(
                key=key, kind=s["kind"],
                total_docs=s["total"], bot_docs=s["bot"],
                bot_rate_pct=round(rate, 1),
                entity_profile=s["profile"],
                samples=samples,
            ))
        for tier, items in rollups.items():
            items.sort(key=_entity_sort_key)
        return rollups

    # ---------- Format ----------

    def _format_bot_activity(
        self, bot_data: Dict[str, Any], behavior: Dict[str, Any],
        rollups: Dict[str, List[BotEntityItem]],
        narratives: List[NarrativeAmplification],
    ) -> BotActivityData:
        total_eligible = bot_data["total_eligible"]
        bot_count = bot_data["bot_count"]
        suspicious_count = bot_data["suspicious_count"]
        by_cluster = bot_data["by_cluster"]
        hourly_distribution = bot_data["hourly_distribution"]
        bot_texts: List[str] = bot_data["bot_texts"]
        bot_urls: List[str] = bot_data["bot_urls"]

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


# =============================================================================
# Pure helpers (unit-testable)
# =============================================================================


def _entity_sort_key(item: BotEntityItem) -> Tuple[int, float, int]:
    """Sort bot-rollup entities: registry-matched first (catch-alls last),
    highest bot_rate_pct next, then highest bot_docs as tie-break."""
    is_catch_all = 1 if item.kind == "catch_all" else 0
    return (is_catch_all, -item.bot_rate_pct, -item.bot_docs)


def _compute_coordination_index(hourly_distribution: Dict[int, int]) -> float:
    if not hourly_distribution:
        return 0.0
    total = sum(hourly_distribution.values())
    if total == 0:
        return 0.0
    return max(hourly_distribution.values()) / total


# How many real posts to surface per amplified narrative / per entity
# modal. Each appears as an evidence excerpt with an outbound source link.
_EXAMPLES_PER_NARRATIVE = 3
_SAMPLES_PER_ENTITY = 5
# Character budget for the excerpt shown in the modal. Full text is
# one click away via the permalink; the preview is just for recognition.
_EXAMPLE_TEXT_CHARS = 220
# Caps for the derived chips in the amplification modal.
_MAX_HASHTAGS = 5
_MAX_TARGETS = 5
_MAX_WHY_FLAGGED = 3
# Per-example evidence caps (Phase 2d) — indicator chips + reasoning shown
# on each flagged post card.
_MAX_INDICATORS_PER_EXAMPLE = 4
_EXAMPLE_REASONING_CHARS = 240

_HASHTAG_RE = re.compile(r"#(\w{2,})")
_SLUG_RE = re.compile(r"^[a-z0-9_]+$")


def _humanize_indicator(indicator: str) -> str:
    """Display form of a bot-detection indicator. Heuristic indicators are
    already prose ("New account (12 days)"); LLM responses sometimes echo
    snake_case signal names ("zero_followers_following_listed") — those are
    real signals but must not render as raw slugs."""
    text = (indicator or "").strip()
    if _SLUG_RE.match(text):
        return text.replace("_", " ").capitalize()
    return text


def _flagged_example(
    doc_id: int,
    text: Any,
    source_type: Any,
    domain: Any,
    x_handle: Any,
    ident: Any,
    data: Optional[Dict[str, Any]] = None,
    confidence: Optional[float] = None,
) -> FlaggedExample | None:
    """Shape one bot-flagged doc into evidence-excerpt form, or None when
    there is no text to excerpt. Permalink via the shared _build_doc_url
    helper — C1 invariant: every evidence surface links back to the
    original.

    ``data`` (the parsed bot_detection output_json) and ``confidence``
    carry the per-doc WHY onto the example (Phase 2d): humanized
    indicators + truncated reasoning + flag confidence. Indicators pass
    through _humanize_indicator so LLM-echoed snake_case slugs never
    render raw; noise entries from pre-sanitization rows are still
    filtered display-side until those age out of the window."""
    excerpt = (text or "").strip()
    if not excerpt:
        return None
    if len(excerpt) > _EXAMPLE_TEXT_CHARS:
        excerpt = excerpt[:_EXAMPLE_TEXT_CHARS].rstrip() + "…"

    indicators: List[str] = []
    reasoning: Optional[str] = None
    if data:
        indicators = [
            _humanize_indicator(i)
            for i in data.get("indicators", [])[:_MAX_INDICATORS_PER_EXAMPLE]
            if isinstance(i, str) and i.strip()
        ]
        reasoning = data.get("reasoning") or None
        if reasoning and len(reasoning) > _EXAMPLE_REASONING_CHARS:
            reasoning = reasoning[:_EXAMPLE_REASONING_CHARS - 3].rstrip() + "..."

    return FlaggedExample(
        doc_id=doc_id,
        text=excerpt,
        source_label=_build_source_label(source_type, domain, x_handle),
        url=_build_doc_url(source_type, domain, ident, x_handle=x_handle),
        confidence=round(float(confidence), 3) if confidence is not None else None,
        indicators=indicators,
        reasoning=reasoning,
    )


def _fetch_narrative_amplification(
    cursor,
    bot_docs_data: List[Dict[str, Any]],
) -> List[NarrativeAmplification]:
    """Top narratives (real clustered claims from the narratives tables)
    whose supporting docs are bot-flagged.

    This replaced the pre-2026-07-10 version that presented bot INDICATOR
    strings as "narratives" — an internal signal name is not a talking
    point, and LLM-echoed slugs leaked straight into headline copy. Now:
      - narrative   = the narrative's actual name (narrative_docs join)
      - examplePosts = bot-flagged docs supporting that narrative
      - topHashtags = hashtags extracted from those docs' texts
      - targets     = who those docs take stances toward (target_mentions)
      - whyFlagged  = the humanized top indicators across those docs
    Empty when no bot-flagged doc belongs to a narrative — the UI states
    that honestly instead of inventing a cluster.
    """
    if not bot_docs_data:
        return []
    cursor.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='narrative_docs'"
    )
    if not cursor.fetchone():
        return []

    by_doc = {row["doc_id"]: row for row in bot_docs_data}
    placeholders = ",".join("?" * len(by_doc))
    cursor.execute(
        f"""
        SELECT nd.doc_id, n.narrative_id, n.name
        FROM narrative_docs nd
        JOIN narratives n ON n.narrative_id = nd.narrative_id
        WHERE nd.doc_id IN ({placeholders})
        """,
        list(by_doc),
    )
    groups: Dict[int, Dict[str, Any]] = {}
    for doc_id, narrative_id, name in cursor.fetchall():
        group = groups.setdefault(
            narrative_id,
            {"narrative_id": narrative_id, "name": name, "doc_ids": []},
        )
        group["doc_ids"].append(doc_id)

    top = sorted(groups.values(), key=lambda g: -len(g["doc_ids"]))[:3]
    out: List[NarrativeAmplification] = []
    for group in top:
        rows = [by_doc[d] for d in group["doc_ids"]]
        examples = []
        for row in rows:
            ex = _flagged_example(
                row["doc_id"], row.get("text"), row.get("source_type"),
                row.get("domain"), row.get("x_handle"), row.get("ident"),
                data=row.get("data"), confidence=row.get("confidence"),
            )
            if ex is not None:
                examples.append(ex)
            if len(examples) >= _EXAMPLES_PER_NARRATIVE:
                break

        indicator_counts: Counter = Counter()
        hashtag_counts: Counter = Counter()
        for row in rows:
            for indicator in row["data"].get("indicators", []):
                indicator_counts[_humanize_indicator(indicator)] += 1
            for tag in _HASHTAG_RE.findall(row.get("text") or ""):
                hashtag_counts[f"#{tag}"] += 1

        out.append(NarrativeAmplification(
            # The REAL narrative_id — the UI deep-links
            # "#narratives?open=<id>" from the amplification card, which
            # only resolves when this matches the Narratives payload.
            # (Was a synthetic 1..3 index before 2026-07-10.)
            id=group["narrative_id"],
            narrative=group["name"] or "",
            confidence="medium" if len(rows) > 5 else "low",
            examplePosts=examples,
            topHashtags=[t for t, _ in hashtag_counts.most_common(_MAX_HASHTAGS)],
            topPhrases=[],
            targets=_targets_for_docs(cursor, group["doc_ids"]),
            suspectedBotVolume=len(rows),
            whyFlagged=[i for i, _ in indicator_counts.most_common(_MAX_WHY_FLAGGED)],
        ))
    return out


def _targets_for_docs(cursor, doc_ids: List[int]) -> List[str]:
    """Display names of the entities the given docs take stances toward,
    from write-time-resolved target_mentions (migration 025). Empty when
    the table is absent (older test DBs) or nothing resolved."""
    if not doc_ids:
        return []
    cursor.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='target_mentions'"
    )
    if not cursor.fetchone():
        return []
    placeholders = ",".join("?" * len(doc_ids))
    cursor.execute(
        f"""
        SELECT m.entity_key, m.raw_target, COUNT(*) AS n
        FROM target_mentions m
        WHERE m.doc_id IN ({placeholders}) AND m.entity_key IS NOT NULL
        GROUP BY m.entity_key
        ORDER BY n DESC
        LIMIT {_MAX_TARGETS}
        """,
        doc_ids,
    )
    registry = get_registry()
    targets: List[str] = []
    for entity_key, raw_target, _n in cursor.fetchall():
        official = registry.officials.get(entity_key)
        targets.append(official.display_name if official else raw_target)
    return targets


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
