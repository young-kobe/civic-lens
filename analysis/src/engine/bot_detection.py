"""
Bot detection engine (Postgres redesign Phase 6, Wave 3) -- ports old
engine/bot.py's HybridBotDetector.analyze_full onto the analyze()/process()
shape of text.py/targets.py. Unlike old bot.py, an LLM failure or
unavailable backend now raises out of analyze() instead of degrading to a
heuristic-only classification -- process() records a failed run, identical
to text.py's contract; there is no configured heuristic-only mode in this
stack. The deterministic stylometric/account signal battery still always
runs and feeds the LLM prompt: a successful call is 'hybrid' (both the
battery and the LLM's own judgment contributed to label/confidence). The
battery alone never classifies except for the empty-text 'unknown' case
below, which needs no LLM call at all.

Author fields (followers/following/verified/verified_type/account_created_at)
come from `corpus.authors` via the caller's join -- the old per-doc
x_users_raw DB read (`_enrich_x_metadata`) disappears with metadata_json.
Reddit has no author snapshot yet (`etl/authors.py::sync_reddit_authors` is
a documented no-op), so reddit_post docs simply carry no author fields and
fall through to the text-only signal subset, matching old bot.py's behavior
for reddit (it never populated X-specific metadata for non-X docs either).
"""

from __future__ import annotations

import re
import statistics
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional

from analysis.src.common import db
from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings
from analysis.src.engine.constants import (
    BOT_LABEL_MIN_CONFIDENCE,
    BOT_PROMPT_TEXT_MAX_CHARS,
    BOT_TASK,
    FOLLOW_RATIO_ANOMALY_MAX_FOLLOWER_SHARE,
    FOLLOW_RATIO_ANOMALY_MIN_FOLLOWING,
    LLM_HEDGE_PHRASES,
    LLM_TYPOGRAPHIC_TELLS,
    SPAM_KEYWORDS,
    X_LOW_FOLLOWERS_THRESHOLD,
    X_NEW_ACCOUNT_AGE_DAYS,
)
from analysis.src.llm.base import normalize_confidence
from analysis.src.llm.client import LLMClient
from analysis.src.llm.prompts import BOT_PROMPT_VERSION, BOT_SYSTEM_PROMPT, BOT_USER_PROMPT_TEMPLATE
from analysis.src.llm.schemas import BOT_SCHEMA
from analysis.src.results import store

logger = get_logger(__name__)

# =============================================================================
# Constants. BOT_TASK/BOT_PROMPT_TEXT_MAX_CHARS/the account-age-and-follower
# thresholds are consolidated into engine/constants.py (Postgres redesign
# Phase 6, constants-consolidation pass, 2026-07-23) -- imported above. The
# compiled regexes below stay module-local: patterns read by exactly one
# function each (see engine/constants.py's Wave 3 section for the stated
# rule).
#
# The _GOVERNMENT_VERIFIED_TYPE / _BUSINESS_VERIFIED_TYPE de-bias gate went
# with _aggregate_score on 2026-07-25: it hard-zeroed the score for verified
# government accounts and capped business accounts at 0.3. Nothing replaces
# it -- verified_type is still measured, still lands in raw_response, and
# still reaches the prompt, but nothing derived from it overrides the
# model's label/confidence.
#
# `analysis.bot_signals.score` / `analysis.author_bot_scores.score`+
# `.variance` are gone entirely as of 0005_drop_bot_score.sql -- `score` had
# become a literal duplicate of `llm_text_likelihood` (2026-07-25), and a
# duplicate column with no reader beyond the one it duplicates does not earn
# its keep. `BotAnalysis`/`BotSignalsRow` carry `llm_text_likelihood` only;
# the author-level exclusion gate reads `bot_post_count`/
# `suspicious_post_count`/`sample_count` instead (see
# `refresh_author_bot_scores()` below and
# `analysis/src/api/queries/constants.py::BOT_FLAGGED_SHARE_EXCLUSION`).
# =============================================================================

