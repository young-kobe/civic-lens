-- 017_x_api_budget.sql
-- Persistent per-month spend tracker for the X API. The prepaid credit model
-- already caps absolute overspend (you can't use credits you haven't bought)
-- but within a month a loop bug or misconfigured timer could burn the whole
-- month's budget in a single run. This table lets the runner enforce a soft
-- ceiling per month and abort before hitting it (walkthrough 048).
--
-- One row per calendar-month (UTC). The runner INSERTs on first call of the
-- month and INCREMENTs on each subsequent call.
--
-- No explicit transaction here: the migration runner wraps this file in one
-- (audit D-6).

CREATE TABLE x_api_budget (
    month_key       TEXT PRIMARY KEY,      -- 'YYYY-MM' UTC
    post_count      INTEGER NOT NULL DEFAULT 0,
    user_count      INTEGER NOT NULL DEFAULT 0,
    request_count   INTEGER NOT NULL DEFAULT 0,
    estimated_cents INTEGER NOT NULL DEFAULT 0,
    last_updated    INTEGER NOT NULL DEFAULT 0   -- unix epoch
);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (17, strftime('%s', 'now'));
