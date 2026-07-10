"""
Target-sentiment extractor for Civic Lens.

LLM-driven extraction of stance targets: which political entities a doc
takes an evaluative position toward, with a per-target stance, topic, and
verbatim evidence. This is the input for the received-tone / expressed-tone
split — overall sentiment measures how a text sounds; target stances measure
who it is about.

Target names are stored as the model emitted them (raw). Resolution against
the entity registry is deterministic and happens at aggregation time
(reporting/entity_registry.py::TargetResolver), so registry edits
retroactively resolve stored rows without re-running the LLM.
"""

from dataclasses import dataclass, asdict, field
from typing import List, Optional, Sequence

from analysis.src.common.logger import get_logger
from analysis.src.engine.text_prep import is_trivial_content, truncate_at_sentence
from analysis.src.llm.prompts import (
    TARGET_SENTIMENT_SYSTEM_PROMPT,
    TARGET_SENTIMENT_USER_PROMPT_TEMPLATE,
)
from analysis.src.llm.schemas import TARGET_SENTIMENT_SCHEMA, TARGET_TOPIC_ENUM

logger = get_logger(__name__)

# Evidence-span word-count floor — matches the sentiment analyzer's rule.
MIN_EVIDENCE_WORDS = 4
# Confidence ceiling when a target arrived with evidence but none verified
# verbatim (same invariant-B2 pattern as sentiment / propaganda).
UNVERIFIED_EVIDENCE_CONFIDENCE_CAP = 0.3
# Schema instructs at most 4 targets; enforce defensively.
MAX_TARGETS = 4
# Character budget for the text the LLM sees, clamped at a sentence boundary.
TARGET_TEXT_MAX_CHARS = 2000

VALID_STANCES = frozenset({"positive", "negative", "neutral", "mixed"})


@dataclass
class TargetStance:
    """Stance toward one target extracted from a doc."""
    target: str          # raw name as the model emitted it
    topic: str           # one of TARGET_TOPIC_ENUM
    stance: str          # positive | negative | neutral | mixed
    confidence: float
    evidence_spans: List[str] = field(default_factory=list)

    def to_dict(self) -> dict:
        return asdict(self)


@dataclass
class TargetSentimentResult:
    targets: List[TargetStance]
    reasoning: Optional[str] = None
    # True when the LLM call itself failed (transport error / unavailable
    # client) rather than legitimately finding no targets. job_runner MUST
    # skip persisting a failed result so the doc has no ai_outputs row and
    # is re-queued next run (same contract as claims — audit A-3).
    extraction_failed: bool = False

    def to_dict(self) -> dict:
        return {
            "targets": [t.to_dict() for t in self.targets],
            "reasoning": self.reasoning,
        }


def _validate_target(raw: dict, source_text: str) -> Optional[TargetStance]:
    """Return a validated TargetStance, or None when the record is unusable.

    Unverifiable evidence does not drop the target — the stance may still be
    correct — but caps its confidence at UNVERIFIED_EVIDENCE_CONFIDENCE_CAP so
    the aggregation confidence floor excludes it from public numbers while the
    row remains auditable.
    """
    if not isinstance(raw, dict):
        return None
    name = (raw.get("target") or "").strip()
    stance = (raw.get("stance") or "").strip().lower()
    if not name or stance not in VALID_STANCES:
        return None
    try:
        confidence = float(raw.get("confidence", 0.5))
    except (TypeError, ValueError):
        return None
    confidence = max(0.0, min(1.0, confidence))

    topic = (raw.get("topic") or "").strip()
    if topic not in TARGET_TOPIC_ENUM:
        topic = "Other"

    source_lower = source_text.lower()
    valid_spans: List[str] = []
    had_any_span = False
    for span in raw.get("evidence_spans") or []:
        if not isinstance(span, str) or not span.strip():
            continue
        had_any_span = True
        cleaned = span.strip()
        if len(cleaned.split()) < MIN_EVIDENCE_WORDS:
            continue
        if cleaned.lower() not in source_lower:
            continue  # fabricated / paraphrased evidence
        if cleaned in valid_spans:
            continue
        valid_spans.append(cleaned)
    if not valid_spans and had_any_span:
        confidence = min(confidence, UNVERIFIED_EVIDENCE_CONFIDENCE_CAP)

    return TargetStance(
        target=name,
        topic=topic,
        stance=stance,
        confidence=confidence,
        evidence_spans=valid_spans,
    )


