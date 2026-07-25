-- data/pg-migrations/0005_drop_bot_score.sql
--
-- Retires the hand-tuned bot "score" column pair (owner decision
-- 2026-07-25, see docs/audit-trail/analysis/2026-07-25-llm-only-judgments.md
-- and docs/audit-trail/analysis/2026-07-25-bot-exclusion-gate.md). Since
-- 2026-07-25's removal of `_aggregate_score`, `analysis.bot_signals.score`
-- has been a literal copy of `llm_text_likelihood` (how machine-written the
-- TEXT reads), which is the wrong signal for an ACCOUNT-level bot-exclusion
-- gate and duplicated a column that already exists. The author-level gate
-- now reads `analysis.author_bot_scores.bot_post_count` +
-- `.suspicious_post_count` over `.sample_count` (a flagged-post SHARE) --
-- see `analysis/src/api/queries/constants.py`'s BOT_FLAGGED_SHARE_EXCLUSION
-- and `engine/bot_detection.py`'s `refresh_author_bot_scores()`. Those three
-- columns already existed and were already populated before this migration;
-- only `score` and `variance` (the variance OF score) are removed --
-- `llm_text_likelihood` / `llm_text_likelihood_mean` stay, unchanged: they
-- are the genuine LLM-text signal and remain useful on their own.
--
-- Applies cleanly on top of 0001_north_star.sql -- 0004_drop_serving.sql.
-- Not idempotent by itself -- same one-shot-apply convention as every other
-- file in this directory (the Go migration runner tracks the applied
-- version in ops.schema_migrations, so this file is never replayed against
-- an already-migrated database).

ALTER TABLE analysis.bot_signals DROP COLUMN score;

ALTER TABLE analysis.author_bot_scores DROP COLUMN score;
ALTER TABLE analysis.author_bot_scores DROP COLUMN variance;
