"""
Target-tone merges — the received-vs-expressed split and outbound-target
rollup.

Merges pre-resolved ``target_mentions`` rows (identity frozen at write time,
migration 025) onto the sentiment result: received tone on each tracked
official, expressed-alignment controls, party-collective rollups, and the
"who they're talking about" outbound rollup on public-tier entities.

Imports the sample builders and the shared stance-count constants from
``samples`` (the leaf); the orchestrating ``aggregator`` imports the merge
entry points from here.
"""

from __future__ import annotations

import json
import math
from datetime import datetime
from typing import Any, Dict, List, Optional, Set, Tuple

from analysis.src.reporting.aggregators.rows import TargetMentionRow
from analysis.src.reporting.aggregators.sentiment.samples import (
    MAX_SAMPLES_PER_TARGET,
    _COLLECTIVE_LABELS,
    _STANCE_KEYS,
    _build_sample_dict,
    _insert_capped,
)
from analysis.src.reporting.entity_registry import (
    CATCH_ALL_OUTLETS, CATCH_ALL_SUBREDDITS, CATCH_ALL_X_USERS,
    DEM_COLLECTIVE, GOP_COLLECTIVE,
    canonicalize_handle, get_registry, resolve_entity,
)
from analysis.src.reporting.models import (
    EntitySentimentItem,
    PublicSentimentResult,
)


# --------------------------------------------------------------------------- #
#  Module constants                                                           #
# --------------------------------------------------------------------------- #

MAX_NARRATIVES_PER_TARGET = 5

# Account-level bot exclusion floor for received tone: (doc, target) pairs
# authored by an X account whose author_bot_scores.score meets this are
# withheld from received-tone denominators. Doc-level bot exclusion already
# runs via bot_docs; this catches accounts whose pattern-of-posting scores
# bot-like even when individual posts pass. Excluded counts are surfaced in
# targetTone metadata — never silently dropped.
BOT_SCORE_AUTHOR_EXCLUSION = 0.5

# Received-tone suppression floor: a net score computed from fewer than this
# many (doc, target) pairs is withheld (net=None, lowSample=True) — one
# classified tweet must never render as a +100.0 headline. The volume is
# always emitted so the UI can show the honest n.
MIN_TARGET_SAMPLE_N = 5

# Trailing-day cap for the received-tone daily series (received.dailyTone),
# mirroring the expressed toneTrend cap so the two overlay on the same chart at
# the same resolution and payload bound.
_RECEIVED_TREND_MAX_DAYS = 30


def _received_day_key(published_at: Any) -> Optional[str]:
    """Calendar-day bucket ('YYYY-MM-DD') for a mention, or None when the
    timestamp is unparseable. Local-time, matching the expressed toneTrend
    (aggregator._day_key) so received and expressed series align by day.
    Defined locally to avoid importing from the aggregator (which imports this
    module — a cycle)."""
    if published_at is None:
        return None
    try:
        return datetime.fromtimestamp(float(published_at)).strftime("%Y-%m-%d")
    except (TypeError, ValueError, OSError, OverflowError):
        return None

# Outbound-target rollup on public-tier entities ("who they're talking
# about"). Named rows are capped; unresolved raw targets need to recur
# before earning a row — a one-off free-text target is grouped into
# "Other targets" rather than rendered as if it were an identified entity.
MAX_OUTBOUND_TARGETS = 8
MIN_OUTBOUND_RAW_RECURRENCE = 2
OUTBOUND_OTHER_LABEL = "Other targets"


def _empty_stance_counts() -> Dict[str, int]:
    return {k: 0 for k in _STANCE_KEYS}


def _net_or_none(counts: Dict[str, int], min_n: int = MIN_TARGET_SAMPLE_N) -> Optional[float]:
    """Net score for a stance-count cell, or None when below the suppression
    floor — small-n cells report their volume but never a headline number."""
    total = sum(counts.values())
    if total < min_n:
        return None
    return round((counts["positive"] - counts["negative"]) / total * 100, 1)


def _normalize_party(party: Optional[str]) -> Optional[str]:
    """Collapse registry party values to the R/D axis used for alignment.
    ``independent-dem`` caucuses with Democrats; anything else is outside
    the two-party alignment frame and returns None."""
    if not party:
        return None
    p = party.strip().lower()
    if p == "r":
        return "R"
    if p in ("d", "independent-dem"):
        return "D"
    return None


