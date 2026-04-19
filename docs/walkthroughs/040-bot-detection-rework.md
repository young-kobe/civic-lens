# 040 — Bot Detection Rework (LLM-Driven Content + Account Rollup)

## Context

The pre-040 bot detector targeted **2018-era spambots** — keywords like `"buy now"`, `"viagra"`, high URL density. Against modern propaganda-driver accounts running LLM-generated political text, that detector scored everything as `human` because:
- LLM posts don't contain the old spam keywords.
- LLM output is lexically diverse (fails the repetition signal).
- Account age / follower ratio signals weren't de-biased by verification status.
- Nothing compared behavior across an author's posts — every judgment was single-post.

The stated product goal (see the review in this branch) is to flag **AI-driven** political content and feed that signal into the upcoming propaganda overlay ("this narrative is being pushed mostly by bot-looking accounts"). This walkthrough reorients the detector around that goal.

## Changes

### Scope + pre-exclusions

`analysis/src/scheduler/job_runner.py::run_bot_detection`:
- For every `x_post` doc, joins `x_posts_raw → x_users_raw → account_profiles` before classifying. If the author is classified `elected_official` or `affiliated`, or if `x_users_raw.verified_type == 'government'`, the detector is **skipped entirely** and a coverage row is written with `label='human'`, `confidence=1.0`, `inference_method='deterministic'`, and `indicators=['pre-excluded: tier=…']` (or `verified_type=government`). This preserves the audit trail while guaranteeing those accounts are never bot-flagged.
- For non-excluded `x_posts`, the same join enriches the `metadata` dict with `verified`, `verified_type`, `user_followers`, `user_following`, `user_listed`, `user_tweet_count`, and `user_created_at` — so the detector has the signals it needs. Reddit posts still flow through untouched (no equivalent metadata; no tier classification).
- News docs are already excluded upstream via `CIVIC_RUN_ANALYSIS_ON="social_media"`.

### Schema — account-level rollup

`data/migrations/014_author_bot_scores.sql` — new `author_bot_scores(platform, author_id, score, variance, sample_count, bot_post_count, suspicious_post_count, llm_text_likelihood_mean, stylometric_features_json, updated_at)` keyed on `(platform, author_id)`. Rolled up from `ai_outputs.bot_detection` rows by a new pipeline step; used (starting 041) by the propaganda overlay to show "fraction of supporting docs from automated-looking accounts."

### Constants — LLM-oriented signal lists

`analysis/src/engine/constants.py`:
- **`SPAM_KEYWORDS`** trimmed from 17 → 7 entries. Still catches classic affiliate spam, no longer the headline signal.
- **`LLM_HEDGE_PHRASES`** (new, 35 entries) — phrases LLM outputs over-index on: `"it's important to note"`, `"on one hand"`, `"various factors"`, `"as an ai language model"`, `"in conclusion"`, etc. Matched as case-insensitive substrings.
- **`LLM_TYPOGRAPHIC_TELLS`** (new) — em-dash (U+2014), smart quotes (U+2018/2019/201C/201D), ellipsis character (U+2026). LLMs use these at unnatural rates in casual social-media writing.
- **`COORDINATION_PATTERNS`** gained a `"llm_text_style"` label.

### Engine — stylometric signals + metadata de-biasing

`analysis/src/engine/bot.py` — rewrote `_compute_signals` and `_aggregate_score`.

New deterministic signals in each post's `deterministic_signals` dict:
- `sentence_length_variance` — population variance of per-sentence word counts (low = uniform / LLM-like).
- `hedge_phrase_hits` + `hedge_phrase_rate` — hits per 100 words.
- `typographic_purity_score` — fraction of `LLM_TYPOGRAPHIC_TELLS` present (0..1).
- `follow_ratio_anomaly` — X: `following > 1000 and followers < 10% of following`.
- `sustained_tweet_rate_flag` — X: `tweet_count / account_age_days > 50`.
- `unlisted_active_flag` — X: `tweet_count > 500 and listed_count == 0`.

Score composition (all contributions tuned for placeholder thresholds until calibration lands in walkthrough 044):

| Signal | Contribution |
|---|---|
| Spam keyword hits | 0.3 + 0.1×min(hits, 3) |
| Repetition score > 0.5 | +0.2 |
| URL density > 10% | +0.1 |
| Hashtag count > 5 | +0.08 |
| Uniform sentence length (variance < 4, word_count > 30) | +0.15 |
| Hedge phrase rate > 1.0 | +0.18 |
| Typographic purity ≥ 0.5 | +0.12 |
| Account age < 7 | +0.12 |
| Posting freq > 50/day | +0.15 |
| X new account (< 90 days) | +0.08 |
| X low followers (< 50) | +0.05 |
| X foreign origin (high confidence) | +0.10 |
| Follow-ratio anomaly | +0.20 |
| Sustained tweet rate | +0.15 |
| Unlisted active | +0.08 |

**De-biasing** (applied LAST):
- `verified_type == 'government'` → score forced to **0.0** and label to `human`. Indicators include `"verified_type=government (de-biased)"`. Government accounts cannot be bots in this system.
- `verified_type == 'business'` → score capped at **0.3** (business-verified accounts rarely fit the automation profile).
- `verified_type == 'blue'` → no effect (paid verification is not a meaningful signal).

`BotResult` gains a new `llm_text_likelihood: float` field — a 0..1 "how LLM-generated does the TEXT look," independent of `is_bot` account-level judgment. A gov press release can score high on `llm_text_likelihood` but still stays `is_bot=False` thanks to the government de-bias.

### Prompt + schema

