"""
Pydantic response models for GET /bot-activity: automation-rate summary,
behavioral-signal breakdown, account-age buckets, per-entity bot rates,
bot-pushed narratives, flagged account/doc evidence, and the restored
pre-Postgres officials/general-public entity rollup, coordination stats,
and day-of-week x hour-of-day posting-cadence grid.
"""

from __future__ import annotations

from typing import List, Optional

from analysis.src.api.models.common import CamelModel, EntityProfileModel, LeanLabel, RangeMeta, SampleDocModel


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
    unattributable/NULL account ages are counted under 'unknown'.
    `percentage` is this bucket's share of all bot-flagged authors --
    restores the pre-Postgres `AccountAgeItem.percentage` field (0.0 when
    there are no bot-flagged authors at all, never divide-by-zero)."""

    age_range: str
    account_count: int
    percentage: float


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


class FlaggedExample(CamelModel):
    """One bot-flagged doc (`analysis.bot_signals.label='bot'` on a
    current run) shown as evidence on a `BotEntityItem` card. Restores the
    pre-Postgres `FlaggedExample` shape. `url` is `corpus.documents
    .source_url` -- always present (invariant C1), never synthesized.
    `indicators` are humanized from the run's own LLM response
    (`runs.raw_response -> 'llm' -> 'indicators'`, the same free-form
    signal list `bot_detection.py::analyze()` records) via
    `_humanize_indicator`; `reasoning` is that response's own `reasoning`
    field, truncated for display."""

    doc_id: int
    text: str
    url: str
    source_label: str
    confidence: Optional[float] = None
    reasoning: Optional[str] = None
    indicators: List[str] = []


class BotEntityItem(CamelModel):
    """One entity's bot-classification rollup for the officials /
    general-public grid (restores the pre-Postgres `BotEntityItem`).
    `key`/`kind` come from `entity_profile` for a registry-matched
    entity ('official' = editorial official, 'account' = non-editorial
    officials-list account, 'subreddit' = registry subreddit) or from the
    shared catch-all sentinel ('catch_all') when the doc's author/
    subreddit matches no registry entity. `samples` are confidence-ranked,
    capped at 3 -- an evidence preview, not a full drill-down."""

    key: str
    kind: str
    entity_profile: EntityProfileModel
    total_docs: int
    bot_docs: int
    bot_rate_pct: float
    samples: List[FlaggedExample] = []


class CoordinationStats(CamelModel):
    """Restores the pre-Postgres `CoordinationStats`. `account_reuse` is
    the share of bot-flagged (label='bot') authors in range who posted
    more than one bot-flagged doc; `avg_posts_per_suspected_account` is
    the mean bot-flagged-doc count over those same authors. Both are 0.0
    when no doc in range is bot-flagged. `identical_text_pairs` has no
    Postgres source (the old O(n^2) shingle-similarity scan was never
    ported) -- always None, never fabricated."""

    account_reuse: float
    avg_posts_per_suspected_account: float
    identical_text_pairs: Optional[int] = None


class PostingCadenceCell(CamelModel):
    """One cell of the day-of-week x hour-of-day bot-flagged posting-
    cadence grid (restores the pre-Postgres `HeatmapDataPoint`). All
    7 x 24 = 168 cells are present even at count 0. `day_of_week` follows
    Postgres `EXTRACT(dow)` (and JS `Date.getDay()`): Sunday=0 .. Saturday=6."""

    day_of_week: int
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
    # Docs labelled 'bot' OR 'suspicious' -- restores the pre-Postgres
    # BotOverview.totalFlaggedPosts (bot_count + suspicious_count), a
    # wider count than bot_scored_doc_count (which is author-share-gated).
    total_flagged_posts: int
    behavioral_signals: List[BehavioralSignalBucket] = []
    account_age_buckets: List[AccountAgeBucket] = []
    # Max single-hour share of the bot-flagged posting-cadence histogram
    # (posting_cadence) -- 1.0 means every bot-flagged doc in range posted
    # in the same UTC hour, 0.0 when there is no bot-flagged activity at
    # all. Reimplements the retired
    # reporting/aggregators/bot/metrics.py::_compute_coordination_index().
    coordination_index: float
    posting_cadence: List[PostingCadenceBucket] = []
    # Day-of-week x hour-of-day grid over the same bot-flagged docs --
    # restores the pre-Postgres BehavioralSignals.postingCadence heatmap.
    posting_cadence_grid: List[PostingCadenceCell] = []
    by_entity: List[EntityBotRate] = []
    # Officials / general-public entity rollups -- restores the
    # pre-Postgres BotOverview.by_official / by_general_public three-way
    # grid (news is out of scope here; there is no by_news_outlet).
    by_official: List[BotEntityItem] = []
    by_general_public: List[BotEntityItem] = []
    coordination_stats: CoordinationStats
    bot_pushed_narratives: List[BotPushedNarrative] = []
    flagged_accounts: List[FlaggedAccount] = []
    flagged_docs: List[SampleDocModel] = []