def _fetch_narrative_doc_map(cursor, cutoff: Optional[int]) -> Dict[int, List[Tuple[int, str]]]:
    """doc_id → [(narrative_id, name)] for docs clustered into narratives,
    bounded by the time window. Lets received tone answer WHICH recurring
    claims drive the mentions of an official, not just how many there are.
    Empty when the narrative tables haven't been created (fresh/test DBs)."""
    cursor.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='narrative_docs'"
    )
    if not cursor.fetchone():
        return {}
    sql = (
        "SELECT nd.doc_id, n.narrative_id, n.name "
        "FROM narrative_docs nd JOIN narratives n ON n.narrative_id = nd.narrative_id"
    )
    params: tuple = ()
    if cutoff is not None:
        sql += " JOIN docs d ON d.doc_id = nd.doc_id WHERE d.published_at >= ?"
        params = (cutoff,)
    cursor.execute(sql, params)
    out: Dict[int, List[Tuple[int, str]]] = {}
    for doc_id, narrative_id, name in cursor.fetchall():
        out.setdefault(doc_id, []).append((narrative_id, name))
    return out


def _fetch_doc_topics(cursor, min_conf: float) -> Dict[int, str]:
    """doc_id → dominant LLM-extracted topic from target_mentions.

    The schema-enforced per-target topic (TARGET_TOPIC_ENUM) is the real
    topic signal; the title-keyword heuristic (_extract_topic) is only the
    fallback for docs with no resolved mention topic. Joined through
    ai_outputs_latest so superseded outputs' mentions don't vote. 'Other'
    is excluded — an all-'Other' doc falls through to the keyword fallback
    rather than surfacing a bucket that overlaps with "General". Ties break
    by higher mention count, then alphabetically (stable across runs).
    Empty when the table hasn't been created (fresh/test DBs)."""
    cursor.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='target_mentions'"
    )
    if not cursor.fetchone():
        return {}
    cursor.execute(
        """
        SELECT m.doc_id, m.topic, COUNT(*) AS n
        FROM target_mentions m
        JOIN ai_outputs_latest a ON a.output_id = m.output_id
        WHERE m.topic IS NOT NULL AND m.topic != 'Other'
          AND m.confidence >= ?
        GROUP BY m.doc_id, m.topic
        ORDER BY m.doc_id, n DESC, m.topic
        """,
        (min_conf,),
    )
    topics: Dict[int, str] = {}
    for doc_id, topic, _n in cursor.fetchall():
        topics.setdefault(doc_id, topic)
    return topics


def _speaker_tier(tier: Optional[str], ap_tier: Optional[str]) -> Optional[str]:
    """Coarse WHO-is-talking bucket for a target_sentiment row: 'officials'
    (registry match, provenance flag, or curated elected_official tier),
    'affiliated' (curated politically-affiliated accounts), else the
    resolve_entity tier ('news' | 'public' | None)."""
    if tier == "officials" or ap_tier == "elected_official":
        return "officials"
    if ap_tier == "affiliated":
        return "affiliated"
    return tier


def _engagement_weight(engagement: Any) -> float:
    """Log-damped reach-proxy weight for one (doc, target) pair: 1 for zero
    or unknown engagement, growing sub-linearly so one viral post can't
    single-handedly own an official's weighted net."""
    try:
        e = max(0, int(engagement or 0))
    except (TypeError, ValueError):
        e = 0
    return 1.0 + math.log1p(e)


