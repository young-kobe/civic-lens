"""
Narrative projection: assemble ``NarrativeSummary`` objects from the raw fact
maps the repository returns.

Everything presentation-shaped lives here — the label→tone mapping, sentiment
JSON parsing, source labels / URLs / snippets (via ``evidence.py``), reasoning
truncation, first-seen author-faction display, the registry three-way frame
lookup, and the cross-tier fold. The repository owns SQL; the projector owns
meaning. Keeping the two apart is what let the SQL be batched without touching
any of the per-narrative business rules.
"""

import json
from typing import Any, Dict, List, Optional, Sequence, Tuple

from analysis.src.reporting.aggregators.base import (
    build_sample_author,
    build_sample_engagement,
)
from analysis.src.reporting.aggregators.evidence import (
    build_doc_url,
    build_source_label,
    text_snippet,
)
from analysis.src.reporting.aggregators.rows import NarrativeSupportingDocRow
from analysis.src.reporting.entity_registry import (
    catch_all_profile,
    CATCH_ALL_OUTLETS,
    CATCH_ALL_SUBREDDITS,
    CATCH_ALL_X_USERS,
    get_registry,
    route_reporting_entity,
)
from analysis.src.reporting.models import NarrativeSummary

# Per-source-type display labels for the source_breakdown chips. Narrative-
# internal (distinct from evidence.build_source_label, which builds the richer
# "News · nyt.com" evidence label). Kept next to the code that renders it.
_SOURCE_LABELS = {
    "news": "News",
    "reddit_post": "Reddit",
    "reddit_comment": "Reddit",
    "x_post": "X",
}


def registry_lookup(
    source_type: Optional[str],
    domain: Optional[str],
    x_handle: Optional[str],
) -> Tuple[Optional[str], Optional[Dict[str, Any]]]:
    """(tier_group, entity_profile_dict) for the first-seen doc.
    Catch-all profile when source is classifiable but no registry
    entry matched; (None, None) when source_type is unknown."""
    route = route_reporting_entity(get_registry(), source_type, domain, x_handle)
    if route is None:
        return None, None
    if route.entity_profile is not None:
        return route.tier, route.entity_profile
    # Matched tier but no registry entity — narrative keeps its own (singular)
    # display copy, keyed off the shared router's catch-all sentinel.
    if route.key == CATCH_ALL_OUTLETS:
        return route.tier, catch_all_profile(
            CATCH_ALL_OUTLETS, "Other news outlets",
            "News doc whose domain is not in the tracked outlet registry.",
        )
    if route.key == CATCH_ALL_X_USERS:
        return route.tier, catch_all_profile(
            CATCH_ALL_X_USERS, "Other X users",
            "X post whose author is not in the tracked officials registry.",
        )
    if route.key == CATCH_ALL_SUBREDDITS:
        return route.tier, catch_all_profile(
            CATCH_ALL_SUBREDDITS, "Other subreddits",
            "Reddit post whose subreddit is not in the tracked subreddit registry.",
        )
    return route.tier, None


def build_source_breakdown(
    rows: List[Tuple[Optional[str], int]],
) -> List[Dict[str, Any]]:
    return [
        {"source_type": st, "label": _SOURCE_LABELS.get(st, st), "count": c}
        for st, c in rows
    ]


def build_timeline(rows: List[Tuple[str, int]]) -> List[Dict[str, Any]]:
    return [{"date": day, "count": count} for day, count in rows]


def net_sentiment(rows: List[Tuple[Optional[str], Optional[float]]]) -> float:
    """Net sentiment (-100..+100) over supporting docs in the window.

    Maps POSITIVE→+conf, NEGATIVE→-conf, NEUTRAL/MIXED→0, averages, scales to %.
    NEUTRAL/MIXED contribute 0 to the numerator but DO count in the denominator
    (audit A-6). Low-confidence rows are already dropped in SQL (walkthrough
    039), so a 49-NEUTRAL + 1-POSITIVE narrative reports ~+2, not +90."""
    total = 0.0
    count = 0
    for output_json, conf in rows:
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
        count += 1
    if count == 0:
        return 0.0
    return round((total / count) * 100, 1)


def is_cross_tier(rows: List[Tuple[Optional[str], Optional[str]]]) -> bool:
    """True when supporting docs span >1 of {news, officials, public}.

    Tier assignment here is coarse and matches resolve_entity:
      * news → 'news'
      * x_post authored by a verified official → 'officials'
      * x_post by anyone else → 'public'
      * reddit_post / reddit_comment → 'public'
    """
    officials_handles = set(get_registry().officials.keys())
    tiers: set = set()
    for source_type, handle in rows:
        if source_type == "news":
            tiers.add("news")
        elif source_type == "x_post":
            tiers.add("officials" if handle in officials_handles else "public")
        elif source_type in ("reddit_post", "reddit_comment"):
            tiers.add("public")
        if len(tiers) >= 2:
            return True
    return False


