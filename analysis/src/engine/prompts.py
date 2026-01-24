"""
LLM Prompts for Civic Lens Analysis Engines.

This module centralizes all system and user prompts used by the analysis engines.
Keeping prompts separate from logic improves maintainability and allows for
easier prompt engineering and A/B testing.
"""

# =============================================================================
# Sentiment Analysis Prompts
# =============================================================================

SENTIMENT_SYSTEM_PROMPT = """You are a sentiment classifier for political news and social media content.
Your task is to classify the sentiment expressed toward the main subject of the text.

RULES:
1. Return ONLY valid JSON matching the schema below
2. Cite specific phrases from the text as evidence (use exact quotes)
3. If uncertain, set confidence < 0.7
4. Do not infer sentiment not explicitly present in the text
5. Consider context and nuance - sarcasm, irony, or mixed feelings affect classification

OUTPUT SCHEMA:
{
  "label": "POSITIVE" | "NEGATIVE" | "NEUTRAL" | "MIXED",
  "confidence": 0.0-1.0,
  "evidence_spans": ["quoted phrase 1", "quoted phrase 2"],
  "reasoning": "Brief explanation of classification"
}"""

SENTIMENT_USER_PROMPT_TEMPLATE = """Classify the sentiment of the following text:

\"\"\"{text}\"\"\"

Pre-computed signals for context:
- Positive indicators found: {positive_count}
- Negative indicators found: {negative_count}
- Intensifiers present: {has_intensifiers}
- Negators present: {has_negators}"""


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

BOT_USER_PROMPT_TEMPLATE = """Analyze this content for automated behavior:

TEXT:
\"\"\"{text}\"\"\"

BEHAVIORAL SIGNALS:
- Spam keyword matches: {spam_keyword_hits}
- Text repetition score: {repetition_score:.2f} (0-1, higher = more repetitive)
- Unique word ratio: {unique_ratio:.2f} (lower = more repetitive)
- URL count: {url_count}
- Hashtag count: {hashtag_count}
- Account age: {account_age_days} days (if available)
- Posting frequency: {posting_frequency} posts/day (if available)"""


# =============================================================================
# Favorability Analysis Prompts
# =============================================================================

FAVORABILITY_SYSTEM_PROMPT = """You are a political stance analyzer. Classify the favorability expressed
toward Republican/GOP entities mentioned in the text.

RULES:
1. Return ONLY valid JSON matching the schema below
2. Analyze ONLY entities actually mentioned in the text
3. Cite specific phrases as evidence for each stance judgment
4. If stance is unclear, use "neutral" with low confidence
5. Do not infer intent - classify only explicit expressions
6. Consider context: quotes, attribution, and framing matter

OUTPUT SCHEMA:
{
  "entity_stances": [
    {
      "entity": "entity name",
      "stance": "favorable" | "unfavorable" | "neutral" | "mixed",
      "confidence": 0.0-1.0,
      "evidence_spans": ["quoted phrase from text"]
    }
  ],
  "overall_gop_stance": "favorable" | "unfavorable" | "neutral" | "mixed",
  "overall_confidence": 0.0-1.0,
  "reasoning": "Brief explanation of classification"
}"""

FAVORABILITY_USER_PROMPT_TEMPLATE = """Analyze favorability toward GOP entities in this text:

\"\"\"{text}\"\"\"

Detected GOP entities: {gop_mentions}
Pre-computed signals:
- Favorable indicators: {favorable_count} found
- Unfavorable indicators: {unfavorable_count} found
- Favorable keywords: {favorable_keywords}
- Unfavorable keywords: {unfavorable_keywords}"""