_SENTENCE_SPLIT_RE = re.compile(r"[.!?]+")


@dataclass(frozen=True)
class BotDocInput:
    """One doc's bot-engine input. Bot detection applies only to
    reddit_post/x_post docs (news is excluded upstream by the queue matrix,
    ops/etl/queue.py's seed SQL) -- this engine trusts the caller for that
    scoping and does not re-check source_type, matching text.py/targets.py's
    convention.

    Author fields are the caller's `corpus.authors` join (None when the doc
    has no author_id, or the author row lacks a snapshot value).
    `place_country_code` is doc-level, not author-level -- it is snapshotted
    per-post onto `corpus.x_posts` (a tweet's geotag, not the account's) and
    is None for reddit_post.

    Dropped vs. old bot.py's metadata dict, because corpus.authors carries
    no equivalent column and neither ever had a real producer: listed_count
    and tweet_count (no column in corpus.authors -- `unlisted_active_flag`
    and `sustained_tweet_rate_flag` are dropped signals, not ported) and
    posting_frequency/posts_per_day (never populated by any old producer
    either; a dead signal in production, confirmed by grep). handle/
    display_name are also omitted: the old heuristic battery never read them.
    """

    doc_id: int
    source_type: str
    text: str
    followers_count: Optional[int] = None
    following_count: Optional[int] = None
    verified_type: Optional[str] = None
    account_created_at: Optional[datetime] = None
    place_country_code: Optional[str] = None


@dataclass(frozen=True)
class BotAnalysis:
    """The validated outcome of one `analyze()` call.

    `burstiness`/`type_token_ratio`/`template_score` are the deterministic
    stylometric battery -- always computed the same way for every
    non-empty-text doc, so they carry one consistent meaning for
    rollups/thresholds regardless of inference_method (DDL comment on
    analysis.bot_signals). `confidence` and `label` come from the LLM for
    every successful (hybrid) run; the battery never classifies on its own
    except the empty-text 'unknown' case.

    Mapping decisions (see module docstring / task report for full
    rationale):
    - label: old vocabulary bot/suspicious/human/unknown maps 1:1 onto the
      DDL's analysis.bot_label enum, which DOES include 'unknown'
      (0001_north_star.sql) -- used for exactly the one case old bot.py used
      it for: doc.text empty/falsy, no signal battery possible.
    - inference_method: 'hybrid' for every successful classification
      (deterministic signals fed the LLM's prompt AND its own judgment set
      the label -- both contributed); 'deterministic' ONLY for the
      empty-text 'unknown' case. An LLM failure no longer degrades to a
      heuristic-only classification -- it raises (see analyze()'s
      docstring) and process() records a failed run instead.
    - burstiness = old sentence_length_variance; type_token_ratio = old
      unique_ratio (the literal type-token-ratio stylometric term);
      template_score = old hedge_phrase_rate (canned LLM template phrasing,
      the most literal "templated" signal of the remaining un-mapped ones).
      typographic_purity_score/repetition_score/spam hits/url/hashtag counts
      have no typed column and live only in raw_response.
    """

    label: str  # analysis.bot_label: human|suspicious|bot|unknown
    confidence: float
    llm_text_likelihood: Optional[float]
    burstiness: Optional[float]
    type_token_ratio: Optional[float]
    template_score: Optional[float]
    indicators: List[str]
    reasoning: Optional[str]
    inference_method: str  # 'deterministic' | 'hybrid'
    raw_response: Optional[Dict]


def analyze(doc: BotDocInput, client: LLMClient) -> BotAnalysis:
    """Bot signal for one doc. No DB access. Raises whatever
    `client.complete()` raises (RuntimeError: backend unavailable, or after
    retries are exhausted) -- process() below is what turns that into a
    recorded failed run, identical to text.py's contract. The deterministic
    signal battery runs first regardless: it feeds the LLM prompt and lands
    in the typed stylometric columns, so a successful call is 'hybrid'
    (both the battery and the LLM's own judgment contributed).
    """
    if not doc.text:
        return BotAnalysis(
            label="unknown", confidence=0.0, llm_text_likelihood=None,
            burstiness=None, type_token_ratio=None, template_score=None,
            indicators=[], reasoning="Empty text", inference_method="deterministic",
            raw_response=None,
        )

    signals = _compute_signals(doc)
    return _llm_analysis(doc.text, signals, client)