def _format_received(accum: Dict[str, Any]) -> Dict[str, Any]:
    """Materialize a per-target accumulator into the wire shape stored on
    EntitySentimentItem.received / targetTone.collectives."""
    counts = accum["counts"]
    volume = sum(counts.values())
    low_sample = volume < MIN_TARGET_SAMPLE_N
    by_topic = []
    for topic, topic_counts in accum["by_topic"].items():
        topic_volume = sum(topic_counts.values())
        by_topic.append({
            "topic": topic,
            "net": _net_or_none(topic_counts),
            "volume": topic_volume,
            "lowSample": topic_volume < MIN_TARGET_SAMPLE_N,
        })
    by_topic.sort(key=lambda c: -c["volume"])

    by_speaker_tier = []
    for speaker, tier_counts in accum["by_speaker"].items():
        tier_volume = sum(tier_counts.values())
        by_speaker_tier.append({
            "tier": speaker,
            "net": _net_or_none(tier_counts),
            "volume": tier_volume,
            "lowSample": tier_volume < MIN_TARGET_SAMPLE_N,
        })
    by_speaker_tier.sort(key=lambda c: -c["volume"])

    by_narrative = []
    for nid, cell in accum["by_narrative"].items():
        n_volume = sum(cell["counts"].values())
        by_narrative.append({
            "narrativeId": nid,
            "name": cell["name"],
            "net": _net_or_none(cell["counts"]),
            "volume": n_volume,
            "lowSample": n_volume < MIN_TARGET_SAMPLE_N,
        })
    by_narrative.sort(key=lambda c: -c["volume"])
    by_narrative = by_narrative[:MAX_NARRATIVES_PER_TARGET]

    # Engagement-weighted net shares the raw-pair suppression floor: the
    # weighting changes emphasis within a sample, never rescues a thin one.
    weighted_net = None
    if not low_sample and accum["w_total"] > 0:
        weighted_net = round(
            (accum["w_pos"] - accum["w_neg"]) / accum["w_total"] * 100, 1
        )

    # Received-tone daily series (trailing window), suppressed below the sample
    # floor per day so a 1-mention day never draws a spike. Powers overlaying an
    # official's "tone directed at them" on the Tone-over-time chart.
    daily_tone = [
        {
            "date": d,
            "net": _net_or_none(day_counts),
            "volume": sum(day_counts.values()),
            "lowSample": sum(day_counts.values()) < MIN_TARGET_SAMPLE_N,
        }
        for d, day_counts in sorted(accum.get("by_day", {}).items())[-_RECEIVED_TREND_MAX_DAYS:]
    ]

    return {
        "net": _net_or_none(counts),
        "volume": volume,
        "lowSample": low_sample,
        "engagementWeightedNet": weighted_net,
        "byTopic": by_topic,
        "bySpeakerTier": by_speaker_tier,
        "byNarrative": by_narrative,
        "dailyTone": daily_tone,
        "samples": accum["samples"],
    }


def _get_or_create_official_item(
    result: PublicSentimentResult,
    by_key: Dict[str, EntitySentimentItem],
    registry,
    key: str,
) -> Optional[EntitySentimentItem]:
    """Find the official's expressed-tone item, or create a zero-volume one.

    An official who never posted in the window but is discussed by others
    still needs a card — received tone is exactly the metric that exists
    without their participation."""
    item = by_key.get(key)
    if item is not None:
        return item
    official = registry.officials.get(key)
    if official is None:
        return None
    item = EntitySentimentItem(
        key=key, kind="official",
        positive=0, negative=0, neutral=0, volume=0, netScore=0.0,
        entity_profile=official.profile_dict(),
    )
    result.byOfficial.append(item)
    by_key[key] = item
    return item


