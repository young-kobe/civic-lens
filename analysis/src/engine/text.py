"""
Unified text engine (Postgres redesign Phase 6): one LLM call producing
sentiment. `analyze()` is pure (no DB access); `process()` composes it with
`results/store.py` to write one `analysis.runs` ('text') row plus its
`sentiment_results` row.

Deliberate behavior change from the old `engine/analyzer.py`: there is no
heuristic GOP-keyword-proximity fallback. A failed or unavailable LLM call
is recorded as a failed run (honest, re-queueable) rather than silently
substituting a lower-quality deterministic guess.

Owner decision (2026-07-23): the trivial-content short-circuit (prompt rule
5 -- mentions/links/hashtags only) is a `done` deterministic run with NO
`sentiment_results` row, replacing the old ported neutral-at-0.5 placeholder
(`TRIVIAL_CONTENT_CONFIDENCE` deleted) -- unanalyzable is not neutral.

Sentiment-only: per-entity stance is covered by the party-neutral,
topic-tagged `targets` engine instead, so this module computes no favorability.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List, Optional

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
class TextAnalysis:
    """The validated outcome of one `analyze()` call.

    `sentiment` is None for the trivial-content short-circuit (owner
    decision 2026-07-23): unanalyzable content gets no sentiment_results
    row at all, not a placeholder neutral/low-confidence guess -- process()
    below skips save_sentiment() and finishes the run with zero result
    rows."""

    sentiment: Optional[SentimentOutcome]
    inference_method: str  # 'llm' | 'deterministic'
    raw_response: Optional[Dict]  # verbatim LLM payload; None for the trivial short-circuit


def analyze(doc: TextDocInput, client: LLMClient) -> TextAnalysis:
    """Sentiment for one doc. No DB access. Raises whatever
    `client.complete()` raises (RuntimeError, after retries are exhausted)
    when the LLM call fails or the backend is unavailable -- `process()`
    below is what turns that into a recorded failed run."""
    if is_trivial_content(doc.text):
        return TextAnalysis(
            sentiment=None,
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

    return TextAnalysis(
        sentiment=sentiment,
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


def _resolve_model_id() -> str:
    """Resolves model_id from settings.llm_backend (gemini_model vs
    ollama_model). A per-engine stopgap until Phase 7 generalizes model_id
    resolution into the scheduler."""
    settings = get_settings()
    if settings.llm_backend.lower() == "ollama":
        return settings.ollama_model
    return settings.gemini_model


def process(doc: TextDocInput, client: LLMClient) -> store.RunOutcome:
    """Analyze `doc` and persist sentiment under one analysis.runs('text')
    row. Returns the RunOutcome from store.RunHandle.finish()."""
    store.register_prompt_version(
        TEXT_ANALYSIS_PROMPT_VERSION, TEXT_TASK,
        TEXT_ANALYSIS_SYSTEM_PROMPT, TEXT_ANALYSIS_USER_PROMPT_TEMPLATE,
    )
    model_id = _resolve_model_id()

    try:
        result = analyze(doc, client)
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
        # unanalyzable is not neutral -- no sentiment_results row, run
        # confidence None (nothing was measured). The run itself still
        # lands 'done': the doc was correctly handled, just not analyzed.
        return handle.finish("done", confidence=None, raw_response=None)

    handle.save_sentiment(store.SentimentRow(
        label=result.sentiment.label,
        score=result.sentiment.confidence,
        sarcasm_detected=result.sentiment.sarcasm_detected,
        evidence_spans=result.sentiment.evidence_spans,
    ))

    return handle.finish("done", confidence=result.sentiment.confidence, raw_response=result.raw_response)
