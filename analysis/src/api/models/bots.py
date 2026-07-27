"""
Pydantic response models for GET /bot-activity: automation-rate summary,
behavioral-signal breakdown, account-age buckets, per-entity bot rates,
bot-pushed narratives, and flagged account/doc evidence.
"""

from __future__ import annotations

from typing import List, Optional

from analysis.src.api.models.common import CamelModel, LeanLabel, RangeMeta, SampleDocModel


class BehavioralSignalBucket(CamelModel):
    """Mean typed stylometrics (`analysis.bot_signals`) for one `bot_label`
    bucket -- shows why docs in that bucket read the way they do. Averages
    are None only if every row in the bucket has a NULL for that column."""

    label: str
    doc_count: int
    avg_llm_text_likelihood: Optional[float] = None
    avg_burstiness: Optional[float] = None
    avg_type_token_ratio: Optional[float] = None
    avg_template_score: Optional[float] = None


class AccountAgeBucket(CamelModel):
    """Count of DISTINCT bot-flagged (label='bot') authors whose
    `corpus.authors.account_created_at` falls in this age range --
    unattributable/NULL account ages are counted under 'unknown'."""

    age_range: str
    account_count: int


class EntityBotRate(CamelModel):
    """Bot-classification rate for one registry entity (`corpus.entities`,
    resolved via `corpus.author_profiles.entity_id`). Unlinked authors
    (general_public accounts with no registry match) are not represented
    here -- they surface only via `flagged_accounts`/`flagged_docs`."""

    entity_id: int
    entity_key: str
    kind: str
    display_name: str
    total_docs: int
    bot_docs: int
    bot_rate_pct: float


class PostingCadenceBucket(CamelModel):
    """Count of bot-flagged (`bot_signals.label='bot'`) docs published in
    this UTC hour-of-day (0-23) across the whole [start, end] window -- the
    histogram `coordinationIndex` is computed over. All 24 hours are
    present even at count 0."""

    hour: int
    doc_count: int


class BotPushedNarrative(CamelModel):
    """One recurring narrative (`analysis.narratives`) ranked by the share
    of its in-range member docs authored by a bot-scored account."""

    narrative_id: int
    name: str
    member_doc_count: int
    bot_authored_doc_count: int
    bot_fraction_pct: float
    samples: List[SampleDocModel] = []


class FlaggedAccount(CamelModel):
    """One example bot-scored account (`analysis.author_bot_scores`). Its
    lean surfaces ONLY as a derived `LeanLabel` (`analysis.author_leans`)
    -- never a bare string -- and is omitted entirely when no lean row
    exists or the row's lean is 'unknown' (the LeanLabel invariant)."""

    author_id: int
    platform: str
    handle: Optional[str] = None
    display_name: Optional[str] = None
    # Share (0..1) of this author's confidence-floored analyzed posts
    # labelled bot or suspicious (bot_post_count + suspicious_post_count,
    # over sample_count) -- owner decision 2026-07-25, replacing the
    # retired numeric bot_score. Named for what it now IS, not what it used
    # to be: a field called bot_score holding a share would be dishonest.
    flagged_post_share: float
    sample_count: int
    followers_count: Optional[int] = None
    lean: Optional[LeanLabel] = None
    samples: List[SampleDocModel] = []


class BotActivityResponse(CamelModel):
    """GET /bot-activity payload. `range` carries the resolved time
    bounds, the sampled/official_record doc split, and the distinct
    model_ids behind the aggregate (`RangeMeta`, owner decision
    2026-07-24)."""

    range: RangeMeta
    analyzed_doc_count: int
    bot_scored_doc_count: int
    automation_rate_pct: float
    behavioral_signals: List[BehavioralSignalBucket] = []
    account_age_buckets: List[AccountAgeBucket] = []
    # Max single-hour share of the bot-flagged posting-cadence histogram
    # (posting_cadence) -- 1.0 means every bot-flagged doc in range posted
    # in the same UTC hour, 0.0 when there is no bot-flagged activity at
    # all. Reimplements the retired
    # reporting/aggregators/bot/metrics.py::_compute_coordination_index().
    coordination_index: float
    posting_cadence: List[PostingCadenceBucket] = []
    by_entity: List[EntityBotRate] = []
    bot_pushed_narratives: List[BotPushedNarrative] = []
    flagged_accounts: List[FlaggedAccount] = []
    flagged_docs: List[SampleDocModel] = []
