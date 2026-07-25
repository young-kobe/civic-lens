"""
JSON Schemas for LLM Structured Output.

These schemas are passed to Ollama (format parameter) and Gemini
(response_schema) to enforce deterministic JSON responses from the LLM.

Note: Gemini's schema validator rejects OpenAPI-spec ``minimum``/``maximum``
keywords. Numeric confidences are documented as being in [0,1] in the
prompts, and the engine code clamps when it reads them — we don't bake
range constraints into the schema here.
"""

# Text Analysis Schema (Sentiment only as of 2026-07-25 -- per-entity stance
# is TARGET_SENTIMENT_SCHEMA below; see
# docs/audit-trail/analysis/2026-07-25-text-sentiment-only.md)
TEXT_ANALYSIS_SCHEMA = {
    "type": "object",
    "properties": {
        "sentiment_label": {
            "type": "string",
            "enum": ["POSITIVE", "NEGATIVE", "NEUTRAL", "MIXED"]
        },
        "sentiment_confidence": {
            "type": "number"
        },
        "sentiment_evidence_spans": {
            "type": "array",
            "items": {"type": "string"}
        },
        "sarcasm_detected": {
            "type": "boolean"
        },
        "sentiment_reasoning": {
            "type": "string"
        }
    },
    "required": [
        "sentiment_label", "sentiment_confidence", "sentiment_evidence_spans",
        "sarcasm_detected", "sentiment_reasoning"
    ]
}

# Target Sentiment Schema (received vs. expressed tone split)
#
# Topic values mirror reporting/aggregators/constants.py::TOPIC_KEYWORDS keys
# plus "Other". Kept as a literal here (rather than importing from the
# reporting layer) so the llm package stays below reporting in the layering;
# test_target_extractor asserts the two stay in sync.
TARGET_TOPIC_ENUM = [
    "Immigration", "Economy", "Healthcare", "Climate", "Foreign Policy",
    "Gun Policy", "Abortion", "Education", "Justice", "Technology",
    "Social Issues", "Democracy", "Housing", "National Security", "Other",
]

TARGET_STANCE_SCHEMA = {
    "type": "object",
    "properties": {
        "target": {"type": "string"},
        "topic": {"type": "string", "enum": TARGET_TOPIC_ENUM},
        "stance": {
            "type": "string",
            "enum": ["positive", "negative", "neutral", "mixed"],
        },
        "confidence": {"type": "number"},
        "evidence_spans": {
            "type": "array",
            "items": {"type": "string"},
        },
    },
    "required": ["target", "topic", "stance", "confidence"],
}

TARGET_SENTIMENT_SCHEMA = {
    "type": "object",
    "properties": {
        "targets": {
            "type": "array",
            "items": TARGET_STANCE_SCHEMA,
        },
        "reasoning": {"type": "string"},
    },
    "required": ["targets"],
}

# Claim Extraction Schema
CLAIM_SCHEMA = {
    "type": "object",
    "properties": {
        "claim": {"type": "string"},
        "confidence": {"type": "number"},
        "evidence_span": {"type": "string"},
    },
    "required": ["claim", "confidence", "evidence_span"],
}

CLAIM_EXTRACTION_SCHEMA = {
    "type": "object",
    "properties": {
        "claims": {
            "type": "array",
            "items": CLAIM_SCHEMA,
        },
    },
    "required": ["claims"],
}

# Propaganda Detection Schema (walkthrough 042)
PROPAGANDA_TECHNIQUE_ENUM = [
    "loaded_language",
    "name_calling",
    "ad_hominem",
    "appeal_to_fear",
    "whataboutism",
    "doubt_casting",
]

PROPAGANDA_TECHNIQUE_SCHEMA = {
    "type": "object",
    "properties": {
        "technique": {
            "type": "string",
            "enum": PROPAGANDA_TECHNIQUE_ENUM,
        },
        "confidence": {"type": "number"},
        "evidence_span": {"type": "string"},
    },
    "required": ["technique", "confidence", "evidence_span"],
}

PROPAGANDA_SCHEMA = {
    "type": "object",
    "properties": {
        "techniques": {
            "type": "array",
            "items": PROPAGANDA_TECHNIQUE_SCHEMA,
        },
        "overall_propaganda_score": {
            "type": "number",
        },
        "reasoning": {"type": "string"},
    },
    "required": ["techniques", "overall_propaganda_score"],
}


# Bot Detection Schema (walkthrough 040 — added llm_text_likelihood).
# `is_bot` was dropped (2026-07-25): a lossy 2-value collapse of `label`
# (human/bot/suspicious), which is what engine/bot_detection.py actually reads.
BOT_SCHEMA = {
    "type": "object",
    "properties": {
        "label": {
            "type": "string",
            "enum": ["human", "bot", "suspicious"]
        },
        "confidence": {
            "type": "number"
        },
        "llm_text_likelihood": {
            "type": "number"
        },
        "indicators": {
            "type": "array",
            "items": {"type": "string"}
        },
        "reasoning": {
            "type": "string"
        }
    },
    "required": ["label", "confidence", "indicators"]
}