# =============================================================================
# Deterministic signal battery
# =============================================================================


def _compute_signals(doc: BotDocInput) -> Dict[str, Any]:
    """Deterministic text + account signals -- one flat dict for
    auditability, same convention as old bot.py's _compute_signals."""
    text_lower = doc.text.lower()
    words = text_lower.split()
    word_count = len(words)

    spam_found = [kw for kw in SPAM_KEYWORDS if kw in text_lower]
    unique_ratio = len(set(words)) / word_count if word_count > 0 else 1.0
    repetition_score = max(0.0, 1 - unique_ratio) if word_count > 5 else 0.0
    url_count = text_lower.count("http://") + text_lower.count("https://")
    hashtag_count = doc.text.count("#")

    sentence_length_variance = _sentence_length_variance(doc.text)
    hedge_hits, hedge_rate = _hedge_stats(text_lower, word_count)
    typographic_purity = _typographic_purity(doc.text)

    followers = doc.followers_count or 0
    following = doc.following_count or 0
    verified_type = doc.verified_type or ""

    # X-specific flags -- explicitly gated on source_type (not merely
    # inferred from the account fields being absent) as a belt-and-suspenders
    # guard: a future author-snapshot source for reddit must not silently
    # start tripping "X account" indicators.
    account_age_days: Optional[float] = None
    x_new_account_flag = False
    x_low_followers_flag = False
    x_foreign_origin_flag = False
    follow_ratio_anomaly = False

    if doc.source_type == "x_post":
        if doc.account_created_at is not None:
            account_age_days = (
                datetime.now(timezone.utc) - doc.account_created_at
            ).total_seconds() / 86400
            x_new_account_flag = account_age_days < X_NEW_ACCOUNT_AGE_DAYS
        x_low_followers_flag = followers < X_LOW_FOLLOWERS_THRESHOLD
        if doc.place_country_code:
            x_foreign_origin_flag = doc.place_country_code.upper() != "US"
        if (
            following > FOLLOW_RATIO_ANOMALY_MIN_FOLLOWING
            and followers < FOLLOW_RATIO_ANOMALY_MAX_FOLLOWER_SHARE * following
        ):
            follow_ratio_anomaly = True

    signals: Dict[str, Any] = {
        "spam_keyword_hits": len(spam_found),
        "spam_keywords_found": spam_found,
        "repetition_score": round(repetition_score, 3),
        "unique_ratio": round(unique_ratio, 3),
        "url_count": url_count,
        "hashtag_count": hashtag_count,
        "word_count": word_count,
        "sentence_length_variance": round(sentence_length_variance, 3),
        "hedge_phrase_hits": hedge_hits,
        "hedge_phrase_rate": round(hedge_rate, 3),
        "typographic_purity_score": round(typographic_purity, 3),
        "account_age_days": account_age_days,
        "verified_type": verified_type,
        "followers": followers,
        "following": following,
        "x_new_account_flag": x_new_account_flag,
        "x_low_followers_flag": x_low_followers_flag,
        "x_foreign_origin_flag": x_foreign_origin_flag,
        "follow_ratio_anomaly": follow_ratio_anomaly,
    }
    return signals


def _sentence_length_variance(text: str) -> float:
    """Population variance of per-sentence word counts. 0 when <2 sentences."""
    parts = [s.strip() for s in _SENTENCE_SPLIT_RE.split(text) if s.strip()]
    lengths = [len(s.split()) for s in parts]
    if len(lengths) < 2:
        return 0.0
    return float(statistics.pvariance(lengths))