def _merge_target_tone(
    result: PublicSentimentResult,
    target_rows: List[tuple],
    bot_docs: Set[int],
    bot_score_authors: Optional[Set[str]] = None,
    narrative_map: Optional[Dict[int, List[Tuple[int, str]]]] = None,
) -> None:
    """Merge pre-resolved target_mentions rows (one per (doc, target),
    identity frozen at write time — migration 025) into the result:

    * ``received`` onto each tracked official's EntitySentimentItem — the
      reputational signal (how sampled posts talk ABOUT them), kept separate
      from netScore, which stays the expressed signal (their own posts).
      Enriched with an engagement-weighted net (reach proxy), a
      by-speaker-tier split (WHO is talking: news / officials / affiliated /
      public), and the top narratives driving the mentions.
    * ``expressed_alignment`` per official speaker — stance toward same-party
      vs cross-party tracked targets, the control that separates "criticizing
      the other side" (expected) from something newsworthy.
    * ``result.targetTone`` — suppression threshold, resolution coverage,
      party-collective rollups, global alignment baselines, and the count of
      mentions excluded because their author's account-level bot score is
      high (``bot_score_authors``, from author_bot_scores).

    The per-mention confidence floor is applied by the SQL fetch (row-level
    confidence is a mean and would hide a confident target next to an
    uncertain one). Unresolved mentions arrive as rows with a NULL
    entity_key — persisted, counted, never silently dropped. No-op when no
    rows exist, so pre-target snapshots keep their exact shape.
    """
    if not target_rows:
        return
    bot_score_authors = bot_score_authors or set()
    narrative_map = narrative_map or {}

    registry = get_registry()
    received: Dict[str, Dict[str, Any]] = {}
    alignment: Dict[str, Dict[str, Dict[str, int]]] = {}
    baseline = {"same": _empty_stance_counts(), "cross": _empty_stance_counts()}
    resolved_mentions = 0
    unresolved_mentions = 0
    bot_excluded_mentions = 0

    # Per-doc context cache: speaker resolution, engagement weight, doc
    # narratives, and the doc-level reasoning are identical for every
    # mention of the same doc.
    doc_ctx: Dict[int, Dict[str, Any]] = {}

    for raw_row in target_rows:
        m = TargetMentionRow.from_row(raw_row)
        doc_id, key, kind, party = m.doc_id, m.entity_key, m.entity_kind, m.entity_party
        stance, topic, conf, evidence_json = m.stance, m.topic, m.confidence, m.evidence_json
        output_json, source_type, published_at, title = (
            m.output_json, m.source_type, m.published_at, m.title)
        domain_or_subreddit, ident, text, x_handle = (
            m.domain_or_subreddit, m.ident, m.text, m.x_handle)
        is_official_tier, author_id, ap_tier = m.is_official_tier, m.author_id, m.ap_tier
        engagement = m.engagement
        if doc_id in bot_docs:
            continue
        if author_id and author_id in bot_score_authors:
            # Account-level exclusion: the author's cross-post bot rollup is
            # high, so none of their stances count toward received tone. The
            # count keeps the exclusion auditable.
            bot_excluded_mentions += 1
            continue
        if stance not in _STANCE_KEYS:
            continue

        ctx = doc_ctx.get(doc_id)
        if ctx is None:
            try:
                reasoning = (json.loads(output_json) or {}).get("reasoning") or ""
            except json.JSONDecodeError:
                reasoning = ""
            # Speaker resolution for the alignment control: only posts
            # authored by a registry-matched official carry a speaker party.
            speaker_key = None
            speaker_party = None
            tier, entity = resolve_entity(
                registry, source_type, domain_or_subreddit, x_handle,
                is_official_tier=bool(is_official_tier),
            )
            if tier == "officials" and entity is not None:
                speaker_key = entity.handle
                speaker_party = _normalize_party(entity.party)
            ctx = {
                "reasoning": reasoning,
                "weight": _engagement_weight(engagement),
                "narratives": narrative_map.get(doc_id, []),
                "speaker_key": speaker_key,
                "speaker_party": speaker_party,
                "speaker_tier": _speaker_tier(tier, ap_tier),
                "day": _received_day_key(published_at),
            }
            doc_ctx[doc_id] = ctx

        if key is None:
            unresolved_mentions += 1
            continue
        resolved_mentions += 1
        weight = ctx["weight"]

        accum = received.setdefault(key, {
            "kind": kind,
            "counts": _empty_stance_counts(),
            "by_topic": {},
            "by_speaker": {},
            "by_narrative": {},
            "by_day": {},
            "w_pos": 0.0, "w_neg": 0.0, "w_total": 0.0,
            "samples": [],
        })
        accum["counts"][stance] += 1
        if ctx["day"] is not None:
            accum["by_day"].setdefault(
                ctx["day"], _empty_stance_counts()
            )[stance] += 1
        accum["by_topic"].setdefault(
            topic or "Other", _empty_stance_counts()
        )[stance] += 1
        if ctx["speaker_tier"] is not None:
            accum["by_speaker"].setdefault(
                ctx["speaker_tier"], _empty_stance_counts()
            )[stance] += 1
        for nid, n_name in ctx["narratives"]:
            cell = accum["by_narrative"].setdefault(
                nid, {"name": n_name, "counts": _empty_stance_counts()},
            )
            cell["counts"][stance] += 1
        accum["w_total"] += weight
        if stance == "positive":
            accum["w_pos"] += weight
        elif stance == "negative":
            accum["w_neg"] += weight

        try:
            evidence_spans = json.loads(evidence_json) if evidence_json else []
        except json.JSONDecodeError:
            evidence_spans = []
        sample = _build_sample_dict(
            doc_id, stance.upper(), conf,
            {"reasoning": ctx["reasoning"], "evidence_spans": evidence_spans},
            title, source_type, published_at, domain_or_subreddit, ident, text,
            x_handle, topic=topic or "Other",
        )
        _insert_capped(accum["samples"], sample, MAX_SAMPLES_PER_TARGET)

        # Alignment cell — self-references are excluded: "same party"
        # means another entity on the speaker's side, not self-praise.
        target_party = _normalize_party(party)
        if (ctx["speaker_key"] and ctx["speaker_party"] and target_party
                and key != ctx["speaker_key"]):
            cell = "same" if ctx["speaker_party"] == target_party else "cross"
            cells = alignment.setdefault(ctx["speaker_key"], {
                "same": _empty_stance_counts(), "cross": _empty_stance_counts(),
            })
            cells[cell][stance] += 1
            baseline[cell][stance] += 1

    by_key = {item.key: item for item in result.byOfficial}
    collectives: Dict[str, Dict[str, Any]] = {}
    for key, accum in received.items():
        formatted = _format_received(accum)
        if key in (GOP_COLLECTIVE, DEM_COLLECTIVE):
            collectives[key] = formatted
            continue
        item = _get_or_create_official_item(result, by_key, registry, key)
        if item is not None:
            item.received = formatted

    for speaker_key, cells in alignment.items():
        item = _get_or_create_official_item(result, by_key, registry, speaker_key)
        if item is None:
            continue
        item.expressed_alignment = {
            "samePartyNet": _net_or_none(cells["same"]),
            "samePartyVolume": sum(cells["same"].values()),
            "crossPartyNet": _net_or_none(cells["cross"]),
            "crossPartyVolume": sum(cells["cross"].values()),
        }

    # Re-sort so officials created target-only slot in with the rest.
    result.byOfficial.sort(key=lambda it: (it.kind == "catch_all", -it.volume))

    result.targetTone = {
        "minSampleN": MIN_TARGET_SAMPLE_N,
        "resolvedMentions": resolved_mentions,
        "unresolvedMentions": unresolved_mentions,
        "botExcludedMentions": bot_excluded_mentions,
        "engagementWeight": (
            "1 + ln(1 + retweets + replies + likes + quotes) per sampled "
            "post; engagement counts are a reach proxy, not verified reach"
        ),
        "collectives": collectives,
        "baselines": {
            "samePartyNet": _net_or_none(baseline["same"]),
            "samePartyVolume": sum(baseline["same"].values()),
            "crossPartyNet": _net_or_none(baseline["cross"]),
            "crossPartyVolume": sum(baseline["cross"].values()),
        },
    }


