# Civic Lens Scoring Methodology

> **Version**: 2.0  
> **Last Updated**: 2026-07-09

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

The `label` is derived from a blended 0-1 `score`, not from confidence:

| Label | Score Range | Action |
|-------|-----------------|--------|
| `human` | 0.0 - 0.4 | Included in all aggregations |
| `suspicious` | 0.4 - 0.7 | Included in aggregations, flagged for review |
| `bot` | 0.7 - 1.0 | **Excluded** from sentiment, favorability, and narrative aggregations |

### Signals

The detector blends an LLM (or heuristic) text-likelihood estimate with deterministic
behavioral signals into a single 0-1 `score`. Rather than fixed weights, it accumulates
human-readable indicator strings, each naming the exact signal that fired:

- **Text / stylometric**: near-duplicate or templated phrasing; unnatural typographic
  purity (smart quotes / em-dashes rare in casual social writing).
- **Account / behavioral**: new account (< 7 days), high posting rate (> 50/day),
  X account < 90 days old, X account with < 50 followers, non-US geo-tagged origin,
  follow-ratio anomalies, sustained high tweet-rate over account lifetime, active account
  with zero list memberships.
- **De-bias**: government-verified X accounts are forced to `human` with the score
  suppressed — an officeholder's account is not a bot in our model.

### Label & Confidence

The label comes from `score`; `confidence` is computed separately from signal strength and
indicator count (it is NOT a sum of indicator weights):

```python
# Label from the blended score
if score >= 0.7:    label = "bot"
elif score >= 0.4:  label = "suspicious"
else:               label = "human"

# Confidence from signal strength + indicator count
if score >= 0.7 and len(indicators) >= 2:
    confidence = min(0.6 + 0.1 * len(indicators), 0.95)
elif score >= 0.4:
    confidence = 0.5 + (score - 0.4)
else:
    confidence = 0.7 - score
```

### Output Schema

```json
{
  "label": "bot",
  "confidence": 0.85,
  "is_bot": true,
  "indicators": ["New account (3 days)", "High posting rate (120/day)", "X account has < 50 followers"],
  "reasoning": "Account shows automated posting patterns",
  "inference_method": "heuristic"
}
```

> [!IMPORTANT]  
> Classification is a probabilistic lead, not a verdict, and may include false positives.
> Each indicator names the specific behavior that triggered it so a reader can audit the call.
> Bot-flagged content is excluded from public-facing metrics but retained for audit.

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
  "confidence": 0.78,
  "evidence_spans": ["criticized the administration's handling of..."],
  "reasoning": "Strongly critical framing of the policy.",
  "sarcasm_detected": false,
  "inference_method": "heuristic"
}
```

> [!NOTE]  
> Sentiment represents content tone, not author intent. Sarcasm is flagged when the model
> detects it (`sarcasm_detected`) and the tone label accounts for it. Results are labeled as
> "sampled political discourse" in the UI, and net tone is rendered in points ("pts") on a
> -100 to +100 scale, not as a percentage.

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

## Propaganda Technique Detection

LLM-driven classifier that flags one or more of six starter rhetorical techniques in
political content. A flag measures rhetorical *style* — not truth, intent, or whether a post
is "propaganda" in the everyday sense.

### Techniques

`loaded_language`, `name_calling`, `ad_hominem`, `appeal_to_fear`, `whataboutism`, `doubt_casting`.

### Method

1. A cheap deterministic pre-gate short-circuits obviously plain text (no loaded-language
   markers in the opening) so the LLM is only spent where techniques are plausible.
2. The LLM returns candidate techniques, each with a verbatim `evidence_span`.
3. **Validation**: a technique whose evidence span is under four words, or is not a
   case-insensitive substring of the source text, is dropped. If the LLM returned techniques
   but none validate, `overall_propaganda_score` is capped at 0.2.

There is intentionally **no deterministic fallback** — a technique claim without a verifiable
quote is not surfaced.

### Output Schema

```json
{
  "techniques": [
    {"technique": "loaded_language", "confidence": 0.85, "evidence_span": "radical extremist agenda"}
  ],
  "overall_propaganda_score": 0.62
}
```

> [!NOTE]  
> `overall_propaganda_score` runs 0 (none) to 1 (saturated) — the mean technique intensity
> across scored posts. It is not a truth or intent score. The UI renders it as "0.62 / 1".

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
