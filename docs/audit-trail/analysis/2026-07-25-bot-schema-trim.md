# 2026-07-25 — Bot task drops `is_bot` and two data-less account tells

The bot-detection task's LLM schema and prompt (`analysis/src/llm/{schemas,prompts}.py`) drop `is_bot` — a lossy two-value collapse of `label` (human/bot/suspicious), which is what `engine/bot_detection.py` actually reads — plus two "automated-account tell" prompt bullets and their user-prompt-template placeholders (`posting_frequency`, `listed_count`) that have never had a real data source in the Postgres-redesign stack.

## CRITICAL DEPLOY CONSTRAINT

Same as the paired text-engine entry (`docs/audit-trail/analysis/2026-07-25-text-sentiment-only.md`): `analysis/src/engine/bot.py` (the RETIRED SQLite stack, still driven by `scheduler/job_runner.py` on production's systemd timer) imports the SAME `BOT_SCHEMA`/`BOT_SYSTEM_PROMPT`/`BOT_USER_PROMPT_TEMPLATE` objects this change edits — it has no private copy. After this lands, `bot.py` reads `is_bot` off a `.get()` default and will NOT crash; it will silently fall back to whatever its default resolves to instead of a real model judgment. **The owner must disable the analysis timer before this branch reaches production.** No fork was made; the retired stack is deleted at Phase 11, not forked.

## What shipped

- **`analysis/src/llm/schemas.py`**: `BOT_SCHEMA` drops `is_bot` from `properties` and `required`.
- **`analysis/src/llm/prompts.py`**: `BOT_PROMPT_VERSION` bumped `bot-v2` -> `bot-v3`. `BOT_SYSTEM_PROMPT`'s "Automated-account tells" list drops "Sustained high tweet rate across the account lifetime" and "Active account with zero list memberships and generic bio" (2 of 4 bullets; the follow-ratio-skew and recent-account tells stay). Rule 2 ("If data is insufficient, set is_bot=false...") and rule 4 (the government-press-release example) reworded around `label='human'` instead of `is_bot=false`, since the schema no longer has that field — leaving the old wording would have described a field the model can no longer return. `is_bot` removed from the OUTPUT SCHEMA block. `BOT_USER_PROMPT_TEMPLATE` drops the `Posting frequency: {posting_frequency} posts/day` line and shortens `Followers / Following / Listed: {followers} / {following} / {listed_count}` to `Followers / Following: {followers} / {following}`.
- **`analysis/src/engine/bot_detection.py`**: `_llm_analysis()` drops the hardcoded `posting_frequency="unknown"`/`listed_count="unknown"` keyword arguments to `BOT_USER_PROMPT_TEMPLATE.format()` (the template no longer has those placeholders). Docstring updated to describe the current state rather than the now-removed hardcoding.
- **Tests**: `analysis/tests/test_engine_bot.py`'s `_valid_llm_response()` fixture drops `is_bot` (no test asserted on it). New `analysis/tests/test_text_bot_schema_trim.py::BotSchemaTrimTests` asserts `is_bot` is gone from both the schema and the prompt, and that the two dropped account-tell phrases no longer appear in `BOT_SYSTEM_PROMPT`.

## Why

- **`is_bot` was strictly lossy.** `label` already carries `human`/`bot`/`suspicious`; collapsing that onto a boolean threw away the `suspicious` middle case for no reader that used it — `bot_detection.py`'s `analyze()`/`process()` only ever read `label`.
- **Token cost, not just tidiness.** `posting_frequency`/`listed_count` have had no data source at all since the Postgres-redesign bot engine landed (`BotDocInput`'s own docstring says so: `corpus.authors` carries no equivalent column and neither field ever had a real producer). Every bot-task call was spending roughly 40 tokens asking the model to reason about two literal `"unknown"` strings it could never resolve to anything else. Removing them is a real per-call cost reduction with zero information loss.

## Follow-ups

- **Owner action required before deploy**: disable the analysis systemd timer before this branch reaches production, same as the text-engine entry — `engine/bot.py` is untouched and still reads the shared prompt/schema objects.
- `docs/todos/pg-redesign.md`'s existing Phase 11 line already covers deleting `engine/bot.py` alongside `engine/analyzer.py`; no new todo needed for this change specifically.