def _merge_outbound_targets(
    result: PublicSentimentResult,
    target_rows: List[tuple],
    bot_docs: Set[int],
    bot_score_authors: Optional[Set[str]] = None,
) -> None:
    """Attach "who they're talking about" to PUBLIC and NEWS entities.

    The inverse of ``received``: target_mentions rows grouped by the
    AUTHORING bucket (news outlet / subreddit / sampled author / catch-all)
    instead of the mentioned target. Answers who a bucket's sentiment is
    directed at — e.g. a news outlet's expressed stance toward each party.

    Bucket keys mirror the final ``byGeneralPublic`` / ``byNewsOutlet`` items:
    an author whose per-handle bucket was consolidated below the card floors
    folds into the tier's catch-all here too, so the rollup always lands on a
    card that actually renders. Resolved targets show their registry name;
    party collectives their fixed label; unresolved raw targets earn a row
    only when they recur (``MIN_OUTBOUND_RAW_RECURRENCE``), otherwise they
    pool into "Other targets" — identity is never fabricated from one
    free-text string. Nets share the MIN_TARGET_SAMPLE_N suppression floor.
    """
    if not target_rows:
        return
    bot_score_authors = bot_score_authors or set()
    registry = get_registry()
    public_items = {item.key: item for item in result.byGeneralPublic}
    news_items = {item.key: item for item in result.byNewsOutlet}
    if not public_items and not news_items:
        return

    # (tier, bucket_key) -> target_group_key -> {label, kind, entityKey, counts}
    accum: Dict[Tuple[str, str], Dict[str, Dict[str, Any]]] = {}

    for raw_row in target_rows:
        m = TargetMentionRow.from_row(raw_row)
        doc_id, key, stance = m.doc_id, m.entity_key, m.stance
        source_type, domain_or_subreddit, x_handle = (
            m.source_type, m.domain_or_subreddit, m.x_handle)
        is_official_tier, author_id, ap_tier = m.is_official_tier, m.author_id, m.ap_tier
        raw_target = m.raw_target
        if doc_id in bot_docs:
            continue
        if author_id and author_id in bot_score_authors:
            continue
        if stance not in _STANCE_KEYS:
            continue

        tier, entity = resolve_entity(
            registry, source_type, domain_or_subreddit, x_handle,
            is_official_tier=bool(is_official_tier),
        )

        # Route to the news or public bucket that authored the mention. Curated
        # elected officials route to the officials column (handled by received),
        # not here. (Officials expressing tone about others is expressed_
        # alignment; this surface is news + public.)
        if tier == "news":
            bucket_key = entity.domain if entity is not None else CATCH_ALL_OUTLETS
            if bucket_key not in news_items:
                continue
            tkey = "news"
        elif tier == "public" and ap_tier != "elected_official":
            if entity is not None:
                bucket_key = entity.subreddit
            elif source_type == "x_post" and x_handle:
                bucket_key = canonicalize_handle(x_handle) or x_handle.lower()
            elif source_type == "x_post":
                bucket_key = CATCH_ALL_X_USERS
            else:
                bucket_key = CATCH_ALL_SUBREDDITS
            if bucket_key not in public_items:
                # Consolidated sampled author (or a bucket with no sentiment
                # card) — fold into the tier's catch-all.
                bucket_key = (
                    CATCH_ALL_X_USERS if source_type == "x_post"
                    else CATCH_ALL_SUBREDDITS
                )
                if bucket_key not in public_items:
                    continue
            tkey = "public"
        else:
            continue

        if key is not None:
            if key in _COLLECTIVE_LABELS:
                group_key = key
                label = _COLLECTIVE_LABELS[key]
                target_kind = "collective"
            else:
                group_key = key
                official = registry.officials.get(key)
                label = (
                    official.profile_dict().get("displayName") if official
                    else key
                )
                target_kind = "official"
        else:
            raw = (raw_target or "").strip()
            if not raw:
                continue
            group_key = f"raw:{raw.lower()}"
            label = raw
            target_kind = "raw"

        bucket = accum.setdefault((tkey, bucket_key), {})
        cell = bucket.setdefault(group_key, {
            "label": label, "kind": target_kind,
            "entityKey": key,
            "counts": _empty_stance_counts(),
        })
        cell["counts"][stance] += 1

    for (tkey, bucket_key), groups in accum.items():
        item = (news_items if tkey == "news" else public_items).get(bucket_key)
        if item is None:
            continue
        named: List[Dict[str, Any]] = []
        other = _empty_stance_counts()
        for cell in groups.values():
            volume = sum(cell["counts"].values())
            if cell["kind"] == "raw" and volume < MIN_OUTBOUND_RAW_RECURRENCE:
                for stance_key, n in cell["counts"].items():
                    other[stance_key] += n
                continue
            named.append(cell)
        named.sort(key=lambda c: -sum(c["counts"].values()))
        for cell in named[MAX_OUTBOUND_TARGETS:]:
            for stance_key, n in cell["counts"].items():
                other[stance_key] += n
        named = named[:MAX_OUTBOUND_TARGETS]

        targets = [
            {
                "label": cell["label"],
                "entityKey": cell["entityKey"],
                "kind": cell["kind"],
                "net": _net_or_none(cell["counts"]),
                "volume": sum(cell["counts"].values()),
                "lowSample": sum(cell["counts"].values()) < MIN_TARGET_SAMPLE_N,
            }
            for cell in named
        ]
        other_volume = sum(other.values())
        if other_volume:
            targets.append({
                "label": OUTBOUND_OTHER_LABEL,
                "entityKey": None,
                "kind": "other",
                "net": _net_or_none(other),
                "volume": other_volume,
                "lowSample": other_volume < MIN_TARGET_SAMPLE_N,
            })
        if targets:
            item.outbound = {
                "minSampleN": MIN_TARGET_SAMPLE_N,
                "volume": sum(t["volume"] for t in targets),
                "targets": targets,
            }