def first_seen_fields(row: Optional[tuple]) -> tuple:
    """(source_type, domain, tier, author_profile, handle) from a first-seen
    docs row. ``tier`` / ``author_profile`` are only populated for x_post
    first-seen docs resolved via x_posts_raw.author_id → account_profiles.
    Naming rationale (first-seen = first-ingested-by-us): walkthrough 035."""
    if not row:
        return None, None, None, None, None
    (source_type, domain, tier, full_name, party, branch, chamber,
     state_or_district, office_title, account_type, author_id, username) = row

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


def build_supporting_docs(
    rows: Sequence[NarrativeSupportingDocRow],
) -> List[Dict[str, Any]]:
    """Modal drill-down table rows. Source label / URL are synthesized here so
    the UI doesn't re-derive them. Rows without a sentiment row still appear
    (null label/confidence/reasoning). The dedup guard is belt-and-braces:
    ai_outputs_latest + UNIQUE(narrative_id, doc_id) already yield one row per
    doc, but a duplicate would otherwise render the doc twice (audit A-11)."""
    out: List[Dict[str, Any]] = []
    seen_doc_ids: set = set()
    for r in rows:
        if r.doc_id in seen_doc_ids:
            continue
        seen_doc_ids.add(r.doc_id)

        sentiment_label = None
        reasoning = None
        if r.output_json:
            try:
                parsed = json.loads(r.output_json)
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

        out.append({
            "doc_id": r.doc_id,
            "title": r.title or None,
            # Social posts (X, Reddit) have no headline; give the UI a text
            # snippet to show in the Headline column instead of "(untitled)".
            "snippet": text_snippet(r.text) if not r.title else None,
            "source_type": r.source_type or "unknown",
            "source_label": build_source_label(
                r.source_type, r.domain_or_subreddit, r.x_handle,
            ),
            "url": build_doc_url(
                r.source_type, r.domain_or_subreddit, r.ident, r.x_handle,
            ),
            "published_at": r.published_at,
            "sentiment_label": sentiment_label,
            "confidence": float(r.confidence) if r.confidence is not None else None,
            "reasoning": reasoning,
            "engagement": build_sample_engagement(
                r.source_type, r.x_retweets, r.x_replies, r.x_likes, r.x_quotes,
                r.reddit_score, r.reddit_comments,
            ),
            "author": build_sample_author(
                r.source_type, r.x_handle, r.u_name, r.u_avatar,
                r.u_verified_type, r.u_followers, r.u_created_at, bio=r.u_bio,
            ),
        })
    return out


def build_summary(
    ranked_row: tuple,
    *,
    first_seen_row: Optional[tuple],
    source_breakdown_rows: List[Tuple[Optional[str], int]],
    timeline_rows: List[Tuple[str, int]],
    sentiment_rows: List[Tuple[Optional[str], Optional[float]]],
    inbound: int,
    citation_detail: Tuple[Dict[str, int], int, List[Dict[str, Any]]],
    propaganda_score: Optional[float],
    bot_pushed_fraction: Optional[float],
    cross_tier_rows: List[Tuple[Optional[str], Optional[str]]],
    supporting_rows: Sequence[NarrativeSupportingDocRow],
    mean_confidence: Optional[float],
) -> NarrativeSummary:
    """Assemble one NarrativeSummary from the pre-fetched fact maps."""
    (narrative_id, name, first_seen_at, first_seen_doc_id,
     clustering_mode, clustering_threshold, embedding_model,
     support_count) = ranked_row

    # Clustering audit provenance (migration 015): how this cluster was formed.
    # embedding_model is null for jaccard-mode narratives.
    clustering = None
    if clustering_mode is not None:
        clustering = {
            "mode": clustering_mode,
            "threshold": clustering_threshold,
            "embedding_model": embedding_model,
        }

    (first_seen_source_type, first_seen_domain, first_seen_tier,
     first_seen_author, first_seen_handle) = first_seen_fields(first_seen_row)
    # For x_post docs we default the tier to 'general_public' when the author is
    # not in account_profiles. For news/reddit we leave it null.
    if first_seen_tier is None and first_seen_source_type == "x_post":
        first_seen_tier = "general_public"

    # Registry three-way frame (walkthrough 058) — independent of
    # first_seen_tier, which comes from account_profiles.
    tier_group, entity_profile = registry_lookup(
        first_seen_source_type, first_seen_domain, first_seen_handle,
    )

    inbound_by_link_type, external_citations, cross_narrative = citation_detail

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
        source_breakdown=build_source_breakdown(source_breakdown_rows),
        timeline=build_timeline(timeline_rows),
        net_sentiment=net_sentiment(sentiment_rows),
        inbound_citation_count=inbound,
        inbound_by_link_type=inbound_by_link_type,
        external_citation_count=external_citations,
        cross_narrative_citations=cross_narrative,
        propaganda_score=propaganda_score,
        bot_pushed_fraction=bot_pushed_fraction,
        first_seen_entity_profile=entity_profile,
        first_seen_tier_group=tier_group,
        cross_tier=is_cross_tier(cross_tier_rows),
        top_supporting_docs=build_supporting_docs(supporting_rows),
        mean_confidence=mean_confidence,
        clustering=clustering,
    )
