-- 014_author_bot_scores.sql
-- Account-level bot scoring rollup (walkthrough 040).
--
-- Per-post bot_detection rows in ai_outputs answer "does THIS post look
-- automated?" The propaganda-overlay question we actually want to answer is
-- "is THIS ACCOUNT likely driven by a bot / LLM?" — which requires averaging
-- a single author's posts together. One account with 40 flagged posts is a
-- very different signal from 40 accounts each with 1 flagged post.
--
-- Rollup is recomputed by job_runner.run_account_bot_rollup from the
-- ai_outputs bot_detection rows.
--
-- Scope: X only. Reddit posts/comments don't have an author field we surface
-- at ingest time, so Reddit stays at per-post scoring.

CREATE TABLE IF NOT EXISTS author_bot_scores (
    platform TEXT NOT NULL CHECK(platform IN ('x', 'reddit')),
    author_id TEXT NOT NULL,
    score REAL NOT NULL,                        -- mean of post-level aggregated_score
    variance REAL,                               -- cross-post variance (low ~= LLM uniformity)
    sample_count INTEGER NOT NULL,
    bot_post_count INTEGER NOT NULL DEFAULT 0,  -- posts labeled 'bot'
    suspicious_post_count INTEGER NOT NULL DEFAULT 0,
    llm_text_likelihood_mean REAL,              -- mean across posts, if LLM path returned it
    stylometric_features_json TEXT,             -- mean features across account
    updated_at INTEGER NOT NULL,
    PRIMARY KEY (platform, author_id)
);

CREATE INDEX IF NOT EXISTS idx_author_bot_scores_score ON author_bot_scores(score);
CREATE INDEX IF NOT EXISTS idx_author_bot_scores_bot_count ON author_bot_scores(bot_post_count);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (14, strftime('%s', 'now'));
