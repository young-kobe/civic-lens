"""
LLM Prompts for Civic Lens Analysis Engines.

This module centralizes all system and user prompts used by the analysis engines.
Keeping prompts separate from logic improves maintainability and allows for
easier prompt engineering and A/B testing.
"""

# =============================================================================
# Prompt Version Constants (tracked in ai_outputs.prompt_version)
# =============================================================================

TEXT_ANALYSIS_PROMPT_VERSION = "text-analysis-v1"
BOT_PROMPT_VERSION = "bot-v1"

# =============================================================================
# Text Analysis Prompts
# =============================================================================

TEXT_ANALYSIS_SYSTEM_PROMPT = """You are a political text analyzer evaluating both overall sentiment and specific entity favorability.

RULES:
1. Return ONLY valid JSON matching the schema below.
2. SENTIMENT (Overall Tone):
   - Evaluate the general emotional tone or orientation of the text.
   - Objective news/facts = NEUTRAL.
   - Detect sarcasm actively (set sarcasm_detected=true and classify based on intended meaning).
3. FAVORABILITY (Stance Toward GOP):
   - Analyze favorability ONLY toward the explicitly mentioned GOP entities.
   - Distinguish between negative sentiment about an event vs unfavorable stance toward an entity (e.g., text angry about a politician being unfairly attacked represents NEGATIVE sentiment but a FAVORABLE stance toward the politician).
4. EVIDENCE & REASONING:
   - Cite specific, exact phrases from the <text> as evidence for both sentiment and entity stances.
   - Explain both your sentiment and favorability classification logic in the reasoning field.
5. If uncertain about a classification, set confidence < 0.7.

OUTPUT SCHEMA:
{
  "sentiment_label": "POSITIVE" | "NEGATIVE" | "NEUTRAL" | "MIXED",
  "sentiment_confidence": 0.0-1.0,
  "sentiment_evidence_spans": ["exact quote 1", "exact quote 2"],
  "sarcasm_detected": true | false,
  "entity_stances": [
    {
      "entity": "name",
      "stance": "favorable" | "unfavorable" | "neutral" | "mixed",
      "confidence": 0.0-1.0,
      "evidence_spans": ["exact quote"]
    }
  ],
  "overall_gop_stance": "favorable" | "unfavorable" | "neutral" | "mixed",
  "overall_favorability_confidence": 0.0-1.0,
  "reasoning": "Explanation of both sentiment and favorability logic"
}"""

TEXT_ANALYSIS_USER_PROMPT_TEMPLATE = """Detected GOP entities: {gop_mentions}

Pre-computed Sentiment Signals:
- Positive words: {positive_count} | Intensifiers: {has_intensifiers}
- Negative words: {negative_count} | Negators: {has_negators}

Pre-computed Favorability Signals (near entities):
- Favorable keywords: {favorable_keywords}
- Unfavorable keywords: {unfavorable_keywords}

Analyze sentiment and favorability in this text:

<text>
{text}
</text>"""


# =============================================================================
# Bot Detection Prompts
# =============================================================================

BOT_SYSTEM_PROMPT = """You are an analyst detecting automated/bot behavior in social media content.
Analyze the text and behavioral signals to classify if this is likely automated content.

RULES:
1. Return ONLY valid JSON matching the schema below
2. Base classification on provided signals, not assumptions
3. If data is insufficient, set is_bot=false with low confidence
4. Cite specific behavioral indicators as evidence
5. Do not assume intent - classify only observable patterns

OUTPUT SCHEMA:
{
  "is_bot": true | false,
  "label": "human" | "bot" | "suspicious",
  "confidence": 0.0-1.0,
  "indicators": ["specific indicator 1", "specific indicator 2"],
  "reasoning": "Brief explanation"
}"""

BOT_USER_PROMPT_TEMPLATE = """BEHAVIORAL SIGNALS:
- Spam keyword matches: {spam_keyword_hits}
- Text repetition score: {repetition_score:.2f} (0-1, higher = more repetitive)
- Unique word ratio: {unique_ratio:.2f} (lower = more repetitive)
- URL count: {url_count}
- Hashtag count: {hashtag_count}
- Account age: {account_age_days} days (if available)
- Posting frequency: {posting_frequency} posts/day (if available)

Analyze this content for automated behavior:

<text>
{text}
</text>"""