def build_tracked_targets_block(tracked_targets: Sequence[str]) -> str:
    """Format the TRACKED TARGETS block prepended above the input text.

    Empty list → empty string (no block, no token cost) — same shape as the
    reference-context seeds block in llm/context_seeds.py.
    """
    if not tracked_targets:
        return ""
    lines = "\n".join(f"- {name}" for name in tracked_targets)
    return (
        "TRACKED TARGETS (use these exact names when the target matches):\n"
        f"{lines}\n\n"
    )


class TargetSentimentExtractor:
    """Extract per-target stances from doc text via LLM."""

    def __init__(
        self,
        llm_enabled: bool = False,
        llm_client=None,
        tracked_targets: Optional[Sequence[str]] = None,
    ):
        # llm_client lets tests supply a fake BaseLLMClient directly.
        # tracked_targets is the canonical-name list injected by the caller
        # (job_runner builds it from the entity registry) so this engine
        # module doesn't import the reporting layer.
        self.llm_enabled = llm_enabled or llm_client is not None
        self._llm_client = llm_client
        self._targets_block = build_tracked_targets_block(tracked_targets or [])
        logger.info(
            f"Initialized TargetSentimentExtractor (llm_enabled={self.llm_enabled}, "
            f"tracked_targets={len(tracked_targets or [])})"
        )
        if self.llm_enabled and self._llm_client is None:
            from analysis.src.llm import get_llm_client
            self._llm_client = get_llm_client()
            if not self._llm_client.is_available:
                raise RuntimeError("LLM client not available for TargetSentimentExtractor")

    def extract(self, text: str) -> TargetSentimentResult:
        # Empty or trivial text (@-mentions / links / hashtags only) is a
        # genuine "no targets", answered without spending the call.
        if not text or is_trivial_content(text):
            return TargetSentimentResult(
                targets=[],
                reasoning="Trivial content (mentions/links/hashtags only); LLM call skipped.",
            )
        # A missing / unavailable client is a failure, not a clean empty —
        # the doc was never analyzed. Flag it so job_runner re-queues.
        if not self.llm_enabled or self._llm_client is None or not self._llm_client.is_available:
            return TargetSentimentResult(targets=[], extraction_failed=True)

        user_prompt = self._targets_block + TARGET_SENTIMENT_USER_PROMPT_TEMPLATE.format(
            text=truncate_at_sentence(text, TARGET_TEXT_MAX_CHARS)
        )

        try:
            response = self._llm_client.complete(
                system_prompt=TARGET_SENTIMENT_SYSTEM_PROMPT,
                user_prompt=user_prompt,
                response_schema=TARGET_SENTIMENT_SCHEMA,
            )
        except Exception as e:
            logger.warning(f"Target-sentiment LLM call failed: {e}")
            return TargetSentimentResult(targets=[], extraction_failed=True)

        validated: List[TargetStance] = []
        seen_names: set = set()
        for raw in (response.get("targets", []) or [])[:MAX_TARGETS]:
            target = _validate_target(raw, text)
            if target is None:
                continue
            # De-dup by normalized name; keep the first (schema caps at 4,
            # so a duplicate is a model slip, not a real second stance).
            name_key = target.target.lower()
            if name_key in seen_names:
                continue
            seen_names.add(name_key)
            validated.append(target)

        return TargetSentimentResult(
            targets=validated,
            reasoning=response.get("reasoning"),
        )
