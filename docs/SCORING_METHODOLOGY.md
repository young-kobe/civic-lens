# Civic Lens Scoring Methodology

> **Version**: 1.0  
> **Last Updated**: 2026-01-23

---

## Overview

Civic Lens uses a hybrid analysis approach combining:
1. **Deterministic Heuristics**: Fast, reproducible pattern matching
2. **LLM Analysis (Optional)**: Enhanced interpretation when enabled

All scores include confidence values and are stored with full traceability in the `ai_outputs` table.

---

## Bot Detection Scoring

Identifies potential automated accounts or coordinated inauthentic behavior.

### Labels

| Label | Confidence Range | Action |
|-------|-----------------|--------|
| `human` | 0.0 - 0.3 | Included in all aggregations |
| `suspicious` | 0.3 - 0.7 | Included in aggregations, flagged for review |
| `bot` | 0.7 - 1.0 | **Excluded** from sentiment, favorability, and cluster APIs |

### Indicators Checked

| Indicator | Weight | Description |
|-----------|--------|-------------|
| `new_account` | 0.15 | Account age < 30 days |
| `high_frequency` | 0.20 | Posting rate > 10 posts/hour |
| `repetitive_content` | 0.25 | >70% similar text across posts |
| `templated_text` | 0.20 | Matches known template patterns |
| `coordination_timing` | 0.20 | Posts clustered within seconds of others |

### Confidence Calculation

```python
confidence = sum(indicator_weight for indicator in detected_indicators)
confidence = min(1.0, confidence)  # Cap at 1.0

if confidence >= 0.7:
    label = "bot"
elif confidence >= 0.3:
    label = "suspicious"
else:
    label = "human"
```

### Output Schema

```json
{
  "label": "bot",
  "confidence": 0.85,
  "is_bot": true,
  "indicators": ["high_frequency", "repetitive_content", "new_account"],
  "explanation": "Account shows automated posting patterns"
}
```

> [!IMPORTANT]  
> Classification is heuristic-based and may include false positives. All bot-flagged content is excluded from public-facing metrics but retained for audit.

---

## Sentiment Analysis Scoring

Classifies content emotional tone toward topics discussed.

### Labels

| Label | Numeric Mapping | Description |
|-------|-----------------|-------------|
| `POSITIVE` | +1 | Favorable, supportive, optimistic tone |
| `NEGATIVE` | -1 | Critical, opposing, pessimistic tone |
| `NEUTRAL` | 0 | Factual, balanced, no strong emotion |
| `MIXED` | 0 | Contains both positive and negative elements |

### Heuristic Method (Default)

Uses lexicon-based analysis with political domain vocabulary:

1. Tokenize text
2. Match against positive/negative political lexicons
3. Count weighted matches
4. Apply contextual modifiers (negation, intensifiers)

```python
positive_score = sum(weights for positive_tokens_found)
negative_score = sum(weights for negative_tokens_found)

if positive_score > negative_score * 1.5:
    label = "POSITIVE"
elif negative_score > positive_score * 1.5:
    label = "NEGATIVE"
elif positive_score > 0 and negative_score > 0:
    label = "MIXED"
else:
    label = "NEUTRAL"
```

### LLM-Enhanced Method (When Enabled)

Prompts LLM with:
```
Analyze the sentiment of this political content.
Classify as: POSITIVE, NEGATIVE, NEUTRAL, or MIXED.
Provide confidence score.
```

### Net Score Calculation (Aggregated)

```python
net_score = ((positive_count - negative_count) / total_count) * 100
```

| Net Score | Interpretation |
|-----------|----------------|
| +100 | All content positive |
| +50 to +100 | Strongly positive |
| +10 to +50 | Slightly positive |
| -10 to +10 | Neutral/balanced |
| -50 to -10 | Slightly negative |
| -100 to -50 | Strongly negative |
| -100 | All content negative |

### Output Schema

```json
{
  "label": "NEGATIVE",
  "score": -0.65,
  "confidence": 0.78,
  "method": "heuristic"
}
```

> [!NOTE]  
> Sentiment represents content tone, not author intent. Results are labeled as "sampled platform discourse" in the UI.

---

## GOP Favorability Scoring

Measures content stance toward Republican Party / GOP positions.

### Stances

| Stance | Numeric Mapping | Description |
|--------|-----------------|-------------|
| `favorable` | +1 | Supports GOP positions/candidates |
| `unfavorable` | -1 | Criticizes GOP positions/candidates |
| `neutral` | 0 | No partisan framing detected |
| `mixed` | 0 | Contains both favorable and unfavorable elements |

### Detection Method

1. **Entity Recognition**: Identify GOP-related entities (Republican, Trump, McConnell, etc.)
2. **Context Analysis**: Determine sentiment toward those entities
3. **Stance Classification**: Map entity-sentiment pairs to overall stance

### Keyword Categories

**Pro-GOP Indicators**:
- Positive framing of Republican actions
- Criticism of Democratic positions
- Support for conservative policies

**Anti-GOP Indicators**:
- Criticism of Republican actions
- Support for Democratic positions
- Opposition to conservative policies

### Net Favorability Calculation

```python
net_favorability = ((favorable - unfavorable) / total) * 100
```

### Platform Normalization

Reddit source types are normalized:
```python
if source_type in ('reddit_post', 'reddit_comment'):
    platform = 'reddit'
```

### Output Schema

```json
{
  "overall_gop_stance": "unfavorable",
  "entity_stances": {
    "Trump": "unfavorable",
    "Republican Party": "neutral"
  },
  "confidence": 0.72,
  "evidence_spans": ["criticized the administration's..."]
}
```

> [!WARNING]  
> This is a **proxy metric** based on sampled media/social discourse, NOT polling data. Results represent content sentiment toward GOP, not verified population opinion.

---

## Confidence Scoring

All analyses include confidence scores indicating certainty.

| Confidence | Level | Interpretation |
|------------|-------|----------------|
| 0.0 - 0.3 | Low | Uncertain, may need human review |
| 0.3 - 0.6 | Medium | Reasonable certainty |
| 0.6 - 0.8 | High | Strong certainty |
| 0.8 - 1.0 | Very High | Near-certain classification |

### Factors Affecting Confidence

- Text length (longer = more signal = higher confidence)
- Keyword density
- Ambiguity in language
- Mixed signals in content

---

## Time-Based Filtering

All aggregations support filtering by time window:

| Filter | Seconds | Use Case |
|--------|---------|----------|
| 24h | 86,400 | Breaking news, trending |
| 7d | 604,800 | Weekly trends |
| 30d | 2,592,000 | Monthly analysis (default) |
| 90d | 7,776,000 | Quarterly trends |

### Implementation

```python
now = int(time.time())
cutoff = now - seconds_for_window

docs = query("SELECT * FROM docs WHERE published_at >= ?", [cutoff])
```

---

## Traceability

All scores are traceable to:
1. **Source Document**: `ai_outputs.doc_id` -> `docs.ident`
2. **Model Used**: `ai_outputs.model_id`
3. **Prompt Version**: `ai_outputs.prompt_version`
4. **Timestamp**: `ai_outputs.created_at`
5. **Raw Evidence**: `output_json.evidence_spans` (when available)

This ensures reproducibility and audit capability per project invariants.