def _hedge_stats(text_lower: str, word_count: int) -> tuple[int, float]:
    """Return (hits, rate-per-100-words) for LLM hedge phrases."""
    hits = sum(1 for phrase in LLM_HEDGE_PHRASES if phrase in text_lower)
    rate = (hits * 100 / word_count) if word_count > 0 else 0.0
    return hits, rate


def _typographic_purity(text: str) -> float:
    """Fraction of LLM_TYPOGRAPHIC_TELLS present in the text."""
    if not text:
        return 0.0
    hits = sum(1 for ch in LLM_TYPOGRAPHIC_TELLS if ch in text)
    return hits / len(LLM_TYPOGRAPHIC_TELLS)


# =============================================================================
# LLM path
# =============================================================================

# Noise patterns for post-hoc indicator sanitization -- ported verbatim from
# old bot.py's _NOISE_INDICATOR_PATTERNS/_sanitize_llm_indicators (canonical
# fix for todos/bot-propaganda-entity-signals.md "Sanitize whyFlagged at
# source"; the UI's own sanitizeWhyFlagged predates and is superseded here).
_NOISE_INDICATOR_PATTERNS: tuple[re.Pattern, ...] = (
    re.compile(r"=\s*None\b", re.IGNORECASE),
    re.compile(r"=\s*null\b", re.IGNORECASE),
    re.compile(r"=\s*undefined\b", re.IGNORECASE),
    re.compile(r"=\s*0(\s|$)"),
    re.compile(r"=\s*$"),
)


def _sanitize_llm_indicators(raw: Any) -> List[str]:
    if not isinstance(raw, list):
        return []
    out: List[str] = []
    for item in raw:
        text = str(item).strip() if item is not None else ""
        if not text:
            continue
        if any(p.search(text) for p in _NOISE_INDICATOR_PATTERNS):
            continue
        out.append(text)
    return out


def _safe_prompt_value(value: Any) -> str:
    """Never surface a literal None/empty value in the prompt -- the model
    would otherwise echo "field=None"-style noise back into indicators.
    Ported verbatim from old bot.py's _safe_prompt_value."""
    if value is None:
        return "unknown"
    text = str(value).strip()
    if not text or text.lower() in {"none", "null", "undefined", "n/a"}:
        return "unknown"
    return text


def _llm_analysis(text: str, signals: Dict[str, Any], client: LLMClient) -> BotAnalysis:
    """LLM classification given the already-computed deterministic signals
    as prompt context. inference_method='hybrid': both the signal battery
    and the LLM's own judgment contributed to `label`/`confidence`.

    posting_frequency/listed_count are hardcoded "unknown" -- BOT_USER_PROMPT_
    TEMPLATE (llm/prompts.py, not owned by this module) still has placeholders
    for both, but neither signal has a data source anymore (see BotDocInput's
    docstring); always "unknown" is simpler than routing a value that is
    always absent through _safe_prompt_value.
    """
    user_prompt = BOT_USER_PROMPT_TEMPLATE.format(
        text=text[:BOT_PROMPT_TEXT_MAX_CHARS],
        spam_keyword_hits=signals["spam_keyword_hits"],
        repetition_score=signals["repetition_score"],
        unique_ratio=signals["unique_ratio"],
        url_count=signals["url_count"],
        hashtag_count=signals["hashtag_count"],
        sentence_length_variance=signals["sentence_length_variance"],
        hedge_phrase_hits=signals["hedge_phrase_hits"],
        hedge_phrase_rate=signals["hedge_phrase_rate"],
        typographic_purity_score=signals["typographic_purity_score"],
        account_age_days=_safe_prompt_value(signals.get("account_age_days")),
        posting_frequency="unknown",
        followers=_safe_prompt_value(signals.get("followers")),
        following=_safe_prompt_value(signals.get("following")),
        listed_count="unknown",
        verified_type=_safe_prompt_value(signals.get("verified_type")),
    )

    response = client.complete(
        system_prompt=BOT_SYSTEM_PROMPT, user_prompt=user_prompt, response_schema=BOT_SCHEMA,
    )

    # LLMClient.complete() only auto-normalizes keys named/suffixed
    # "confidence" (see llm/client.py::_coerce_confidence_fields) --
    # llm_text_likelihood needs the same manual pass old bot.py gave it.
    llm_text_likelihood = normalize_confidence(
        response.get("llm_text_likelihood", 0.0), "llm_text_likelihood"
    )
    label = response.get("label", "human")
    if label not in ("human", "bot", "suspicious"):
        label = "human"  # schema already enforces this; defensive only

    return BotAnalysis(
        label=label,
        confidence=response.get("confidence", 0.5),  # already normalized by LLMClient
        # Owner decision 2026-07-25: the hand-tuned additive _aggregate_score
        # is gone -- a threshold formula must not produce a bot judgment. The
        # deterministic stylometrics only feed the prompt and their typed
        # columns; llm_text_likelihood is the model's own text-style signal,
        # kept distinct from `label` (the account-level exclusion gate reads
        # label-derived counts, never this field -- see
        # refresh_author_bot_scores() below).
        llm_text_likelihood=llm_text_likelihood,
        burstiness=signals["sentence_length_variance"],
        type_token_ratio=signals["unique_ratio"],
        template_score=signals["hedge_phrase_rate"],
        indicators=_sanitize_llm_indicators(response.get("indicators", [])),
        reasoning=response.get("reasoning"),
        inference_method="hybrid",
        raw_response={"llm": response, "deterministic_signals": signals},
    )