`analysis/src/engine/prompts.py::BOT_SYSTEM_PROMPT` rewritten: explicitly asks about LLM-generated text tells (uniform sentence-length, hedge phrases, typographic purity) AND automated-account patterns. De-bias rules are codified in the prompt itself — the LLM is instructed never to label a `verified_type='government'` account as a bot. New user-prompt template passes stylometric + behavioral + account signals.

`analysis/src/llm/schemas.py::BOT_SCHEMA` — added `llm_text_likelihood: number [0,1]` as an optional field.

### New pipeline step — account rollup

`analysis/src/scheduler/job_runner.py::run_account_bot_rollup` (step 8/9, between account classification and snapshots):
- Reads `ai_outputs` rows with `task_type='bot_detection'` AND `inference_method != 'deterministic'` (i.e. skips pre-excluded rows) joined to `docs → x_posts_raw` to get the author_id.
- Groups per author, computes mean + variance of `aggregated_score`, counts bot-labeled / suspicious-labeled posts, averages `llm_text_likelihood`, UPSERTs into `author_bot_scores`.
- Reddit is out of scope — no author field surfaced at ingest time.
- New CLI `-Tasks bot_rollup` to run just this step.

### Aggregator — real numbers replacing stubs

`analysis/src/reporting/aggregators/bot.py` — full rewrite:
- **Denominator fix:** `total_eligible` excludes rows where `inference_method='deterministic'` (pre-excluded gov / electeds). Automation rate now reports over eligible posts only.
- **`accountAgeDistribution`** computed from `x_users_raw.created_at` for the flagged X authors, bucketed as `< 7 days`, `7-30`, `30-90`, `90-365`, `1-3 years`, `3+ years`, `unknown`.
- **`identicalTextPairs`** counts exact duplicates across flagged bot posts — `C(n, 2)` for each group of identical text, summed.
- **`copyPasteSimilarity`** bucketed high/medium/low using 8-word shingled Jaccard over a cap of 400 bot posts (O(N²)-safe).
- **`accountReuse`** = fraction of unique authors with > 1 flagged post.
- **`avgPostsPerSuspectedAccount`** = flagged post count ÷ unique flagged authors.
- **`linkDomainConcentration`** — real domain histogram over URLs extracted from flagged post text.

The UI's `BotData` shape is unchanged; the backing data is real now.

### Tests

`analysis/tests/test_bot_rework.py` — 17 new tests:
- **Stylometric helpers (6)**: empty/short → variance 0; uniform text → 0; varied text → > 1; hedge substrings counted; typographic purity scales with tells; `_heuristic_llm_text_likelihood` triages LLM-like vs human-like.
- **Aggregate score (5)**: baseline human low; `verified_type='government'` forces 0; `business` caps at 0.3; follow-ratio anomaly bumps score; three stylometric hits alone push over 0.4.
- **Detector paths (2)**: gov-verified author is not flagged despite obvious spam text; unlisted-active indicator fires when `tweet_count > 500 AND listed_count == 0`.
- **Aggregator helpers (3)**: shingle Jaccard works; identical-text pairs count correctly; link-domain concentration picks the top domain.
- **Aggregator end-to-end (1)**: pre-excluded deterministic rows are dropped from the denominator, giving honest `suspectedAutomationRate`.

Existing pre-040 tests adjusted:
- `test_engines.test_bot_detector` — spam test text updated to "buy now click here free gift act now" because `"make money"` is no longer in `SPAM_KEYWORDS` (3 hits instead of 2 to clear the 0.5 threshold).

## Verification

- Migration 014 applied cleanly against the live dev DB.
- 17/17 new tests pass.
- Affected-module bundle (bot_rework + engines + llm_engines.TestHybridBotDetector + inference_method + aggregation_confidence_filter + propagation + account_classifier + review + refresh_accounts + rich_aggregators) — 109/109 pass.
- Pre-existing `test_llm_engines.test_deterministic_fallback_favorability` still fails for its unrelated `"Trump"` vs `"trump"` case-sensitivity reason (untouched by 040).

## What it does NOT yet do (deferred to later walkthroughs)

- **Cross-post stylometric aggregation.** `author_bot_scores.stylometric_features_json` is defined in the schema but not populated — per-post stylometric features live in `deterministic_signals`; averaging them into the author row can follow when propaganda detection needs them (041).
- **Timing-burst coordination detection.** The `coordinationIndex` still uses the pre-040 "hour concentration" proxy. Proper burst-timing analysis (e.g., account groups posting within a 30-minute window) is coordinated-inauthentic-behavior territory, a separate workstream.
- **Calibration.** All scoring thresholds are placeholders until a labeled golden set exists (walkthrough 044). The Review tab (034) already supports `task_type='bot_detection'` so curating labeled examples can start now.

## Deploy

```powershell
.\run.ps1 migrate
.\run.ps1 analyze -Tasks bot,bot_rollup,snapshots
```

The first analyze run after this walkthrough will re-score every bot_detection row with the new signals. Existing pre-040 rows stay in place (historical audit trail); new rows use the LLM-oriented scoring. `author_bot_scores` is populated on the `bot_rollup` step.

## Roadmap — shift remaining

| # | Scope |
|---|---|
| 041 | Cache + versioning + stubs cleanup — geo-sentiment caching, variable-limit narratives, complete B1 versioning |
| 042 | Propaganda pipeline — backend (taxonomy, prompts, detector, loader + job_runner wiring, **uses `author_bot_scores` as a narrative-overlay signal**) |
| 043 | Propaganda pipeline — surfaces (aggregator, API, UI tab, review-task extension) |
| 044 | Calibration harness — reads `ai_output_evals WHERE is_golden=1`, produces accuracy curves per task (**now includes bot_detection** as a first-class calibrated task) |
