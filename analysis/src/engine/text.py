"""
Unified text engine (Postgres redesign Phase 6): one LLM call producing
sentiment + favorability. `analyze()` is pure (no DB access -- entity
resolution is a lookup against an already-loaded `EntityResolver`, not an
I/O call); `process()` composes it with `results/store.py` to write one
`analysis.runs` ('text') row plus its `sentiment_results` and
`favorability_stances` rows.

Deliberate behavior change from the old `engine/analyzer.py`: there is no
heuristic GOP-keyword-proximity fallback. A failed or unavailable LLM call
is recorded as a failed run (honest, re-queueable) rather than silently
substituting a lower-quality deterministic guess.

Owner decision (2026-07-23): the trivial-content short-circuit (prompt rule
6 -- mentions/links/hashtags only) is a `done` deterministic run with NO
`sentiment_results` row, replacing the old ported neutral-at-0.5 placeholder
(`TRIVIAL_CONTENT_CONFIDENCE` deleted) -- unanalyzable is not neutral.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List, Optional

from analysis.src.common.entity_resolver import EntityResolver
from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings
from analysis.src.engine import validation
from analysis.src.engine.constants import TEXT_ANALYSIS_MAX_CHARS
from analysis.src.engine.text_prep import is_trivial_content, truncate_at_sentence
from analysis.src.llm.client import LLMClient
from analysis.src.llm.context_seeds import format_seeds_block, match_seeds
from analysis.src.llm.prompts import (
    TEXT_ANALYSIS_PROMPT_VERSION,
    TEXT_ANALYSIS_SYSTEM_PROMPT,
    TEXT_ANALYSIS_USER_PROMPT_TEMPLATE,
)
from analysis.src.llm.schemas import TEXT_ANALYSIS_SCHEMA
from analysis.src.results import store

logger = get_logger(__name__)

TEXT_TASK = "text"

# The LLM emits uppercase sentiment labels (prompt schema); analysis.sentiment_label
# is lowercase. Unmapped/unexpected labels fall back to "neutral".
_SENTIMENT_LABEL_MAP = {
    "POSITIVE": "positive", "NEGATIVE": "negative", "NEUTRAL": "neutral", "MIXED": "mixed",
}

@dataclass(frozen=True)
class TextDocInput:
    """One doc's text-engine input, assembled by the caller from
    corpus.documents. `title` is informational only -- the current prompt
    template interpolates `text` alone."""

    doc_id: int
    source_type: str
    title: Optional[str]
    text: str


@dataclass(frozen=True)
class SentimentOutcome:
    label: str  # analysis.sentiment_label: positive|negative|neutral|mixed
    confidence: float
    evidence_spans: List[str]
    sarcasm_detected: bool
    reasoning: Optional[str]


@dataclass(frozen=True)
class FavorabilityStanceOutcome:
    entity_id: int
    raw_entity_name: str
    stance: str  # analysis.favorability_label: favorable|unfavorable|neutral|mixed
    confidence: float
    evidence_spans: List[str]


@dataclass(frozen=True)
class TextAnalysis:
    """The validated outcome of one `analyze()` call. `favorability_stances`
    holds only entities EntityResolver could resolve; `dropped_unresolved`
    counts the rest (favorability_stances.entity_id is NOT NULL, so an
    unresolved stance cannot be stored -- unlike target_mentions in the
    targets engine, which keeps raw_target strings because that table
    allows entity_id NULL).

    `sentiment` is None for the trivial-content short-circuit (owner
    decision 2026-07-23): unanalyzable content gets no sentiment_results
    row at all, not a placeholder neutral/low-confidence guess -- process()
    below skips save_sentiment() and finishes the run with zero result
    rows."""

    sentiment: Optional[SentimentOutcome]
    favorability_stances: List[FavorabilityStanceOutcome]
    dropped_unresolved: int
    inference_method: str  # 'llm' | 'deterministic'
    raw_response: Optional[Dict]  # verbatim LLM payload; None for the trivial short-circuit


def analyze(doc: TextDocInput, client: LLMClient, resolver: EntityResolver) -> TextAnalysis:
    """Sentiment + favorability for one doc. No DB access: `resolver` is an
    already-loaded in-memory lookup, not a live query. Raises whatever
    `client.complete()` raises (RuntimeError, after retries are exhausted)
    when the LLM call fails or the backend is unavailable -- `process()`
    below is what turns that into a recorded failed run."""
    if is_trivial_content(doc.text):
        return TextAnalysis(
            sentiment=None,
            favorability_stances=[],
            dropped_unresolved=0,
            inference_method="deterministic",
            raw_response=None,
        )

    seeds_block = format_seeds_block(match_seeds(doc.text))
    user_prompt = seeds_block + TEXT_ANALYSIS_USER_PROMPT_TEMPLATE.format(
        text=truncate_at_sentence(doc.text, TEXT_ANALYSIS_MAX_CHARS)
    )
    response = client.complete(
        system_prompt=TEXT_ANALYSIS_SYSTEM_PROMPT,
        user_prompt=user_prompt,
        response_schema=TEXT_ANALYSIS_SCHEMA,
    )

    sentiment = _build_sentiment(response, doc.text)
    favorability_stances, dropped_unresolved = _build_favorability_stances(response, doc.text, resolver)
    if dropped_unresolved:
        logger.info(
            f"text engine doc={doc.doc_id}: dropped {dropped_unresolved} "
            "favorability stance(s) toward an unresolved entity"
        )

    return TextAnalysis(
        sentiment=sentiment,
        favorability_stances=favorability_stances,
        dropped_unresolved=dropped_unresolved,
        inference_method="llm",
        raw_response=response,
    )


def _build_sentiment(response: dict, source_text: str) -> SentimentOutcome:
    label = _SENTIMENT_LABEL_MAP.get(response.get("sentiment_label"), "neutral")
    spans, had_invalid = validation.validate_spans(
        response.get("sentiment_evidence_spans", []) or [], source_text
    )
    confidence = validation.cap_confidence_if_unverified(
        response.get("sentiment_confidence", 0.5), verified=not had_invalid
    )
    return SentimentOutcome(
        label=label,
        confidence=confidence,
        evidence_spans=spans,
        sarcasm_detected=bool(response.get("sarcasm_detected", False)),
        reasoning=response.get("sentiment_reasoning"),
    )


def _build_favorability_stances(
    response: dict, source_text: str, resolver: EntityResolver,
) -> tuple[List[FavorabilityStanceOutcome], int]:
    stances: List[FavorabilityStanceOutcome] = []
    dropped_unresolved = 0
    for raw_stance in response.get("entity_stances", []) or []:
        if not isinstance(raw_stance, dict):
            continue
        raw_entity_name = raw_stance.get("entity", "")
        entity_id = resolver.resolve(raw_entity_name)
        if entity_id is None:
            dropped_unresolved += 1
            continue
        spans, had_invalid = validation.validate_spans(
            raw_stance.get("evidence_spans", []) or [], source_text
        )
        confidence = validation.cap_confidence_if_unverified(
            raw_stance.get("confidence", 0.5), verified=not had_invalid
        )
        stances.append(FavorabilityStanceOutcome(
            entity_id=entity_id,
            raw_entity_name=raw_entity_name,
            stance=raw_stance.get("stance", "neutral"),
            confidence=confidence,
            evidence_spans=spans,
        ))
    return stances, dropped_unresolved


def _resolve_model_id() -> str:
    """Mirrors the old job_runner's model_id resolution (settings.llm_backend
    selects gemini_model vs ollama_model). A per-engine stopgap until Phase 7
    generalizes model_id resolution into the scheduler."""
    settings = get_settings()
    if settings.llm_backend.lower() == "ollama":
        return settings.ollama_model
    return settings.gemini_model


def process(doc: TextDocInput, client: LLMClient, resolver: EntityResolver) -> store.RunOutcome:
    """Analyze `doc` and persist sentiment + favorability under one
    analysis.runs('text') row. Returns the RunOutcome from
    store.RunHandle.finish()."""
    store.register_prompt_version(
        TEXT_ANALYSIS_PROMPT_VERSION, TEXT_TASK,
        TEXT_ANALYSIS_SYSTEM_PROMPT, TEXT_ANALYSIS_USER_PROMPT_TEMPLATE,
    )
    model_id = _resolve_model_id()

    try:
        result = analyze(doc, client, resolver)
    except Exception as exc:
        logger.warning(f"text engine failed for doc={doc.doc_id}: {exc}")
        handle = store.open_run(
            TEXT_TASK, doc_id=doc.doc_id, model_id=model_id,
            prompt_version=TEXT_ANALYSIS_PROMPT_VERSION, inference_method="llm",
        )
        return handle.finish("failed", error=str(exc))

    prompt_version = TEXT_ANALYSIS_PROMPT_VERSION if result.inference_method == "llm" else None
    handle = store.open_run(
        TEXT_TASK, doc_id=doc.doc_id, model_id=model_id,
        prompt_version=prompt_version, inference_method=result.inference_method,
    )

    if result.sentiment is None:
        # Trivial-content short-circuit (owner decision 2026-07-23):
        # unanalyzable is not neutral -- no sentiment_results row, no
        # favorability_stances rows, run confidence None (nothing was
        # measured to average). The run itself still lands 'done': the doc
        # was correctly handled, just not analyzed.
        return handle.finish("done", confidence=None, raw_response=None)

    handle.save_sentiment(store.SentimentRow(
        label=result.sentiment.label,
        score=result.sentiment.confidence,
        sarcasm_detected=result.sentiment.sarcasm_detected,
        evidence_spans=result.sentiment.evidence_spans,
    ))
    handle.save_favorability_stances([
        store.FavorabilityStanceRow(
            entity_id=stance.entity_id,
            stance=stance.stance,
            score=stance.confidence,
            evidence_spans=stance.evidence_spans,
        )
        for stance in result.favorability_stances
    ])

    # Run-level confidence: mean of every result row's own confidence -- the
    # old job_runner stored sentiment/favorability as separate ai_outputs rows
    # each with its own confidence; this run row unifies both under one value.
    confidences = [result.sentiment.confidence] + [s.confidence for s in result.favorability_stances]
    run_confidence = sum(confidences) / len(confidences)

    return handle.finish("done", confidence=run_confidence, raw_response=result.raw_response)
