"""
Typed SQL row projections for the aggregators.

The aggregator queries assemble wide SELECTs from the shared fragments in
``base.py`` and then unpack the result tuples positionally (up to 28 columns).
Positional unpacking is fragile: reordering a column in the SELECT silently
shifts every downstream field, and the same wide tuple is unpacked in more than
one place (drift risk). These frozen dataclasses give each row named fields via
a ``from_row`` classmethod, so the SELECT-to-field mapping lives in exactly one
place per query and consumers read ``row.doc_id`` instead of ``row[0]``.

``from_row`` still assumes the SELECT column order matches the field order here
— that assumption is now centralized and named rather than duplicated across
unpack sites.
"""

from dataclasses import dataclass
from typing import Any, Optional


@dataclass(frozen=True)
class SentimentRow:
    """One ``task_type='sentiment'`` row: ai_outputs + docs + x author +
    account_profiles classification + the 12-column sample-enrichment
    projection. Column order matches the SELECT in
    ``SentimentAggregator.get_public_sentiment`` (base SELECT then
    ``SAMPLE_ENRICHMENT_SELECT``)."""

    doc_id: int
    output_json: Optional[str]
    confidence: Optional[float]
    source_type: Optional[str]
    published_at: Optional[float]
    title: Optional[str]
    domain_or_subreddit: Optional[str]
    ident: Optional[str]
    text: Optional[str]
    x_handle: Optional[str]
    is_official_tier: Any
    ap_tier: Optional[str]
    ap_full_name: Optional[str]
    ap_party: Optional[str]
    ap_office_title: Optional[str]
    ap_account_type: Optional[str]
    x_retweets: Any
    x_replies: Any
    x_likes: Any
    x_quotes: Any
    u_name: Optional[str]
    u_avatar: Optional[str]
    u_verified_type: Optional[str]
    u_followers: Any
    u_created_at: Any
    u_bio: Optional[str]
    reddit_score: Any
    reddit_comments: Any

    @classmethod
    def from_row(cls, row: tuple) -> "SentimentRow":
        return cls(*row)


@dataclass(frozen=True)
class TargetMentionRow:
    """One ``target_mentions`` row joined to ai_outputs_latest + docs + x
    author. Column order matches the ``target_sql`` SELECT in
    ``SentimentAggregator.get_public_sentiment``. Unpacked identically by the
    received-tone and outbound-target passes, which previously duplicated the
    21-column tuple and indexed it with magic positions (``r[0], r[1], ...``)."""

    doc_id: int
    entity_key: Optional[str]
    entity_kind: Optional[str]
    entity_party: Optional[str]
    stance: Optional[str]
    topic: Optional[str]
    confidence: Optional[float]
    evidence_json: Optional[str]
    output_json: Optional[str]
    source_type: Optional[str]
    published_at: Optional[float]
    title: Optional[str]
    domain_or_subreddit: Optional[str]
    ident: Optional[str]
    text: Optional[str]
    x_handle: Optional[str]
    is_official_tier: Any
    author_id: Optional[str]
    ap_tier: Optional[str]
    engagement: Any
    raw_target: Optional[str]

    @classmethod
    def from_row(cls, row: tuple) -> "TargetMentionRow":
        return cls(*row)


@dataclass(frozen=True)
class BotDetectionRow:
    """One ``task_type='bot_detection'`` row: ai_outputs_latest + docs + x
    author handle + the ingestor's ``is_official_tier`` provenance flag.

    Shared by both bot-detection scans in ``bot/repository.py`` — the
    per-doc automation-rate pass and the per-entity rollup pass — which both
    select this same 11-column projection (previously two separate
    positionally-unpacked tuples with different column sets, a drift risk).
    Each pass reads only the fields it needs (the detection pass ignores
    ``is_official_tier``; the rollup pass ignores ``published_at``)."""

    doc_id: int
    output_json: Optional[str]
    confidence: Optional[float]
    inference_method: Optional[str]
    source_type: Optional[str]
    domain_or_subreddit: Optional[str]
    published_at: Optional[float]
    ident: Optional[str]
    text: Optional[str]
    username: Optional[str]
    is_official_tier: Any

    @classmethod
    def from_row(cls, row: tuple) -> "BotDetectionRow":
        return cls(*row)


@dataclass(frozen=True)
class NarrativeSupportingDocRow:
    """One supporting-doc row for a narrative's modal drill-down table:
    docs + x author + the sentiment ai_output + the 12-column sample-enrichment
    projection. Column order matches the batched supporting-docs SELECT in
    ``NarrativeRepository.get_supporting_docs`` (the batching-only
    ``narrative_id`` and ``rn`` columns are stripped before ``from_row`` — they
    partition/rank the window, they are not fields of the doc itself)."""

    doc_id: int
    title: Optional[str]
    text: Optional[str]
    source_type: Optional[str]
    domain_or_subreddit: Optional[str]
    ident: Optional[str]
    published_at: Optional[float]
    x_handle: Optional[str]
    output_json: Optional[str]
    confidence: Optional[float]
    x_retweets: Any
    x_replies: Any
    x_likes: Any
    x_quotes: Any
    u_name: Optional[str]
    u_avatar: Optional[str]
    u_verified_type: Optional[str]
    u_followers: Any
    u_created_at: Any
    u_bio: Optional[str]
    reddit_score: Any
    reddit_comments: Any

    @classmethod
    def from_row(cls, row: tuple) -> "NarrativeSupportingDocRow":
        return cls(*row)
