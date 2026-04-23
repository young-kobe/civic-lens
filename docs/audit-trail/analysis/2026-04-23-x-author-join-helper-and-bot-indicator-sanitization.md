# 2026-04-23 — X-author join helper + bot indicator sanitization + politician pre-exclusion revert

Three related analysis-layer changes landed together. The thread connecting them: every one closes a TODO item that was blocking the UI from showing reliable, non-noisy data for politician-operated X accounts.

## What shipped

### `X_AUTHOR_JOIN_SQL` — single source of truth for the X-author join

`analysis/src/reporting/aggregators/base.py`. New module-level constant holding the two LEFT JOINs (`x_posts_raw` + `x_users_raw`) that ten aggregator query sites used to duplicate verbatim:

```python
X_AUTHOR_JOIN_SQL = (
    "LEFT JOIN x_posts_raw x "
    "ON d.source_type = 'x_post' AND x.tweet_id = d.ident "
    "LEFT JOIN x_users_raw u ON u.user_id = x.author_id"
)
```

Call sites migrated (closes `docs/todos/backend-aggregator-audit.md` §1):

- `sentiment.py::get_public_sentiment` — via `fetch_task_rows(... extra_joins=X_AUTHOR_JOIN_SQL)`
- `bot.py::_fetch_bot_detection_data` — f-string injection
- `bot.py::_fetch_entity_rollups` — f-string injection
- `movers.py::_fetch_sentiment_rows` — f-string injection
- `narrative.py::_narrative_tiers` — f-string injection
- `narrative.py::_first_seen_info` — f-string injection
- `narrative.py::_top_supporting_docs` — f-string injection
- `propaganda.py::_fetch_propaganda_rows` — f-string injection
- `propaganda.py::_fetch_examples` — f-string injection
- `review.py::ReviewQueue.items` — f-string injection

Assumption baked into the constant: callers alias `docs` as `d`. Both joins are `LEFT` so non-x_post rows pass through with NULLs. A future schema change (materialized view, derived author table, etc.) touches one constant and every aggregator picks it up.

### Bot indicator sanitization at the source

`analysis/src/engine/bot.py`. Closes `docs/todos/bot-propaganda-entity-signals.md` §"Sanitize whyFlagged at source."

**Root cause of the noise.** `BOT_USER_PROMPT_TEMPLATE` lines like `"Account age: {account_age_days} days"` rendered as `"Account age: None days"` when the signal field was None. The LLM sometimes echoed those literals back in its `indicators: List[str]` output, producing entries like `"account_age=None days"` that the UI had to strip on display (`sanitizeWhyFlagged` in `BotActivityProfiler.tsx`).

**Two-part fix.** Both layers now live in `bot.py`:

1. `_safe_prompt_value(v)` — routes any signal field through a string
   coercer that maps `None`, empty, `"None"`, `"null"`, `"undefined"`,
   `"N/A"` all to `"unknown"` before the prompt template formats them.
   The model never sees an ambiguous placeholder in the first place.
   Integer `0` (a legitimate follower count) survives the mapping as
   the literal string `"0"`.
2. `_sanitize_llm_indicators(raw)` — filters the returned indicators
   array through five noise patterns (`=None` / `=null` / `=undefined`
   / `=0` / trailing `=`). Applied to both the LLM classifier and the
   heuristic classifier so the output contract is uniform regardless
   of which path ran.

Unit tests: `analysis/tests/test_bot_indicator_sanitization.py` — 12 cases covering None substitution, zero-as-signal, case-insensitivity, non-list input coercion, and preservation of legitimate `key=value` indicators like `followers=50`.

UI workaround removed: `sanitizeWhyFlagged` + `isNoiseNarrative` functions deleted from `ui/src/pages/BotActivityProfiler.tsx`; the card now renders `narrative.whyFlagged` directly. Historical ai_outputs rows written before this fix may surface noise until they age out of the snapshot windows (~7 days).

### Politician pre-exclusion reverted

`analysis/src/scheduler/job_runner.py::_enrich_x_metadata`. A rule added back in walkthrough 040 pre-excluded X accounts with `tier in ("elected_official", "affiliated")` from bot detection entirely — a coverage row with `inference_method="deterministic"` and `label="human"` got written instead of running the classifier. Removed today.

**Why the revert.** Politicians and their staff DO use automation — scheduled cross-posting, platform-native scheduling tools, party-coordinated messaging from multiple officeholders, overnight tweets from accounts that are mechanically clearly staff-run. Pre-excluding every `elected_official` account made the "Politicians & Officials" column on the Bot Detector page permanently empty and hid a class of signal that's actually interesting.

**What's kept.** The `verified_type == "government"` pre-exclusion still applies — that's the X API's own `user.fields=verified_type` response (`none | blue | business | government`), not our registry, and it flags state-operated institutional channels (`@WhiteHouse`, federal agency accounts) that are human-run by institutional necessity. That's a tighter, more defensible line than "every tracked politician."

Paired with the change: the heuristic classifier's existing post-hoc `verified_type == "government"` de-bias (lines 274-287 of `bot.py`) is unchanged. That's the surgical counterpart that suppresses score for the same class of account at the end of classification, preserving the raw behavioral numbers on the `deterministic_signals` field for audit.

### Cost-capped initial-seed script

`deploy/scripts/seed-initial.sh`. Added `SEED_LIMIT=50` env override on the analyze invocation. At ~$0.0005/Gemini-Flash-call × 4 LLM stages (bot + text + propaganda + claims) × 50 docs, a full seed costs ≈ $0.10 regardless of how many articles got crawled. The first post-seed timer fire drains the backlog at the configured `CIVIC_LOADER_BATCH_SIZE` (currently 200).

Also shortened the default crawl from 30m to 15m (`SEED_CRAWL_DURATION=15m`) — 15 minutes already drains most RSS feeds once; anything further can wait for the hourly steady-state timer.

## Why this batch shipped together

User asked whether the pipeline only shows "today's" data if politicians didn't tweet today — the aggregator windows correctly look back the full 7d/30d, but on a fresh prod install the DB is empty, so a seed script that bulk-runs the fetchers + analyzer is the right answer. While digging into the bot detector for the registry question, the related indicator sanitization and politician pre-exclusion revert surfaced as inconsistencies worth closing in the same pass.

## Validation

- `python -m unittest discover analysis/tests` — all 43 tests pass (6 engine + 12 new sanitization + the rest).
- `npm run typecheck` + `npm run build` — clean; UI bundle 629.19 kB / 181.46 kB gzipped.
- Aggregator imports all resolve via the new `X_AUTHOR_JOIN_SQL` constant.

## Follow-ups

- Backend-aggregator-audit §1 is now closed; §2-#10 still open. Next up from that list: §4 (`with_cutoff` helper — narrative.py alone has 10 inline `if cutoff is not None` branches) and §7 (`format_doc_source` consolidation).
- Bot detector TODO partially closed; registry-based signals remain future work (but as finer-grained downweights rather than blanket pre-exclusions, per today's revert).
- Propaganda detector entity-signal work still deferred — needs A/B evaluation against the golden set before any prompt or threshold change.