# =============================================================================
# process() -- analyze() + results/store.py
# =============================================================================


def _resolve_model_id() -> str:
    """Mirrors text.py's/targets.py's per-engine model_id resolution."""
    settings = get_settings()
    if settings.llm_backend.lower() == "ollama":
        return settings.ollama_model
    return settings.gemini_model


def process(doc: BotDocInput, client: LLMClient) -> store.RunOutcome:
    """Analyze `doc` and persist its bot signal under one analysis.runs
    ('bot') row. Returns the RunOutcome from store.RunHandle.finish().

    An LLM failure or unavailable backend raises out of analyze(); this is
    recorded as a failed run with the error, identical to text.py's
    contract -- the queue's retry/attempts mechanism owns recovery, not a
    heuristic degrade.
    """
    store.register_prompt_version(BOT_PROMPT_VERSION, BOT_TASK, BOT_SYSTEM_PROMPT, BOT_USER_PROMPT_TEMPLATE)
    model_id = _resolve_model_id()

    try:
        result = analyze(doc, client)
    except Exception as exc:
        logger.warning(f"bot engine failed for doc={doc.doc_id}: {exc}")
        handle = store.open_run(
            BOT_TASK, doc_id=doc.doc_id, model_id=model_id,
            prompt_version=BOT_PROMPT_VERSION, inference_method="hybrid",
        )
        return handle.finish("failed", error=str(exc))

    prompt_version = BOT_PROMPT_VERSION if result.inference_method == "hybrid" else None
    handle = store.open_run(
        BOT_TASK, doc_id=doc.doc_id, model_id=model_id,
        prompt_version=prompt_version, inference_method=result.inference_method,
    )
    handle.save_bot_signals(store.BotSignalsRow(
        label=result.label,
        llm_text_likelihood=result.llm_text_likelihood,
        burstiness=result.burstiness,
        type_token_ratio=result.type_token_ratio,
        template_score=result.template_score,
    ))
    return handle.finish("done", confidence=result.confidence, raw_response=result.raw_response)


# =============================================================================
# Author rollup -- a plain SQL aggregate, not a per-run write
# =============================================================================

# analysis.author_bot_scores is owned by THIS module, not results/store.py:
# it is a materialized rollup recomputed wholesale from current bot_signals
# rows, never written incrementally per-run.

