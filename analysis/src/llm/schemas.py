"""
JSON Schemas for LLM Structured Output.

These schemas are passed to Ollama (format parameter) and Gemini
(response_schema) to enforce deterministic JSON responses from the LLM.

Note: Gemini's schema validator rejects OpenAPI-spec ``minimum``/``maximum``
keywords. Numeric confidences are documented as being in [0,1] in the
prompts, and the engine code clamps when it reads them — we don't bake
range constraints into the schema here.
"""

# Entity Stance Schema (nested in favorability)
ENTITY_STANCE_SCHEMA = {
    "type": "object",
    "properties": {
        "entity": {"type": "string"},
        "stance": {
            "type": "string",
            "enum": ["favorable", "unfavorable", "neutral", "mixed"]
        },
        "confidence": {"type": "number"},
        "evidence_spans": {
            "type": "array",
            "items": {"type": "string"}
        }
    },
    "required": ["entity", "stance", "confidence"]
}

# Text Analysis Schema (Sentiment and Favorability)
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
        "entity_stances": {
            "type": "array",
            "items": ENTITY_STANCE_SCHEMA
        },
        "overall_gop_stance": {
            "type": "string",
            "enum": ["favorable", "unfavorable", "neutral", "mixed"]
        },
        "overall_favorability_confidence": {
            "type": "number"
        },
        "sentiment_reasoning": {
            "type": "string"
        },
        "favorability_reasoning": {
            "type": "string"
        }
    },
    "required": [
        "sentiment_label", "sentiment_confidence", "sentiment_evidence_spans",
        "sarcasm_detected", "overall_gop_stance", "overall_favorability_confidence",
        "sentiment_reasoning", "favorability_reasoning"
    ]
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


# Bot Detection Schema (walkthrough 040 — added llm_text_likelihood)
BOT_SCHEMA = {
    "type": "object",
    "properties": {
        "is_bot": {
            "type": "boolean"
        },
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
    "required": ["is_bot", "label", "confidence", "indicators"]
}
