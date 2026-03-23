"""
JSON Schemas for LLM Structured Output.

These schemas are passed to Ollama (format parameter) and Gemini (response_schema)
to enforce deterministic JSON responses from the LLM.
"""

# Sentiment Analysis Schema
SENTIMENT_SCHEMA = {
    "type": "object",
    "properties": {
        "label": {
            "type": "string",
            "enum": ["POSITIVE", "NEGATIVE", "NEUTRAL", "MIXED"]
        },
        "confidence": {
            "type": "number",
            "minimum": 0,
            "maximum": 1
        },
        "evidence_spans": {
            "type": "array",
            "items": {"type": "string"}
        },
        "reasoning": {
            "type": "string"
        },
        "sarcasm_detected": {
            "type": "boolean"
        }
    },
    "required": ["label", "confidence", "evidence_spans", "reasoning", "sarcasm_detected"]
}

# Bot Detection Schema
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
            "type": "number",
            "minimum": 0,
            "maximum": 1
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

# Favorability Analysis Schema
FAVORABILITY_SCHEMA = {
    "type": "object",
    "properties": {
        "entity_stances": {
            "type": "array",
            "items": ENTITY_STANCE_SCHEMA
        },
        "overall_gop_stance": {
            "type": "string",
            "enum": ["favorable", "unfavorable", "neutral", "mixed"]
        },
        "overall_confidence": {
            "type": "number",
            "minimum": 0,
            "maximum": 1
        },
        "reasoning": {
            "type": "string"
        }
    },
    "required": ["overall_gop_stance", "overall_confidence"]
}