_DELETE_STALE_AUTHOR_SCORES_SQL = """
    DELETE FROM analysis.author_bot_scores
    WHERE author_id NOT IN (
        SELECT d.author_id
        FROM analysis.bot_signals bs
        JOIN analysis.runs r ON r.run_id = bs.run_id
        JOIN corpus.documents d ON d.doc_id = bs.doc_id
        WHERE r.is_current AND d.author_id IS NOT NULL
          AND bs.label != 'unknown'::analysis.bot_label
    )
"""

_UPSERT_AUTHOR_BOT_SCORES_SQL = """
    INSERT INTO analysis.author_bot_scores
        (author_id, sample_count, bot_post_count,
         suspicious_post_count, llm_text_likelihood_mean, updated_at)
    SELECT
        d.author_id,
        COUNT(*),
        COUNT(*) FILTER (WHERE bs.label = 'bot' AND r.confidence >= %(min_conf)s),
        COUNT(*) FILTER (WHERE bs.label = 'suspicious' AND r.confidence >= %(min_conf)s),
        AVG(bs.llm_text_likelihood),
        now()
    FROM analysis.bot_signals bs
    JOIN analysis.runs r ON r.run_id = bs.run_id
    JOIN corpus.documents d ON d.doc_id = bs.doc_id
    WHERE r.is_current AND d.author_id IS NOT NULL
      AND bs.label != 'unknown'::analysis.bot_label
    GROUP BY d.author_id
    ON CONFLICT (author_id) DO UPDATE SET
        sample_count = EXCLUDED.sample_count,
        bot_post_count = EXCLUDED.bot_post_count,
        suspicious_post_count = EXCLUDED.suspicious_post_count,
        llm_text_likelihood_mean = EXCLUDED.llm_text_likelihood_mean,
        updated_at = EXCLUDED.updated_at
"""


def refresh_author_bot_scores() -> int:
    """Full recompute of analysis.author_bot_scores from current bot runs,
    replacing old job_runner.py's run_account_bot_rollup.

    Joins bot_signals -> runs (is_current) -> documents (author_id): a
    reprocess that supersedes a prior run drops that run's is_current, so
    its bot_signals row is automatically excluded here without any special
    casing (old code's equivalent was a raw ai_outputs_latest view). Rows
    with bs.label = 'unknown' (the empty-text case) are excluded from both
    the DELETE and the UPSERT -- they carry no signal to aggregate, the same
    exclusion the pre-0005 `bs.score IS NOT NULL` guard achieved (`score` was
    NULL for, and only for, the 'unknown' label).

    `bot_post_count`/`suspicious_post_count` additionally require
    `r.confidence >= BOT_LABEL_MIN_CONFIDENCE` -- a low-confidence 'bot'
    guess must not silence an author from the downstream flagged-share
    exclusion gate (analysis/src/api/queries/constants.py's
    BOT_FLAGGED_SHARE_EXCLUSION). `sample_count` itself is NOT
    confidence-floored: it is the denominator of "how many analyzed posts",
    which is true regardless of how confident any one label was.

    Old code hardcoded platform='x' (the account_bot_scores table only ever
    held X authors, "reddit stays at per-post scoring" per its own comment).
    This version has no such filter: corpus.authors is unified across
    platforms, and since etl/authors.py::sync_reddit_authors is a no-op
    today, every reddit_post doc's author_id is NULL and is naturally
    excluded by the `d.author_id IS NOT NULL` filter -- the old platform
    restriction falls out of the data rather than needing to be coded, and
    this query needs no change whenever reddit author sync is implemented.

    Authors no longer represented (e.g. every one of their runs got
    superseded by a reprocess that came back 'unknown') are deleted first,
    so this is always a full, correct recompute rather than a stale merge.

    Returns the number of author_bot_scores rows inserted or updated.
    """
    params = {"min_conf": BOT_LABEL_MIN_CONFIDENCE}
    with db.connection() as conn:
        with conn.cursor() as cur:
            cur.execute(_DELETE_STALE_AUTHOR_SCORES_SQL)
            cur.execute(_UPSERT_AUTHOR_BOT_SCORES_SQL, params)
            return cur.rowcount
