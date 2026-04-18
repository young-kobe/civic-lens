# 028 - Sentiment Classification, Polling, and UI Enrichment

## Changes Made

### 1. Sentiment Classification (Analysis Layer)

| File | Change |
|------|--------|
| `prompts.py` | Bumped to `text-analysis-v2`. Added rules: contextual sentiment, derogatory content = NEGATIVE, evidence must be 4+ word verbatim phrases, anti-parrot rule (no returning schema examples), dedup |
| `constants.py` | Added 20+ derogatory/inflammatory terms to `NEGATIVE_WORDS` |
| `test_engines.py` | Added `TestAnalyzer` with 5 heuristic sentiment test cases |

### 2. Evidence Quality (Aggregator Layer)

| File | Change |
|------|--------|
| `sentiment.py` | Added `_sanitize_evidence()`: deduplicates spans, removes placeholder text, filters name-only (<3 words) and @-mention-only spans, caps at 5 per sample |

### 3. Polling Scraper (ETL Layer)

| File | Change |
|------|--------|
| `polling.py` | Dynamic party URL (`gop`, `democratic`), HTML table parsing for RCP Average row, retry with backoff, percentage validation |
| `test_polling.py` | 12 test cases covering table parsing, URL generation, retry, validation |

### 4. UI Enrichment (React Layer)

| File | Change |
|------|--------|
| `PublicSentiment.tsx` | Evidence filtering (dedup, placeholder removal, <3-word rejection, cap at 5), labeled confidence indicator with hover tooltip, source badges, expandable reasoning, polling source/date attribution |
| `types.ts` | Added `source` and `date` to polling data type |
| `transformers.ts` | Pass-through of `source` and `date` fields |

## Test Results

- **`test_engines.py`**: 7/7 passed (including 5 new Analyzer tests)
- **`test_polling.py`**: 12/12 passed
- **`test_rich_aggregators.py`**: 5/5 passed
- **TypeScript**: Clean compile (`tsc --noEmit` no errors)

## Notes

To re-analyze existing content with the new v2 prompt, run:

```powershell
python -m analysis.src.scheduler.job_runner
```

Heuristic improvements take effect immediately for non-LLM mode. The stale classifications visible in the UI (e.g. "Democrats are Satanists" labeled POSITIVE) reflect cached v1 results and will be corrected on re-analysis.

## Follow-Up: Transparency & Accuracy Enhancements

After reviewing the initial UI implementation with the live LLM outputs, several issues related to data transparency and classification accuracy were identified and remediated.

### 1. Data Attribution & Transparency
- **Source Text Visibility**: Extracted `d.text` (full text) from the SQLite database and plumbed it completely through the backend aggregations (`sentiment.py`) and TypeScript data models. The full text is now rendered dynamically within the UI drill-down card and is collapsible alongside the LLM reasoning, allowing users to verify short edge-case evidence spans against the entire raw source.
- **External URL Linking**: Extracted `d.ident` from the database. Added a robust fallback mechanism to map Reddit IDs to valid `https://reddit.com/r/...` URLs, alongside standard web URLs. Added a clickable off-platform `View Original` link pinned to the Source Text card on the dashboard.
- **Drill-Down Metadata**: Display the published date and explicitly scraped source domain/author directly in the classification cards alongside the platform badge.

### 2. Evidence Filtering
- **Removed Aggressive Suppression**: Removed the `<3` word-count filter and the regex `@mention` filter in `sentiment.py` and `PublicSentiment.tsx`. These filters were actively hiding short poor-quality LLM outputs (e.g. single words or mentions), which created UI bugs where cards completely lacked evidence. To maximize transparency, all LLM-extracted spans are now shown raw, regardless of length.

### 3. Classification UI/UX
- **Sentiment Designation Confusion**: Users occasionally mistook standard positive/negative sentiment for Political Favorability. Renamed the sentiment pill labels in the UI to explicitly say "POSITIVE TONE", "NEGATIVE TONE", etc. to distinguish generic emotional intent from GOP favorability.
- **Hover UI**: Updated `MethodPopover.tsx` to automatically open/close on `onMouseEnter` and `onMouseLeave` (in addition to clicks) to improve the heuristic tooling experience.

### 4. LLM Heuristic Independence
- **LLM Prompt Poisoning**: Pre-computed deterministic heuristic signals (counts of positive/negative words and matching unfavorable keywords) had been actively injected into `TEXT_ANALYSIS_USER_PROMPT_TEMPLATE`. This completely contorted the LLM's logic, leading to wildly contradictory results ("setting a fraudster free -> positive outcome") because the LLM was prioritizing justifying the existence of the heuristic markers over performing valid organic semantic analysis.
- **Fix Applied**: Stripped all heuristic counters/signals from the user prompt. The LLM now evaluates the pure `text` exclusively using its core emotional intent system prompt.
- **Post-Fix**: Deleted the `sentiment` and `favorability` records from the `ai_outputs` table and initiated a full background pipeline re-run via `run.ps1 analyze` to overwrite the poisoned data.

### 5. Context Accuracy & Nuance
- **Reddit Comments (Go Crawler)**: The system was previously only extracting the post Title and assuming the URL was linked content. For "Link Posts" common in political subreddits, this resulted in an empty text body, causing the LLM to hallucinate sentiment logic over 10-word headlines. Updated `ingest/internal/runner/reddit.go` to explicitly query the top 5 comments from the Reddit `comments.json` API and concatenate them directly onto the `body` field with the lowest permissible 500ms API sleep to prevent rate-limiting. This transparently feeds rich, long-form discussion context directly into the downstream Python analysis layer.
- **Reasoning Schema Extraction**: The LLM prompt was previously instructing the model to output a single `reasoning` field to justify both its generic emotional Tone (Sentiment) and its GOP Stance (Favorability). This caused the UI to display contradictory text (e.g. displaying "Positive Tone" but printing favorability logic about why the GOP was being attacked). Updated `TEXT_ANALYSIS_SCHEMA` and `prompts.py` to enforce two strictly independent keys: `sentiment_reasoning` and `favorability_reasoning`. Mapped this directly in `analyzer.py`. The sentiment UI will now only display the strictly relevant baseline emotional logic.
